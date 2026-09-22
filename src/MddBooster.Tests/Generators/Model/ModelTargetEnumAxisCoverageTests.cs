using System.Reflection;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The enum counterpart of <see cref="ModelTargetAxisCoverageTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// That ratchet enumerates <c>FieldAttributes.KnownNames</c>, so it covers every
/// declaration written as <c>@name(...)</c> on a field — and nothing else. An enum
/// declares itself differently: a member's label is the quoted text after its name,
/// its stored value is <c>= 100</c>, and the enum's parent is <c>: BasicStatus</c>.
/// None of those is an attribute, so none of them was inside any ratchet, and an axis
/// carried by one target and not another produced no signal anywhere.
/// </para>
/// <para>
/// That is not hypothetical. A consumer reported the member label reaching the
/// TypeScript label map and stopping at an XML doc comment on the C# side, which is
/// exactly the defect class the field ratchet was built for, on an axis it could not
/// see. This is that signal for the enum axis.
/// </para>
/// <para>
/// The vocabulary here is the AST itself — the public properties of
/// <see cref="EnumNode"/> and <see cref="EnumValue"/>. That set grows when the pinned
/// parser grows, which fails this test until the author records what the Model target
/// does with the new axis. As with the field ratchet, the point is not that every axis
/// must be carried, but that not carrying one has to be a stated choice.
/// </para>
/// <para>
/// Enum-value <em>attributes</em> (<c>@system</c>, <c>@deprecated</c>) are deliberately
/// not ratcheted per name: M3L records them without assigning meaning and the set is
/// open, so there is no closed vocabulary to enumerate. They are covered here as the
/// single <c>Attributes</c> axis.
/// </para>
/// </remarks>
public class ModelTargetEnumAxisCoverageTests
{
    private enum Disposition
    {
        /// <summary>The Model target acts on this axis.</summary>
        Carried,

        /// <summary>
        /// Another target acts on it and the Model target does not, though a C#
        /// equivalent exists. This is the defect class the ratchet exists for.
        /// </summary>
        AsymmetricGap,

        /// <summary>No target consumes it.</summary>
        UnimplementedEverywhere,

        /// <summary>Source bookkeeping, or a layer a C# enum does not model.</summary>
        NotApplicable,
    }

    private static readonly IReadOnlyDictionary<string, (Disposition Disposition, string Note)> ValueAxes =
        new Dictionary<string, (Disposition, string)>(StringComparer.Ordinal)
        {
            ["Name"] = (Disposition.Carried,
                "the PascalCase member name, plus [EnumMember(Value)] carrying the declared snake_case form. " +
                "Every target reads it — the SQL CHECK literal and the TypeScript union member are the same text"),
            ["Description"] = (Disposition.Carried,
                "[Display(Name)] and the member's XML doc comment. The attribute is the run-time half: a doc " +
                "comment is compiled away, so a host rendering a document server-side had no way back to the " +
                "declared text while the TypeScript label map had carried it all along. Omitted when the member " +
                "declares no label, matching the field path's [Display] rule. Only the inline quoted text " +
                "reaches this property: an indented blockquote under a member does not attach to it the way " +
                "one under a field does (measured), so the attribute never carries a paragraph — and the " +
                "question of how the two forms combine, open for fields, does not arise here"),
            ["Attributes"] = (Disposition.AsymmetricGap,
                "@system and @deprecated reach the TypeScript target, which drops those values from a generated " +
                "form's choices (EnumValueVisibility). The C# enum emits nothing for either. That decision is " +
                "settled for [Obsolete] specifically, and settled as no: the specification states that an " +
                "attribute constrains authoring and never storage, and that the value remains a fully valid " +
                "stored value in every case, while [Obsolete] is a compile-time constraint on every use " +
                "including reading a row that still holds it — a consumer building with warnings-as-errors " +
                "would stop compiling. The asymmetry itself stays open and stays recorded here; what form a C# " +
                "counterpart should take (a compile-inert marker, or the mirror of the TypeScript choices " +
                "function) waits on a consumer that actually needs the distinction on that axis, because the " +
                "form follows how it is consumed. The reason is stated in the README next to the TypeScript " +
                "output, which is where a consumer looking for the C# half will be"),
            ["Type"] = (Disposition.UnimplementedEverywhere,
                "the per-member value type of `- pending: integer = 100`. No target reads it; see Value"),
            ["Value"] = (Disposition.UnimplementedEverywhere,
                "the explicit stored value of `- pending: integer = 100`. No target reads it, and the axis is " +
                "inert rather than merely unimplemented: enums persist as their declared name (EF's " +
                "HasConversion<string>, and the SQL CHECK literal where that opt-in is on), so a declared " +
                "number has nowhere to land. The generated " +
                "C# member takes its ordinal instead, which is not the declared number"),
        };

