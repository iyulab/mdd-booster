using System.Reflection;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// A ratchet over what the parser hands this generator for a field and a model — every property of
/// <see cref="FieldNode"/> and <see cref="ModelNode"/> — the counterpart of <see cref="ModelTargetEnumAxisCoverageTests"/>
/// for the other two nodes.
/// </summary>
/// <remarks>
/// <para>
/// The attribute ratchet (<see cref="ModelTargetAxisCoverageTests"/>) counts the attribute vocabulary, not the AST. A part
/// of the language that arrives as its own property — not as an attribute — sat outside every ratchet: a field's display
/// label (<c>- name(Label): …</c>) was parsed into <see cref="FieldNode.Label"/> and read by nothing, and an array field
/// (<c>string[]</c>) reached every target as its element type, one string column for a list of strings, with nothing
/// said. The enum nodes had this ratchet and caught their new property the moment the parser pin moved.
/// </para>
/// <para>
/// A parser upgrade that adds a property fails here until its disposition is recorded; a property that goes away fails
/// until its entry goes. Recording one is a statement about the generator, so each note says where it is carried, or
/// why it is not.
/// </para>
/// </remarks>
public class ModelTargetAstAxisCoverageTests
{
    private enum Disposition
    {
        /// <summary>The generator acts on it.</summary>
        Carried,

        /// <summary>No target generates it — and the build says so (a diagnostic, or the unconsumed-elements line).</summary>
        Reported,

        /// <summary>The build refuses a model that uses it — not carried, and not dropped in silence.</summary>
        Refused,

        /// <summary>No target reads it, and nothing reports it either — a known, stated gap.</summary>
        UnimplementedEverywhere,

        /// <summary>Source bookkeeping, a discriminator, or a layer the generated code does not model.</summary>
        NotApplicable,
    }

    private static readonly IReadOnlyDictionary<string, (Disposition Disposition, string Note)> FieldAxes =
        new Dictionary<string, (Disposition, string)>(StringComparer.Ordinal)
        {
            ["Name"] = (Disposition.Carried, "the column, property and TypeScript member name"),
            ["Label"] = (Disposition.Carried,
                "`- name(Label): …` — the display name: [Display(Name)], the TypeScript field schema and form labels " +
                "(FieldAttributes.DisplayLabel)"),
            ["Type"] = (Disposition.Carried, "the type mapping of every target; a shape type (enum, map, object) is refused, see Fields"),
            ["Params"] = (Disposition.Carried, "`string(n)`, `decimal(p,s)` — the column's size and the input rules"),
            ["GenericParams"] = (Disposition.Refused, "`map<K, V>`'s key and value types — a map field is refused (MDD048)"),
            ["Nullable"] = (Disposition.Carried, "NULL / NOT NULL, the C# and TypeScript nullability, required-ness"),
            ["Array"] = (Disposition.Carried,
                "`T[]` — a JSON text column held by ISJSON on SQL Server, a native array on PostgreSQL, List<T> on the " +
                "entity, T[] in TypeScript. Only unbounded primitive elements; a bounded or enum element and the " +
                "combinations whose meaning on a list is not decided are refused (MDD048)"),
            ["ArrayItemNullable"] = (Disposition.Carried,
                "`T?[]` — List<T?> and (T | null)[]; without it, PostgreSQL holds the elements non-null with a CHECK"),
            ["Kind"] = (Disposition.Carried, "stored or derived (lookup, rollup, computed) — decides where the value is read from"),
            ["DefaultValue"] = (Disposition.Carried, "`= value` — the column default and the C# initializer (FieldAttributes.EffectiveDefault)"),
            ["DefaultValueType"] = (Disposition.UnimplementedEverywhere,
                "the parser's literal/expression classification of the default. Not read: each target interprets the " +
                "default's text by its own rules"),
            ["Description"] = (Disposition.Carried, "the XML doc comment, and the display name when no Label is written"),
            ["Attributes"] = (Disposition.Carried, "the attribute vocabulary — ratcheted on its own in ModelTargetAxisCoverageTests"),
            ["FrameworkAttrs"] = (Disposition.Reported, "custom framework attributes — listed among the unconsumed elements (AstAccounting)"),
            ["Lookup"] = (Disposition.Carried, "`@lookup(path)` — the read model's joined column"),
            ["ReferenceQualifiedName"] = (Disposition.Carried,
                "the parser's resolution of a key's target — ShortNames rewrites a target written qualified " +
                "(`@reference(core.Asset)`, an alias's) to its short name, unique in a build (MDD053)"),
            ["TypeQualifiedName"] = (Disposition.Carried,
                "the parser's resolution of a type that names a declaration — ShortNames rewrites a qualified one to its short name"),
            ["Rollup"] = (Disposition.Carried, "`@rollup(…)` — the read model's aggregate column"),
            ["Computed"] = (Disposition.Carried, "`@computed(…)` — the read model's expression column"),
            ["Binding"] = (Disposition.Carried, "`# Entity.Column` binding — [Binding] on the entity property"),
            ["EnumValues"] = (Disposition.Refused, "an inline `enum` field's values — refused (MDD048); declare a top-level ::enum"),
            ["Fields"] = (Disposition.Refused, "an `object` field's sub-fields — refused (MDD048); a separate model with a reference"),
            ["Origin"] = (Disposition.Carried, "the ::extend block that brought the field in — the owner group and the extension rules"),
            ["RenamedFrom"] = (Disposition.Carried,
                "`@renamed_from(…)` — the Sql targets' column rename scripts (RenameSteps, PreScriptWriter); not acted on, " +
                "and said so, when no preScriptsPath is configured"),
            ["Loc"] = (Disposition.NotApplicable, "source bookkeeping — used by diagnostics"),
        };

