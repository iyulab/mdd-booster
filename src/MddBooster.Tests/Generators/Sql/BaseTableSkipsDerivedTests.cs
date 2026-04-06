using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// Regression guard for Cycle 22: base tables must only contain stored
/// columns. Lookup/Rollup/Computed fields belong in the _ext view (Phase I)
/// and must be filtered out by TableRenderer.
/// </summary>
public class BaseTableSkipsDerivedTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Base_table_excludes_lookup_rollup_and_computed_columns()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var lookup = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var sql = TableRenderer.Render(order, "dbo", lookup);

        // Stored columns must be present
        Assert.Contains("[OrderNumber]", sql);
        Assert.Contains("[CustomerId]", sql);
        Assert.Contains("[Status]", sql);
        Assert.Contains("[Subtotal]", sql);

        // Derived (view-only) columns must NOT land in the base table
        Assert.DoesNotContain("[CustomerName]", sql);
        Assert.DoesNotContain("[CustomerEmail]", sql);
        Assert.DoesNotContain("[ItemCount]", sql);
        Assert.DoesNotContain("[TotalSum]", sql);
        Assert.DoesNotContain("[TaxAmount]", sql);
        Assert.DoesNotContain("[GrandTotal]", sql);
    }
}
