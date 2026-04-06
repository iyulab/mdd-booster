using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class ReferenceFkTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Render_emits_foreign_key_for_reference_attribute()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains("[CustomerId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id])", sql);
    }
}