    private static readonly IReadOnlyDictionary<string, (Disposition Disposition, string Note)> EnumAxes =
        new Dictionary<string, (Disposition, string)>(StringComparer.Ordinal)
        {
            ["Name"] = (Disposition.Carried, "the generated type name, PascalCased"),
            ["Values"] = (Disposition.Carried, "the members; see the per-value axes above"),
            ["Description"] = (Disposition.Carried, "the type's XML doc comment"),
            ["Inherits"] = (Disposition.UnimplementedEverywhere,
                "`## ExtendedStatus ::enum : BasicStatus`. No target reads it, and the measured consequence " +
                "is that the generated enum silently lacks the inherited members — Values holds only what the " +
                "block itself declares, so nothing upstream of this renderer has flattened them in either. " +
                "No longer silent: the loader accounting reports the parent list as an unread axis, because an " +
                "artifact that is wrong is worse than one that was never produced. Flattening belongs where the " +
                "specification defines inheritance as a union of values, which is not this repository — so what " +
                "changes here when that lands is this note, not the disposition"),
            ["Label"] = (Disposition.UnimplementedEverywhere,
                "a display name for the enum type itself. No target reads it; the type's own label has no " +
                "counterpart in any generated artifact, unlike a member's"),
            ["Namespace"] = (Disposition.NotApplicable,
                "the source file's `# Namespace:`. The generated namespace comes from the target's own " +
                "configuration, not from the model"),
            ["Prefix"] = (Disposition.NotApplicable,
                "the source file's `# Prefix:` owner. Read by the extension rules over models, not by enum " +
                "rendering — an owner prefix qualifies which file may extend what, and names no C# construct"),
            ["Type"] = (Disposition.NotApplicable, "the discriminator that makes this an enum at all"),
            ["Source"] = (Disposition.NotApplicable, "source bookkeeping — the declaring file"),
            ["Line"] = (Disposition.NotApplicable, "source bookkeeping — used by diagnostics"),
            ["Loc"] = (Disposition.NotApplicable, "source bookkeeping — used by diagnostics"),
        };

    /// <summary>
    /// The ratchet. A parser upgrade that adds an enum axis fails here until its
    /// Model-target disposition is recorded; an axis that goes away fails until its
    /// entry goes.
    /// </summary>
    [Theory]
    [InlineData(typeof(EnumValue))]
    [InlineData(typeof(EnumNode))]
    public void Every_enum_axis_has_a_recorded_model_target_disposition(Type astType)
    {
        var recorded = astType == typeof(EnumValue) ? ValueAxes : EnumAxes;

        var declared = astType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var undocumented = declared.Where(n => !recorded.ContainsKey(n)).ToList();
        Assert.True(undocumented.Count == 0,
            $"{astType.Name} 의 축인데 처분이 기록되지 않았다: {string.Join(", ", undocumented)}. " +
            "Model 타깃이 이것으로 무엇을 하는지(또는 왜 아무것도 하지 않는지) 적어야 통과한다.");

        var stale = recorded.Keys.Where(n => !declared.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"{astType.Name} 에 더 이상 없는 축의 처분이 남아 있다: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// The two <c>UnimplementedEverywhere</c> claims above, pinned by behaviour rather
    /// than by reading the code that would have to change.
    /// </summary>
    /// <remarks>
    /// A note asserting absence cannot be checked by reading a document — the same
    /// reason the bundled spec copy's absence notes are pinned by generating a model and
    /// looking at the output. If either axis starts being carried, these fail and the
    /// disposition above is what has to move.
    /// </remarks>
    [Fact]
    public void Declared_member_values_and_enum_inheritance_leave_no_trace_in_the_generated_enum()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "enum-unread-axes.m3l.md"));

        var numbered = EnumRenderer.Render(ast.Enums.Single(e => e.Name == "Numbered"), "Probe");
        Assert.DoesNotContain("100", numbered, StringComparison.Ordinal);
        Assert.DoesNotContain("200", numbered, StringComparison.Ordinal);

        // No member carries an explicit value at all, so the declared numbers are not
        // merely renumbered — they are gone, and the members take their ordinals.
        // Asserted over the member lines alone: [Display(Name = …)] on the line above
        // contains an `=` of its own.
        var memberLines = numbered.Split('\n')
            .Select(l => l.TrimEnd('\r', ',').Trim())
            .Where(l => l is "Pending" or "Shipped" || l.StartsWith("Pending", StringComparison.Ordinal)
                        || l.StartsWith("Shipped", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, memberLines.Count);
        Assert.All(memberLines, l => Assert.DoesNotContain("=", l, StringComparison.Ordinal));

        var extended = EnumRenderer.Render(ast.Enums.Single(e => e.Name == "ExtendedStatus"), "Probe");
        Assert.Contains("Archived", extended, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft", extended, StringComparison.Ordinal);
        Assert.DoesNotContain("Done", extended, StringComparison.Ordinal);
    }
}
