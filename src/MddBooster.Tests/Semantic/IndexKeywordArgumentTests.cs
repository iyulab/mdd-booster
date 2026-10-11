using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// An index's keyword arguments are not columns. The specification's own
/// <c>@index(customer_id, order_date, name: "customer_orders")</c> used to reach this generator with a third column
/// named <c>name: "customer_orders"</c> — uncompilable C# and SQL naming a column that does not exist. The keywords
/// now arrive apart from the columns; <c>fulltext: true</c>, which no target generates, is <c>MDD041</c>.
/// </summary>
public class IndexKeywordArgumentTests
{
    private const string Model = """
# Namespace: x

## Product
- id: identifier @pk @generated
- sku: string(20) @not_null
- name: string(100) @not_null
- @index(sku, name, name: "product_search", fulltext: true)
- @unique(sku, name: "uq_sku")
""";

    private static (IReadOnlyList<ResolvedModel> Models, IReadOnlyList<SemanticDiagnostic> Diagnostics) Load(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-index-kw-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return (models, new SemanticAnalyzer(models, ast.Enums).Analyze());
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Keyword_arguments_are_read_apart_from_the_columns()
    {
        var entries = SectionIndexParser.Parse(Load(Model).Models.Single());

        Assert.Equal(["sku", "name"], entries[0].Columns);
        Assert.Equal("product_search", entries[0].Name);
        Assert.True(entries[0].FullText);
        Assert.Equal(["sku"], entries[1].Columns);
        Assert.Equal("uq_sku", entries[1].Name);
        Assert.False(entries[1].FullText);
    }

    [Fact]
    public void A_full_text_index_is_MDD041_a_warning()
    {
        var diagnostic = Assert.Single(Load(Model).Diagnostics, d => d.Code == "MDD041");

        Assert.Equal(SemanticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("@index(sku, name, fulltext: true)", diagnostic.Message);
    }

    [Fact]
    public void An_ordinary_index_is_not_MDD041()
    {
        var diagnostics = Load(Model.Replace(", fulltext: true", "", StringComparison.Ordinal)).Diagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD041");
    }
}

/// <summary>
/// A directive's <c>name:</c> is the database name of its index — in both SQL dialects and the EF model — while an
/// <c>### Indexes</c> label is not (those indexes are deployed under the generated name). <c>MDD042</c> refuses a
/// name that cannot be one: not an identifier, or used twice.
/// </summary>
public class DeclaredIndexNameTests
{
    private static string Build(string m3l, string target)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdd-index-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "model.m3l.md"), m3l);
            File.WriteAllText(Path.Combine(dir, "mdd.json"), target);
            Assert.Equal(0, new MddBooster.Cli.Commands.BuildCommand().Run(dir));
            return string.Join("\n", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".sql", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private const string Model = """
# Namespace: x

## Product
- id: identifier @pk @generated
- sku: string(20) @not_null
- name: string(100) @not_null
- created_at: timestamp = now()
- updated_at: timestamp = now()
- @index(sku, name, name: "product_search")
- @unique(sku, name: "uq_product_sku")

### Indexes
- by_name: @index(name)
""";

    [Theory]
    [InlineData("tsql", "[product_search]", "[uq_product_sku]", "[IX_Product_Name]")]
    [InlineData("postgres", "product_search", "uq_product_sku", "ix_product_name")]
    public void A_directive_name_names_the_index_and_a_label_does_not(string dialect, string index, string unique, string labelled)
    {
        var output = Build(Model, $$"""
            { "sources": ["model.m3l.md"], "targets": [
              { "type": "Sql", "dialect": "{{dialect}}", "outputPath": "./sql", "emitSqlProj": false },
              { "type": "Model", "dialect": "{{dialect}}", "outputPath": "./model", "namespace": "Probe", "dbContextName": "ProbeDb" } ] }
            """);

        Assert.Contains(index, output);
        Assert.Contains(unique, output);
        Assert.Contains(labelled, output);
        Assert.Contains("HasDatabaseName(\"product_search\")", output);
        Assert.Contains("HasDatabaseName(\"uq_product_sku\")", output);
        Assert.DoesNotContain("by_name", output);
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-index-name-{Guid.NewGuid():N}.m3l.md");
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
    public void A_name_that_is_not_an_identifier_is_MDD042()
    {
        var diagnostic = Assert.Single(Analyze(Model.Replace("product_search", "product search];--", StringComparison.Ordinal)),
            d => d.Code == "MDD042");

        Assert.Equal(SemanticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_name_used_twice_in_the_build_is_MDD042()
    {
        var twice = Model + "\n## Order\n- id: identifier @pk @generated\n- sku: string(20)\n- @index(sku, name: \"PRODUCT_SEARCH\")\n";

        var diagnostic = Assert.Single(Analyze(twice), d => d.Code == "MDD042");

        Assert.Contains("'Product'", diagnostic.Message);
    }
}
