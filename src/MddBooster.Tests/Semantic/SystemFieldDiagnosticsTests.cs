using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// A field <c>@system</c> says the application's own code writes the value. Two places it cannot mean that: on a field
/// no caller writes in the first place (<c>MDD039</c>, a warning — the declaration changes nothing), and next to
/// <c>@immutable</c>, which says a caller supplies the value once (<c>MDD040</c>, an error — the two disagree about who
/// supplies it).
/// </summary>
public class SystemFieldDiagnosticsTests
{
    private static IReadOnlyList<SemanticDiagnostic> Analyze(string fields)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-system-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, $"""
# Namespace: x

## Account
- id: identifier @pk @generated
- name: string(50)

## Order
- id: identifier @pk @generated
- account_id: identifier @reference(Account)
- quantity: integer
- price: decimal(10,2)
{fields}

## OrderLine
- id: identifier @pk @generated
- order_id: identifier @reference(Order)
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return new SemanticAnalyzer(models, ast.Enums).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void On_a_stored_field_it_is_the_declaration_it_means_to_be()
    {
        var diagnostics = Analyze("- status: string(20) = \"planned\" @system");

        Assert.DoesNotContain(diagnostics, d => d.Code is "MDD039" or "MDD040");
    }

    [Theory]
    [InlineData("- account_name: string @lookup(account_id.name) @system", "lookup")]
    [InlineData("- line_count: integer @rollup(OrderLine.order_id, count) @system", "rollup")]
    [InlineData("- total: decimal(12,2) @computed(\"quantity * price\") @system", "computed")]
    [InlineData("- row_version: rowversion @system", "rowversion")]
    public void On_a_field_no_caller_writes_it_warns_that_it_changes_nothing(string field, string said)
    {
        var diagnostics = Analyze(field);

        var d = Assert.Single(diagnostics, d => d.Code == "MDD039");
        Assert.Equal(SemanticSeverity.Warning, d.Severity);
        Assert.Contains("@system", d.Message);
        Assert.Contains(said, d.Message);
    }

    [Fact]
    public void Next_to_immutable_it_is_an_error()
    {
        var diagnostics = Analyze("- placed_by_id: identifier @reference(Account) @immutable @system");

        var d = Assert.Single(diagnostics, d => d.Code == "MDD040");
        Assert.Equal(SemanticSeverity.Error, d.Severity);
        Assert.Contains("Order.placed_by_id", d.Message);
    }
}
