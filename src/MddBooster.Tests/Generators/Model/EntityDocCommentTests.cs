using MddBooster.Generators.Model;
using MddBooster.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// A model's description — the blockquote under its header — is the documentation a consumer reads on hover,
/// on each of the three types the model becomes. Enums already carried theirs; entity types did not.
/// </summary>
public class EntityDocCommentTests
{
    private const string Documented = """
        ## Order
        > An order & its lines.
        > Totals stay < 1e9.
        - id: identifier @primary
        - total: decimal(12, 2)
        - lines: integer @rollup(OrderLine.order_id, count)

        ## OrderLine
        - id: identifier @primary
        - order_id: identifier @reference(Order)
        """;

    private static EntityPairRenderer.RenderedPair Render(string source, string modelName)
    {
        var (_, models) = TwoFileModel.Load(("m.m3l.md", source));
        return EntityPairRenderer.Render(models.Single(m => m.Name == modelName), "Doc.Entities",
            extBacking: EntityPairRenderer.ExtBacking.Ext, allModels: models);
    }

    /// <summary>The summary on the file's single type declaration, as XML text — and the parser's verdict on it.</summary>
    private static (string? Summary, IReadOnlyList<Diagnostic> Diagnostics) DocOf(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code,
            new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose));
        var type = tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Single();
        var doc = type.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .SingleOrDefault();
        return (doc?.ToFullString(), tree.GetDiagnostics().ToList());
    }

    [Fact]
    public void Each_type_a_model_becomes_carries_its_description()
    {
        var pair = Render(Documented, "Order");

        foreach (var (kind, code) in new[] { ("write", pair.Write), ("read", pair.Read), ("interface", pair.Interface) })
        {
            var (summary, diagnostics) = DocOf(code);
            Assert.True(summary is not null, $"{kind} type has no documentation comment");
            Assert.Contains("<summary>", summary);
            Assert.Contains("/// An order &amp; its lines.", summary);
            Assert.Contains("/// Totals stay &lt; 1e9.", summary);
            // Well-formed XML: no CS1570 (badly formed XML) or CS1587 (misplaced comment).
            Assert.DoesNotContain(diagnostics, d => d.Id is "CS1570" or "CS1587");
        }
    }

    [Fact]
    public void A_model_without_a_description_gets_no_comment()
    {
        var pair = Render(Documented, "OrderLine");
        Assert.Null(DocOf(pair.Write).Summary);
        Assert.Null(DocOf(pair.Read).Summary);
        Assert.Null(DocOf(pair.Interface).Summary);
    }
}
