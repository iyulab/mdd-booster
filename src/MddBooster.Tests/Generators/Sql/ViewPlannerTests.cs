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
        Assert.False(plan.NeedsExtView);
        Assert.False(plan.NeedsAnyView);
        Assert.Empty(plan.Lookups);
        Assert.Empty(plan.Rollups);
        Assert.Empty(plan.Computeds);
    }

    [Fact]
    public void Model_with_lookups_rollups_and_computeds_needs_full_and_ext_views()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var plan = new ViewPlanner().Plan(order);

        Assert.True(plan.NeedsFullView);
        Assert.True(plan.NeedsExtView);
        Assert.Equal(2, plan.Lookups.Count);       // customer_name, customer_email
        Assert.Equal(2, plan.Rollups.Count);       // item_count, total_sum
        Assert.Equal(2, plan.Computeds.Count);     // tax_amount, grand_total
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
}
