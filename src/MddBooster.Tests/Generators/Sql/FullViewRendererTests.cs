using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class FullViewRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Renders_create_view_with_left_join_per_unique_fk()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var plan = new ViewPlanner().Plan(order);

        var sql = FullViewRenderer.Render(plan, "dbo");

        Assert.Contains("CREATE VIEW [dbo].[Order_full]", sql);
        Assert.Contains("FROM [dbo].[Order] AS b", sql);
        Assert.Contains("LEFT JOIN [dbo].[Customer] AS j_customer_id ON b.[CustomerId] = j_customer_id.[Id]", sql);
        // Two lookups on the same FK → still one JOIN
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"LEFT JOIN \[dbo\]\.\[Customer\]"));
        // Projected columns use PascalCase names
        Assert.Contains("j_customer_id.[Name] AS [CustomerName]", sql);
        Assert.Contains("j_customer_id.[Email] AS [CustomerEmail]", sql);
    }

    [Fact]
    public void Throws_when_model_does_not_need_full_view()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single();
        var plan = new ViewPlanner().Plan(model);

        Assert.Throws<InvalidOperationException>(() => FullViewRenderer.Render(plan, "dbo"));
    }
}
