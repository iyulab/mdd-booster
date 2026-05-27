using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class ViewPlannerTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Table_only_model_needs_no_views()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single();

        var plan = new ViewPlanner().Plan(model);

        Assert.False(plan.NeedsFullView);
        Assert.False(plan.NeedsUdView);
        Assert.False(plan.NeedsAnyView);
        Assert.Empty(plan.Lookups);
        Assert.Empty(plan.Rollups);
        Assert.Empty(plan.Computeds);
    }

    [Fact]
    public void Model_with_lookups_rollups_and_computeds_needs_full_view()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var plan = new ViewPlanner().Plan(order);

        Assert.True(plan.NeedsFullView);
        Assert.False(plan.NeedsUdView);  // no deleted_at in fixture
        Assert.True(plan.NeedsAnyView);
        Assert.Equal(2, plan.Lookups.Count);    // customer_name, customer_email
        Assert.Equal(2, plan.Rollups.Count);    // item_count, total_sum
        Assert.Equal(2, plan.Computeds.Count);  // tax_amount, grand_total
    }

    [Fact]
    public void Model_with_deleted_at_needs_ud_view()
    {
        var tmp = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- deleted_at: timestamp\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var model = new InterfaceResolver(ast).ResolveAll().Single();
            var plan = new ViewPlanner().Plan(model);

            Assert.True(plan.NeedsUdView);
            Assert.False(plan.NeedsFullView);
            Assert.True(plan.NeedsAnyView);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Model_with_deleted_at_and_derived_fields_needs_both_views()
    {
        var tmp = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- bar_id: identifier @reference(Bar) @not_null\n" +
            "- deleted_at: timestamp\n" +
            "- bar_name: string @lookup(bar_id.name)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Foo");
            var plan = new ViewPlanner().Plan(foo);

            Assert.True(plan.NeedsUdView);
            Assert.True(plan.NeedsFullView);
            Assert.True(plan.NeedsAnyView);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Model_with_only_rollup_needs_full_view()
    {
        var tmp = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n\n" +
            "### Rollup\n" +
            "- cnt: integer @rollup(Bar.foo_id, count)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Foo");
            var plan = new ViewPlanner().Plan(foo);

            Assert.True(plan.NeedsFullView);
            Assert.False(plan.NeedsUdView);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Related_models_without_derived_fields_are_classified_as_table_only()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll();
        var planner = new ViewPlanner();

        var customer = planner.Plan(models.Single(m => m.Name == "Customer"));
        var item = planner.Plan(models.Single(m => m.Name == "OrderItem"));

        Assert.False(customer.NeedsAnyView);
        Assert.False(item.NeedsAnyView);
    }

    [Fact]
    public void PlanAll_returns_one_plan_per_model_in_input_order()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll();

        var plans = new ViewPlanner().PlanAll(models);

        Assert.Equal(models.Count, plans.Count);
        Assert.Equal(models.Select(m => m.Name), plans.Select(p => p.Model.Name));
    }

    private static string WriteInlineM3l(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-vp-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        return tmp;
    }
}
