using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class UdViewRendererTests
{
    [Fact]
    public void Renders_create_view_with_deleted_at_filter()
    {
        var sql = UdViewRenderer.Render("Order", "dbo");

        Assert.Contains("CREATE VIEW [dbo].[OrderUdView]", sql);
        Assert.Contains("SELECT * FROM [dbo].[Order]", sql);
        Assert.Contains("WHERE [DeletedAt] IS NULL", sql);
        Assert.Contains("GO", sql);
    }

    [Fact]
    public void Uses_provided_schema()
    {
        var sql = UdViewRenderer.Render("Product", "custom");

        Assert.Contains("CREATE VIEW [custom].[ProductUdView]", sql);
        Assert.Contains("FROM [custom].[Product]", sql);
    }
}