    private static readonly IReadOnlyDictionary<string, (Disposition Disposition, string Note)> ModelAxes =
        new Dictionary<string, (Disposition, string)>(StringComparer.Ordinal)
        {
            ["Name"] = (Disposition.Carried, "the table, entity, entity set and TypeScript names"),
            ["Label"] = (Disposition.Reported,
                "`## Customer(Label)` — no generated artifact carries a display name for a type; listed among the " +
                "unconsumed elements"),
            ["Type"] = (Disposition.NotApplicable, "the discriminator — the parser already files models, interfaces and views apart"),
            ["Source"] = (Disposition.NotApplicable, "source bookkeeping — the declaring file"),
            ["Namespace"] = (Disposition.NotApplicable, "the file's `# Namespace:`. The generated namespace comes from the target's configuration"),
            ["Prefix"] = (Disposition.Carried, "the file's `# Prefix:` owner — the extension rules and the includeOwners/excludeOwners surface"),
            ["Base"] = (Disposition.Carried,
                "`::aspect(Base)` — a table whose key is the base's key. `::subtype` is refused (MDD016): its storage " +
                "strategy is not decided"),
            ["ExtendedBy"] = (Disposition.Carried, "the ::extend blocks — the extension coverage the build prints"),
            ["RenamedFrom"] = (Disposition.Carried,
                "`## Name @renamed_from(…)` — the Sql targets' table rename scripts (RenameSteps, PreScriptWriter); not acted " +
                "on, and said so, when no preScriptsPath is configured"),
            ["Line"] = (Disposition.NotApplicable, "source bookkeeping — used by diagnostics"),
            ["Inherits"] = (Disposition.Carried, "the interfaces a model inherits — flattened into its fields (InterfaceResolver)"),
            ["InheritsQualifiedNames"] = (Disposition.Carried,
                "the parser's resolution of each parent — ShortNames rewrites a parent written qualified or through an alias to its short name"),
            ["QualifiedName"] = (Disposition.Carried,
                "names the declarations MDD053 refuses for sharing a short name — the generated names come from the short name"),
            ["Description"] = (Disposition.Carried, "the XML doc comment of the generated types and the TypeScript interface"),
            ["Attributes"] = (Disposition.Carried, "model-level attributes — read where a target acts on them (the entity surface among them)"),
            ["Fields"] = (Disposition.Carried, "the fields; see the field axes above"),
            ["Sections"] = (Disposition.Carried,
                "`### Indexes` is read; `### Relations`, `### Behaviors`, `### Metadata` and sections the language does " +
                "not define are listed among the unconsumed elements"),
            ["Materialized"] = (Disposition.Refused, "`@materialized` on a view — not generated (MDD031): who refreshes it is not decided"),
            ["Refresh"] = (Disposition.Refused, "a materialized view's refresh — only meaningful on one, which is refused"),
            ["SourceDef"] = (Disposition.Carried, "a standalone view's `### Source` — the view's SQL"),
            ["Loc"] = (Disposition.NotApplicable, "source bookkeeping — used by diagnostics"),
        };

    [Theory]
    [InlineData(typeof(FieldNode))]
    [InlineData(typeof(ModelNode))]
    public void Every_field_and_model_axis_has_a_recorded_disposition(Type astType)
    {
        var recorded = astType == typeof(FieldNode) ? FieldAxes : ModelAxes;

        var declared = astType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var undocumented = declared.Where(n => !recorded.ContainsKey(n)).ToList();
        Assert.True(undocumented.Count == 0,
            $"{astType.Name} 의 축인데 처분이 기록되지 않았다: {string.Join(", ", undocumented)}. " +
            "생성기가 이것으로 무엇을 하는지(또는 왜 아무것도 하지 않는지) 적어야 통과한다.");

        var stale = recorded.Keys.Where(n => !declared.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"{astType.Name} 에 더 이상 없는 축의 처분이 남아 있다: {string.Join(", ", stale)}");
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string fields)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-axis-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n## Doc\n- id: identifier @pk @generated\n" + fields);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new SemanticAnalyzer(new InterfaceResolver(ast).ResolveAll(), ast.Enums).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>The <c>Refused</c> claims, pinned by behaviour: each shape stops the build with MDD048.</summary>
    [Theory]
    [InlineData("- mails: email[]\n", "배열")]
    [InlineData("- status: enum = \"draft\"\n  - draft: \"Draft\"\n", "인라인 `enum`")]
    [InlineData("- meta: map<string, integer>?\n", "`map<K, V>`")]
    [InlineData("- address: object\n  - city: string(50)\n", "`object`")]
    public void A_shape_the_generator_does_not_generate_stops_the_build(string field, string named)
    {
        var diagnostics = Analyze(field);
        var refusal = Assert.Single(diagnostics, d => d.Code == "MDD048");
        Assert.Equal(SemanticSeverity.Error, refusal.Severity);
        Assert.Contains(named, refusal.Message);
        // Not also misdescribed as a type that does not exist.
        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD001");
    }
}
