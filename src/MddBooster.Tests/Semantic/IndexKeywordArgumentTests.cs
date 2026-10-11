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
