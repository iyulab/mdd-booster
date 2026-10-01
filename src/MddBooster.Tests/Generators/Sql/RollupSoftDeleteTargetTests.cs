using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A rollup over a soft-deleting target counts only the target's live rows — whichever of the
/// target's columns it reads. A rollup that reads a derived column goes through the target's
/// FullView, which sits on its UdView and so never saw the deleted rows; a rollup that reads only
/// stored columns reads the base table, and used to count them. Two rollups over the same target
/// then counted two different sets of rows.
/// </summary>
public class RollupSoftDeleteTargetTests
{
    private const string Model =
        "## Order\n" +
        "- id: identifier @pk @generated\n\n" +
        "### Rollup\n" +
        "- item_count: integer @rollup(OrderItem.order_id, count)\n" +
        "- qty_total: integer @rollup(OrderItem.order_id, sum(qty))\n" +
        "- line_total: decimal(18,2) @rollup(OrderItem.order_id, sum(line_amount))\n" +
        "- note_count: integer @rollup(Note.order_id, count)\n\n" +
        "## OrderItem\n" +
        "- id: identifier @pk @generated\n" +
        "- order_id: identifier @reference(Order) @not_null\n" +
        "- qty: integer\n" +
        "- price: decimal(18,2)\n" +
        "- deleted_at: timestamp?\n\n" +
        "### Computed\n" +
        "- line_amount: decimal(18,2) @computed(`qty * price`)\n\n" +
        "## Note\n" +
        "- id: identifier @pk @generated\n" +
        "- order_id: identifier @reference(Order) @not_null\n";

    [Fact]
    public void A_stored_column_rollup_over_a_soft_deleting_target_skips_deleted_rows()
    {
        var sql = RenderOrder();

        Assert.Contains("(SELECT COUNT(*) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id] AND [DeletedAt] IS NULL) AS [ItemCount]", sql);
        Assert.Contains("(SELECT ISNULL(SUM([Qty]), 0) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id] AND [DeletedAt] IS NULL) AS [QtyTotal]", sql);
    }

    [Fact]
    public void A_derived_column_rollup_reads_the_FullView_which_already_excludes_them()
    {
        var sql = RenderOrder();

        Assert.Contains("(SELECT ISNULL(SUM([LineAmount]), 0) FROM [dbo].[OrderItemFullView] WHERE [OrderId] = b.[Id]) AS [LineTotal]", sql);
    }

    [Fact]
    public void A_target_without_soft_delete_is_read_unfiltered()
    {
        var sql = RenderOrder();

        Assert.Contains("(SELECT COUNT(*) FROM [dbo].[Note] WHERE [OrderId] = b.[Id]) AS [NoteCount]", sql);
    }

    [Fact]
    public void The_where_clause_still_applies_after_the_live_row_filter()
    {
        var sql = FullViewRenderer.RenderRollupSubquery(
            new RollupDef { Target = "OrderItem", Fk = "order_id", Aggregate = "count", Where = "qty > 0" },
            "dbo", "b", "Id", derivedFieldsByModel: null, targetSoftDeletes: true);

        Assert.Equal(
            "(SELECT COUNT(*) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id] AND [DeletedAt] IS NULL AND ([Qty] > 0))",
            sql);
    }

    [Fact]
    public void The_postgres_renderer_counts_the_same_rows()
    {
        var models = Load();
        var planner = new ViewPlanner();
        var plans = models.Select(planner.Plan).ToList();
        var tables = new Dictionary<string, string> { ["Order"] = "order", ["OrderItem"] = "order_item", ["Note"] = "note" };
        var views = tables.ToDictionary(t => t.Key, t => t.Value + "_full_view");

        var sql = PgFullViewRenderer.Render(
            plans.Single(p => p.Model.Name == "Order"), "public", tables, views,
            models.ToDictionary(m => m.Name), Derived(plans));

        Assert.Contains("(SELECT COUNT(*) FROM public.order_item WHERE order_id = b.id AND deleted_at IS NULL) AS item_count", sql);
        Assert.Contains("FROM public.order_item_full_view WHERE order_id = b.id) AS line_total", sql);
        Assert.Contains("(SELECT COUNT(*) FROM public.note WHERE order_id = b.id) AS note_count", sql);
    }

    [Fact]
    public void The_UdView_filters_with_the_same_predicate()
    {
        var orderItem = Load().Single(m => m.Name == "OrderItem");

        Assert.Contains("WHERE [DeletedAt] IS NULL", UdViewRenderer.Render(orderItem, "dbo"));
        Assert.Contains("WHERE b.deleted_at IS NULL;", PgUdViewRenderer.Render(orderItem, "public", "order_item", "order_item_ud_view"));
    }

    private static string RenderOrder()
    {
        var models = Load();
        var planner = new ViewPlanner();
        var plans = models.Select(planner.Plan).ToList();
        return FullViewRenderer.Render(plans.Single(p => p.Model.Name == "Order"), "dbo", Derived(plans), models);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Derived(IEnumerable<ViewPlan> plans) =>
        plans.Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

    private static IReadOnlyList<ResolvedModel> Load()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-rollup-softdelete-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + Model);
        try { return new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll(); }
        finally { File.Delete(tmp); }
    }
}
