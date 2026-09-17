using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.TypeScript;
using MddBooster.Tests.Generators.TypeScript;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators;

/// <summary>
/// A multi-line field description is valid M3L (an indented blockquote, spec §4.2.6), and the
/// generators use the description as a display label. Every target that writes that text into a
/// string literal must still emit source that compiles.
/// </summary>
public class MultiLineModelTextTests
{
    private const string Model = """
        ## Order
        - name: string(50) "Product name"
          > Shown on invoices.
          > Second line.
        - status: OrderStatus

        ## OrderStatus ::enum
        - draft: "Customer's draft"
        - done: "Done"
        """;

    private static (IReadOnlyList<ResolvedModel> Models, M3lAst Ast) Load()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-mlt-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + Model);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return (new InterfaceResolver(ast).ResolveAll(), ast);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Model_target_emits_compilable_Display_attribute()
    {
        var (models, ast) = Load();
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);

        var pair = EntityPairRenderer.Render(models.Single(m => m.Name == "Order"), "Test.Ns", enumNames);

        Assert.Contains("Name = \"Shown on invoices.\\nSecond line.\"", pair.Write);
        foreach (var source in new[] { pair.Interface, pair.Write, pair.Read })
        {
            var errors = CSharpSyntaxTree.ParseText(source).GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Field_schema_label_is_an_escaped_single_line_literal()
    {
        var (models, _) = Load();

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("label: 'Shown on invoices.\\nSecond line.'", result);
        Assert.DoesNotContain("Shown on invoices.\n", result);
    }

    [Fact]
    public void Form_label_moves_to_an_expression_container_when_it_spans_lines()
    {
        var (models, ast) = Load();

        var content = TsFormRenderer.RenderAll(models, ast.Enums, FormImportFixtures.TestImports)["Order"];

        Assert.Contains("label={'Shown on invoices.\\nSecond line.'}", content);
        Assert.DoesNotContain("Shown on invoices.\n", content);
    }

    [Fact]
    public void Enum_labels_keep_their_existing_quote_escaping()
    {
        var (_, ast) = Load();

        var result = TsEnumLabelsRenderer.RenderAll(ast.Enums);

        Assert.Contains("  draft: 'Customer\\'s draft',", result);
    }
}
