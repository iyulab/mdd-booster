using MddBooster.Core.Ast;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// A ratchet over the declaration vocabulary, not a test of any one emission.
/// </summary>
/// <remarks>
/// <para>
/// Three separate declarations turned out to reach the SQL and TypeScript
/// targets but not the C# entity, and they were found one at a time by
/// consumers hitting them in production. Fixing the three changes nothing about
/// the fourth: nothing in the codebase enumerated the axes, so an axis added to
/// one target and forgotten in another produced no signal anywhere.
/// </para>
/// <para>
/// This test is that signal. Every name in <see cref="FieldAttributes.KnownNames"/>
/// must carry a recorded disposition saying what the Model target does with it.
/// Adding vocabulary grows that set, which fails the test until the author
/// records a decision — the point is not that every declaration must be carried,
/// but that not carrying one has to be a stated choice rather than an oversight.
/// </para>
/// <para>
/// <b>What this does not cover.</b> Type parameters (<c>string(n)</c>,
/// <c>decimal(p,s)</c>) are not attributes and so are absent from
/// <c>KnownNames</c>; the ratchet cannot see a new one. Those two axes are held
/// by the field-for-field correspondence in
/// <see cref="FieldConstraintRenderTests"/> instead. A third type parameter
/// would need its own guard.
/// </para>
/// </remarks>
public class ModelTargetAxisCoverageTests
{
    private enum Disposition
    {
        /// <summary>The Model target acts on this declaration.</summary>
        Carried,

        /// <summary>
        /// Another target acts on it and the Model target does not, though a C#
        /// equivalent exists. This is the defect class the ratchet exists for —
        /// entries here are known asymmetries, not accidents.
        /// </summary>
        AsymmetricGap,

        /// <summary>
        /// No target consumes it. A vocabulary entry that is parsed and then
        /// dropped everywhere is not a Model-target asymmetry.
        /// </summary>
        UnimplementedEverywhere,

        /// <summary>
        /// Concerns a layer the C# entity does not model — presentation, API
        /// surface, identifier naming, or database-side value generation.
        /// </summary>
        NotApplicable,
    }

    private static readonly IReadOnlyDictionary<string, (Disposition Disposition, string Note)> Dispositions =
        new Dictionary<string, (Disposition, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- carried ----
            ["pk"] = (Disposition.Carried, "the property is elided — the base entity supplies Id"),
            ["primary"] = (Disposition.Carried, "spelling of pk; same elision"),
            ["not_null"] = (Disposition.Carried, "normalised into field nullability, which drives [Required] on reference types"),
            ["default"] = (Disposition.Carried, "property initializer, for types with a C# literal form"),
            ["reference"] = (Disposition.Carried, "[Reference] plus an EF navigation property on the write entity"),
            ["computed"] = (Disposition.Carried, "[Computed]"),
            ["lookup"] = (Disposition.Carried, "[Lookup]"),
            ["rollup"] = (Disposition.Carried, "[Rollup]"),
            ["indexed"] = (Disposition.Carried, "[Rollup(Indexed = true)]"),
            ["group"] = (Disposition.Carried, "[Display(GroupName)]"),
            ["implements"] = (Disposition.Carried, "additional interfaces on the generated classes"),
            ["inherits"] = (Disposition.Carried, "base class override"),
            ["binding"] = (Disposition.Carried, "[Binding]"),

            // ---- asymmetric: another target carries it, this one does not ----
            ["unique"] = (Disposition.AsymmetricGap, "SQL emits the constraint; EF HasIndex().IsUnique() would be the counterpart. Informational only — it cannot pre-empt a duplicate without a round trip"),
            ["index"] = (Disposition.AsymmetricGap, "SQL emits the index; EF HasIndex() would be the counterpart"),
            ["min"] = (Disposition.AsymmetricGap, "reaches the TypeScript field schema; [Range] would be the C# counterpart"),
            ["max"] = (Disposition.AsymmetricGap, "reaches the TypeScript field schema; [Range] would be the C# counterpart"),
            ["help"] = (Disposition.AsymmetricGap, "reaches the generated form; [Display(Description)] would be the C# counterpart"),

            // ---- parsed, then dropped by every target ----
            ["fk"] = (Disposition.UnimplementedEverywhere, "@reference is the spelling the generators read"),
            ["on_delete"] = (Disposition.UnimplementedEverywhere, "referential action; EF DeleteBehavior would be the counterpart, on the context rather than the entity"),
            ["on_update"] = (Disposition.UnimplementedEverywhere, "as on_delete"),
            ["immutable"] = (Disposition.UnimplementedEverywhere, "[Editable(false)] would be the counterpart"),
            ["validate"] = (Disposition.UnimplementedEverywhere, "[RegularExpression] or a custom attribute would be the counterpart"),
            ["computed_raw"] = (Disposition.UnimplementedEverywhere, "raw-expression variant of computed; only the parsed computed form is read"),
            ["label"] = (Disposition.UnimplementedEverywhere, "[Display(Name)] is taken from the field description instead"),
            ["searchable"] = (Disposition.UnimplementedEverywhere, "query-surface hint with no entity-level counterpart"),
            ["visibility"] = (Disposition.UnimplementedEverywhere, "presentation hint with no entity-level counterpart"),

            // ---- layers the entity does not model ----
            ["generated"] = (Disposition.NotApplicable, "database-side value generation; the key it qualifies is elided"),
            ["from"] = (Disposition.NotApplicable, "identifier naming"),
            ["internal"] = (Disposition.NotApplicable, "excludes an entity from the data API surface, not from entity generation"),
            ["slot"] = (Disposition.NotApplicable, "form layout"),
            ["display_labels"] = (Disposition.NotApplicable, "form layout"),
            ["system"] = (Disposition.NotApplicable, "enum value visibility in the generated UI"),
        };

    /// <summary>
    /// The ratchet. New vocabulary fails here until its Model-target disposition
    /// is recorded; vocabulary that is removed fails here until its entry goes.
    /// </summary>
    [Fact]
    public void Every_known_declaration_has_a_recorded_model_target_disposition()
    {
        var undocumented = FieldAttributes.KnownNames
            .Where(n => !Dispositions.ContainsKey(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var stale = Dispositions.Keys
            .Where(n => !FieldAttributes.KnownNames.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(undocumented);
        Assert.Empty(stale);
    }

    /// <summary>
    /// Pins the asymmetries themselves, so that carrying one to the Model target
    /// — or introducing a new one — is an edit somebody makes deliberately
    /// rather than a state nobody notices. These are the declarations a consumer
    /// can see honoured in one generated artifact and missing from another.
    /// </summary>
    [Fact]
    public void Known_asymmetries_between_targets_are_the_ones_recorded()
    {
        var asymmetric = Dispositions
            .Where(kv => kv.Value.Disposition == Disposition.AsymmetricGap)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["help", "index", "max", "min", "unique"], asymmetric);
    }

    /// <summary>
    /// A disposition without a reason is a box ticked, not a decision recorded.
    /// </summary>
    [Fact]
    public void Every_disposition_states_a_reason()
    {
        Assert.All(Dispositions, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value.Note), kv.Key));
    }
}
