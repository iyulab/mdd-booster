using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class UdViewRendererTests
{
    private static ResolvedModel Resolve(string body, string name)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-ud-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == name);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Renders_create_view_with_explicit_columns_and_deleted_at_filter()
    {
        var order = Resolve(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- order_number: string(30) @not_null\n" +
            "- deleted_at: timestamp\n", "Order");

        var sql = UdViewRenderer.Render(order, "dbo");

        Assert.Contains("CREATE VIEW [dbo].[OrderUdView]", sql);
        // Explicit base columns (declaration order), not SELECT * — so the view text
        // tracks the table's column set for declarative diff tools.
        Assert.Contains("SELECT [Id], [OrderNumber], [DeletedAt] FROM [dbo].[Order]", sql);
        Assert.DoesNotContain("SELECT * FROM [dbo].[Order]", sql);
        Assert.Contains("WHERE [DeletedAt] IS NULL", sql);
        Assert.Contains("GO", sql);
    }

    [Fact]
    public void Uses_provided_schema()
    {
        var product = Resolve(
            "## Product\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n", "Product");

        var sql = UdViewRenderer.Render(product, "custom");

        Assert.Contains("CREATE VIEW [custom].[ProductUdView]", sql);
        Assert.Contains("FROM [custom].[Product]", sql);
    }
}
