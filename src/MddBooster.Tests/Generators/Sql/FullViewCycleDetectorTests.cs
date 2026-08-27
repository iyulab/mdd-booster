using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// Regression coverage for docket #101 — a chained Lookup and a reverse Rollup can each pass
/// <see cref="FullViewRenderer"/>'s own per-model redirection check and still add up to a
/// cross-model cycle that neither render call can see by itself. These tests exercise the
/// cross-model graph directly; <see cref="FullViewRendererTests"/> covers the (non-cyclic)
/// per-model redirection decisions this detector reuses.
/// </summary>
public class FullViewCycleDetectorTests
{
    // Mirrors SqlGenerator's construction — model name → PascalCase names of that
    // model's own Lookup/Rollup/Computed fields.
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> DerivedFieldsByModel(
        IEnumerable<ResolvedModel> models, ViewPlanner planner) =>
        models.Select(planner.Plan)
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

    private static string WriteInlineM3l(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-fvcycle-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        return tmp;
    }

    [Fact]
    public void Detect_finds_the_cycle_when_a_rollup_source_and_a_chained_lookup_reference_each_other()
    {
        // Order.supply_total rolls up OrderItem.line_total — a Computed field, so the subquery
        // must source OrderItemFullView. OrderItem.customer_name chains through Order's own
        // Lookup field of the same name, so its JOIN must target OrderFullView. Independently
        // each redirection is correct; together they form Order ⇄ OrderItem (docket #101).
        var tmp = WriteInlineM3l(
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- enterprise_id: identifier @reference(Enterprise) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(enterprise_id.name)\n\n" +
            "### Rollup\n" +
            "- supply_total: decimal(12,0) @rollup(OrderItem.order_id, sum(line_total))\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(order_id.customer_name)\n\n" +
            "### Computed\n" +
            "- line_total: decimal(12,0) @computed(`0`)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);
            var plans = models.Select(planner.Plan).ToList();

            var cycle = FullViewCycleDetector.Detect(plans, derivedFieldsByModel);

            Assert.NotNull(cycle);
            Assert.Contains(cycle!, step => step.Model == "Order");
            Assert.Contains(cycle, step => step.Model == "OrderItem");
            // A cycle path starts and ends on the same node.
            Assert.Equal(cycle[0].Model, cycle[^1].Model);
            // The first hop has no incoming edge; every later hop names the field that redirected
            // the previous model's FullView to this one.
            Assert.Null(cycle[0].Via);
            Assert.All(cycle.Skip(1), step => Assert.NotNull(step.Via));
            Assert.Contains(cycle, step => step.Via == "Order.SupplyTotal rollup");
            Assert.Contains(cycle, step => step.Via == "OrderItem.CustomerName lookup");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Detect_returns_null_when_lookups_and_rollups_only_chain_in_one_direction()
    {
        // The three-model chain Enterprise -> Order -> OrderItem (each hop a Lookup reading the
        // previous hop's derived column, no Rollup pointing back) is exactly the shape
        // FullViewRendererTests.Same_named_lookup_column_reused_at_two_chain_depths_does_not_conflict
        // already renders without error — the graph must stay acyclic here too.
        var tmp = WriteInlineM3l(
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- enterprise_id: identifier @reference(Enterprise) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(enterprise_id.name)\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(order_id.customer_name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);
            var plans = models.Select(planner.Plan).ToList();

            var cycle = FullViewCycleDetector.Detect(plans, derivedFieldsByModel);

            Assert.Null(cycle);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Detect_returns_null_when_rollup_and_reverse_lookup_only_touch_raw_columns()
    {
        // Mirrors FullViewRendererTests.Rollup_and_reverse_lookup_over_raw_columns_do_not_create_a_view_cycle
        // (the 0.12.3 regression) at the graph level: a count rollup and a raw-column lookup
        // between the same two models must not be reported as a cycle.
        var tmp = WriteInlineM3l(
            "## Parent\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "### Rollup\n" +
            "- child_count: integer @rollup(Child.parent_id, count)\n\n" +
            "## Child\n" +
            "- id: identifier @pk @generated\n" +
            "- parent_id: identifier @reference(Parent) @not_null\n\n" +
            "### Lookup\n" +
            "- parent_name: string @lookup(parent_id.name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);
            var plans = models.Select(planner.Plan).ToList();

            var cycle = FullViewCycleDetector.Detect(plans, derivedFieldsByModel);

            Assert.Null(cycle);
        }
        finally { File.Delete(tmp); }
    }
}
