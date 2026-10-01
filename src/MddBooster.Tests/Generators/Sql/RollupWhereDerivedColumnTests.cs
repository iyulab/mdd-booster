using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A rollup's <c>where:</c> may name a column the target has only on its FullView — one of the
/// target's own Lookup/Rollup/Computed fields. The subquery must then read that FullView, exactly
/// as it already does when the aggregated field is derived: the base table has no such column, so
/// reading it there builds cleanly and fails when the view is deployed.
/// </summary>
public class RollupWhereDerivedColumnTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> OrderItemDerived =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["OrderItem"] = new HashSet<string> { "EffectiveChannel", "OrderChannel" },
        };

    private static RollupDef Count(string where) =>
        new() { Target = "OrderItem", Fk = "order_id", Aggregate = "count", Where = where };

    [Fact]
    public void A_where_naming_a_derived_column_of_the_target_reads_its_FullView()
    {
        var sql = FullViewRenderer.RenderRollupSubquery(
            Count("effective_channel = 'partner'"), "dbo", "b", "Id", OrderItemDerived);

        Assert.Equal(
            "(SELECT COUNT(*) FROM [dbo].[OrderItemFullView] WHERE [OrderId] = b.[Id] AND ([EffectiveChannel] = 'partner'))",
            sql);
    }

    [Fact]
    public void A_bracketed_derived_column_counts_too()
    {
        var sql = FullViewRenderer.RenderRollupSubquery(
            Count("[EffectiveChannel] IS NOT NULL"), "dbo", "b", "Id", OrderItemDerived);

        Assert.Contains("FROM [dbo].[OrderItemFullView]", sql);
    }

    /// <summary>
    /// Only the target's own columns decide. A string literal, a parent reference (either spelling)
    /// and a raw target column all leave the subquery on the base table — reading a view needlessly
    /// is what once built a cycle out of two models that never read each other's derived columns.
    /// </summary>
    [Theory]
    [InlineData("channel = 'effective_channel'")]
    [InlineData("channel = $parent.effective_channel")]
    [InlineData("channel = b.effective_channel")]
    [InlineData("channel = b.[EffectiveChannel]")]
    [InlineData("channel IS NOT NULL")]
    public void A_where_naming_no_derived_target_column_stays_on_the_base_table(string where)
    {
        var sql = FullViewRenderer.RenderRollupSubquery(Count(where), "dbo", "b", "Id", OrderItemDerived);

        Assert.StartsWith("(SELECT COUNT(*) FROM [dbo].[OrderItem] WHERE", sql);
    }

    [Fact]
    public void The_postgres_renderer_asks_the_same_question()
    {
        var sql = PgFullViewRenderer.RenderRollupSubquery(
            Count("effective_channel = 'partner'"), "public", "b", "id",
            tableNameMap: new Dictionary<string, string> { ["OrderItem"] = "order_item" },
            viewNameMap: new Dictionary<string, string> { ["OrderItem"] = "order_item_full_view" },
            derivedFieldsByModel: OrderItemDerived);

        Assert.Equal(
            "(SELECT COUNT(*) FROM public.order_item_full_view WHERE order_id = b.id AND (effective_channel = 'partner'))",
            sql);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> DerivedFieldsByModel(
        IEnumerable<ResolvedModel> models, ViewPlanner planner) =>
        models.Select(planner.Plan)
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

    private static IReadOnlyList<ResolvedModel> Load(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-rollup-where-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try { return new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll(); }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// The reported shape end to end: a child Computed over its own Lookup of the parent, and a
    /// parent rollup filtering on that Computed. The child reads the parent's raw column, so the two
    /// FullViews do not depend on each other.
    /// </summary>
    [Fact]
    public void A_parent_rollup_filtering_on_a_child_computed_reads_the_child_FullView_without_a_cycle()
    {
        var models = Load(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- channel: string(20)\n\n" +
            "### Rollup\n" +
            "- partner_count: integer @rollup(OrderItem.order_id, count, where: \"effective_channel = 'partner'\")\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n" +
            "- channel: string(20)\n\n" +
            "### Lookup\n" +
            "- order_channel: string @lookup(order_id.channel)\n\n" +
            "### Computed\n" +
            "- effective_channel: string @computed(`COALESCE(channel, order_channel)`)\n");
        var planner = new ViewPlanner();
        var derived = DerivedFieldsByModel(models, planner);
        var plans = models.Select(planner.Plan).ToList();

        var orderSql = FullViewRenderer.Render(plans.Single(p => p.Model.Name == "Order"), "dbo", derived, models);

        Assert.Contains("FROM [dbo].[OrderItemFullView] WHERE [OrderId] = b.[Id] AND ([EffectiveChannel] = 'partner')", orderSql);
        Assert.Null(FullViewCycleDetector.Detect(plans, derived));
    }

    /// <summary>
    /// The cycle detector must see the edge the renderer now takes — otherwise a pair the renderer
    /// turns into ParentFullView ⇄ ChildFullView would reach the database (SQL72009) unreported.
    /// </summary>
    [Fact]
    public void The_cycle_detector_sees_an_edge_that_only_the_where_creates()
    {
        var models = Load(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- channel: string(20)\n\n" +
            "### Rollup\n" +
            "- partner_count: integer @rollup(OrderItem.order_id, count, where: \"effective_channel = 'x'\")\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- order_partner_count: integer @lookup(order_id.partner_count)\n\n" +
            "### Computed\n" +
            "- effective_channel: string @computed(`'x'`)\n");
        var planner = new ViewPlanner();
        var derived = DerivedFieldsByModel(models, planner);
        var plans = models.Select(planner.Plan).ToList();

        var cycle = FullViewCycleDetector.Detect(plans, derived);

        Assert.NotNull(cycle);
        Assert.Contains(cycle!, step => step.Via == "Order.PartnerCount rollup");
    }
}
