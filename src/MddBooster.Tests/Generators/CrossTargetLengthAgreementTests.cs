using System.Text.RegularExpressions;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Core.Types;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.TypeScript;
using static MddBooster.Tests.Generators.TypeScript.FormImportFixtures;

namespace MddBooster.Tests.Generators;

/// <summary>
/// One field's length ceiling reaches four artifacts — the column, the entity's
/// validation attribute, the field schema a client validates against, and the
/// input the user types into. They are produced by four renderers that a reader
/// is unlikely to open at the same time, so a disagreement between them is
/// invisible in review and shows up only as a request that one layer accepts
/// and the next rejects.
/// </summary>
/// <remarks>
/// Per-target tests cannot catch this: each one passes while describing a
/// different number. The defect this pins had exactly that shape — the column
/// was sized from the type, the attribute from the type <em>parameter</em>, and
/// a type carrying a bound without a parameter reached only the column.
/// </remarks>
public class CrossTargetLengthAgreementTests
{
    private const string Fixture = "field-constraints.m3l.md";

    /// <summary>
    /// Fields whose bound is implied by their type rather than written into the
    /// declaration. Restricting the assertion to these keeps it pointed at the
    /// axis that was missing, rather than re-testing the declared-bound path
    /// that already had coverage.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ImpliedBoundFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactEmail"] = "email",
            ["ContactPhone"] = "phone",
            ["HomePage"] = "url",
        };

    private static (M3lAst Ast, IReadOnlyList<ResolvedModel> Models) Load()
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", Fixture));
        return (ast, new InterfaceResolver(ast).ResolveAll());
    }

    private static int? FirstCapturedInt(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// The attribute lines preceding each rendered property. Matching attribute
    /// and property in one expression would break the moment another attribute
    /// is emitted between them, which says nothing about the length axis.
    /// </summary>
    private static Dictionary<string, List<string>> AttributesByProperty(string rendered)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pending = new List<string>();
        foreach (var raw in rendered.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                pending.Add(line);
                continue;
            }
            var match = Regex.Match(line, @"^public [\w\.<>\[\]:]+\??\s+(\w+) \{ get; set; \}");
            if (match.Success)
                result[match.Groups[1].Value] = [.. pending];
            pending.Clear();
        }
        return result;
    }

    /// <summary>The single rendered control bound to <paramref name="property"/>.</summary>
    private static string ControlFor(string form, string property) =>
        form.Split('\n').Single(l => l.Contains($"form.{property} ", StringComparison.Ordinal));

    [Fact]
    public void A_bound_implied_by_the_type_reaches_every_target_with_the_same_value()
    {
        var (ast, models) = Load();
        var model = models.Single(m => m.Name == "Sample");
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);
        var enumLookup = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var entityAttrs = AttributesByProperty(EntityPairRenderer.Render(model, "Test.Entities", enumNames).Write);
        var schema = TsFieldSchemaRenderer.RenderAll(models);
        var form = TsFormRenderer.RenderAll(models, ast.Enums, TestImports)["Sample"];

        foreach (var (property, m3lType) in ImpliedBoundFields)
        {
            var expected = M3lPrimitives.ImplicitMaxLength[m3lType];
            var field = model.Fields.Single(f => NameCasing.ToPascalCase(f.Name) == property);

            // The column is the reference point: it is the layer that actually
            // rejects an over-long value, so the others are right only insofar
            // as they agree with it.
            var column = ColumnRenderer.Render(field, enumLookup);
            Assert.Equal(expected, FirstCapturedInt(column, @"NVARCHAR\((\d+)\)"));

            Assert.Contains($"[StringLength({expected})]", entityAttrs[property]);

            Assert.Equal(expected, FirstCapturedInt(
                schema, $@"{property}: \{{[^}}]*maxLength: (\d+)"));

            Assert.Equal(expected, FirstCapturedInt(
                ControlFor(form, property), @"maxlength=\{(\d+)\}"));
        }
    }

    /// <summary>
    /// Guards the assertion above against passing vacuously: if the fixture
    /// stopped declaring these fields, every lookup would simply find nothing.
    /// </summary>
    [Fact]
    public void The_fixture_actually_declares_a_field_for_each_implied_bound_type()
    {
        var (_, models) = Load();
        var model = models.Single(m => m.Name == "Sample");

        foreach (var (property, m3lType) in ImpliedBoundFields)
        {
            var field = model.Fields.Single(f => NameCasing.ToPascalCase(f.Name) == property);
            Assert.Equal(m3lType, field.Type);
            Assert.Null(FieldAttributes.StringMaxLength(field));   // the bound is not written down
            Assert.NotNull(FieldAttributes.EffectiveMaxLength(field));
        }
    }

    /// <summary>
    /// Every type carrying an implicit bound is exercised above. Adding one to
    /// the table without adding a field here would leave the new type's four
    /// artifacts unchecked while the suite still looked green.
    /// </summary>
    [Fact]
    public void Every_type_with_an_implicit_bound_is_covered_by_this_test()
    {
        Assert.Equal(
            M3lPrimitives.ImplicitMaxLength.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ImpliedBoundFields.Values.OrderBy(k => k, StringComparer.Ordinal));
    }
}
