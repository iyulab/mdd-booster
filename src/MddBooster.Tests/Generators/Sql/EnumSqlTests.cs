using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class EnumSqlTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Enum_typed_columns_emit_nvarchar_with_check_constraint()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-enum.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var lookup = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var sql = TableRenderer.Render(order, "dbo", lookup);

        // status is non-null OrderStatus with 5 values; longest is 'in_production' (13 chars)
        Assert.Contains("[Status] NVARCHAR(20) NOT NULL", sql);
        // CHECK constraints removed — EF Core EnumMemberConverter handles validation.
        // SSDT dacpac cannot deploy CHECK constraints idempotently (WITH NOCHECK issue).
        Assert.DoesNotContain("CHECK", sql);

        // priority is nullable Priority with values low/normal/high — still typed as NVARCHAR
        Assert.Contains("[Priority] NVARCHAR(20) NULL", sql);
    }

    [Fact]
    public void Missing_enum_lookup_does_not_affect_primitive_columns()
    {
        // Backward-compat guard: existing callers that don't pass enumLookup
        // must still produce identical output for pure-primitive tables.
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single();

        var withoutLookup = TableRenderer.Render(model, "dbo");
        var withEmptyLookup = TableRenderer.Render(model, "dbo", new Dictionary<string, M3L.Native.EnumNode>());

        Assert.Equal(withoutLookup, withEmptyLookup);
    }
}
