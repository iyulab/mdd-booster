using MddBooster.Cli.Commands;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Tests.Cli;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// <c>MDD055</c> — a model that declares more than one primary-key column (<c>@primary(1)</c> + <c>@primary(2)</c>).
/// No target produces a composite key: the runtime entity base class carries one <c>Id</c>. Before this check the
/// Model target refused the input, the SQL Server output repeated an inline <c>PRIMARY KEY</c> on each column, and the
/// PostgreSQL output kept the first column and dropped the rest — a build that succeeded with a schema that is not the
/// one the model declares. The build now stops at the model, whichever targets it runs.
/// </summary>
public class CompositePrimaryKeyDiagnosticTests
{
    private const string Composite = """
# Namespace: x

## OrderLine
- order_id: integer @primary(1)
- line_no: integer @primary(2)
- quantity: integer
""";

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-compositepk-{Guid.NewGuid():N}.m3l.md");
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
    public void Two_primary_key_columns_are_refused_naming_the_model_and_both_columns()
    {
        var error = Assert.Single(Analyze(Composite), d => d.Code == "MDD055");

        Assert.Equal(SemanticSeverity.Error, error.Severity);
        Assert.Contains("OrderLine", error.Message);
        Assert.Contains("order_id", error.Message);
        Assert.Contains("line_no", error.Message);
    }

    [Fact]
    public void A_single_primary_key_is_clean()
    {
        var diagnostics = Analyze("""
# Namespace: x

## OrderLine
- id: identifier @pk @generated
- quantity: integer
""");

        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD055");
    }

    [Fact]
    public void A_model_with_no_primary_key_is_not_this_diagnostics_business()
    {
        var diagnostics = Analyze("""
# Namespace: x

## Note
- body: text
""");

        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD055");
    }
}

/// <summary>
/// The refusal holds for every target and both SQL dialects — the one input, one answer.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class CompositePrimaryKeyBuildTests
{
    [Theory]
    [InlineData("""{ "type": "Sql", "projectPath": "../db", "emitSqlProj": false }""", "db")]
    [InlineData("""{ "type": "Sql", "projectPath": "../db", "emitSqlProj": false, "dialect": "postgres" }""", "db")]
    [InlineData("""{ "type": "Model", "projectPath": "../app", "namespace": "App.Domain", "dbContextName": "AppDbContext" }""", "app")]
    public void Every_target_stops_the_build_with_the_same_diagnostic_and_writes_nothing(string target, string outputDir)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-compositepk-{Guid.NewGuid():N}");
        var mdd = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mdd);
        File.WriteAllText(Path.Combine(mdd, "tables.m3l.md"), """
            # Namespace: x

            ## OrderLine
            - order_id: integer @primary(1)
            - line_no: integer @primary(2)
            - created_at: timestamp @not_null
            - updated_at: timestamp @not_null
            """);
        File.WriteAllText(Path.Combine(mdd, "mdd.json"), $$"""
            { "sources": ["./tables.m3l.md"], "targets": [ {{target}} ] }
            """);
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(3, new BuildCommand().Run(mdd));
            Assert.Contains("MDD055", stderr.Text);
            Assert.Contains("OrderLine", stderr.Text);
            Assert.False(Directory.Exists(Path.Combine(root, outputDir)), "nothing is generated");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
