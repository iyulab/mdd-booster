using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// Two ways a <c>rowversion</c> field compiles and still defeats its purpose. <c>MDD025</c>: a row
/// version marked <c>@internal</c> drops out of the read type, so the set loses its ETag and every
/// conditional write with it. <c>MDD026</c>: a lookup that reads another row's version — the value
/// identifies a version of that row, nothing about this one, and on PostgreSQL there is no column to
/// join to.
/// </summary>
public class RowVersionDiagnosticsTests
{
    private static IReadOnlyList<SemanticDiagnostic> Analyze(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-rowversion-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return new SemanticAnalyzer(models, ast.Enums).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void A_row_version_on_the_read_surface_is_clean()
    {
        var diagnostics = Analyze("""
# Namespace: x

## Order
- id: identifier @pk @generated
- row_version: rowversion
""");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_internal_row_version_warns_that_the_set_loses_its_ETag()
    {
        var diagnostics = Analyze("""
# Namespace: x

## Order
- id: identifier @pk @generated
- row_version: rowversion @internal
""");

        var d = Assert.Single(diagnostics, d => d.Code == "MDD025");
        Assert.Equal(SemanticSeverity.Warning, d.Severity);
        Assert.Contains("Order.row_version", d.Message);
        Assert.Contains("ETag", d.Message);
    }

    [Fact]
    public void A_lookup_reading_another_rows_version_is_an_error()
    {
        var diagnostics = Analyze("""
# Namespace: x

## Customer
- id: identifier @pk @generated
- row_version: rowversion

## Order
- id: identifier @pk @generated
- customer_id: identifier @reference(Customer)
- customer_version: rowversion @lookup(customer_id.row_version)
""");

        var d = Assert.Single(diagnostics, d => d.Code == "MDD026");
        Assert.Equal(SemanticSeverity.Error, d.Severity);
        Assert.Contains("Order.customer_version", d.Message);
        Assert.Contains("Customer.row_version", d.Message);
    }
}
