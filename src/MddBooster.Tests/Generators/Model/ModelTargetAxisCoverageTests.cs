using MddBooster.Core.Ast;
using MddBooster.Core.Types;

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
/// <b>A second ratchet, over types.</b> A declaration is not the only thing
/// that can carry a constraint — a type can too, without anything appearing in
/// the text of the field. <c>email</c> is bounded; nothing about the
/// declaration says so. That axis sat outside this ratchet (it is not an
/// attribute) and outside the field-for-field correspondence in
/// <c>FieldConstraintRenderTests</c> (which needed a fixture declaring such a
/// field, and none did), so it reached the column and nowhere else for as long
/// as it existed. The type dispositions below close that, and type parameters
/// (<c>string(n)</c>, <c>decimal(p,s)</c>) are recorded there too.
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
            ["internal"] = (Disposition.Carried, "at field level: dropped from the interface and the Ext (read) class, kept on the write class. The FullView/UdView SELECT drops the same field in the same pass — see FullViewRendererTests/UdViewRendererTests. At model level it stays NotApplicable — that grain is EntitySurface.IsInternal, an Api/TypeScript-surface decision with no entity-generation counterpart"),

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
            ["visibility"] = (Disposition.UnimplementedEverywhere, "a surface axis rather than a presentation one — it decides whether a field is emitted at all, not how it is shown. @internal records the same decision one grain coarser, over a whole entity; no target reads a per-field spelling of it. Carrying it would not be a Model-target edit on its own: the FullView SELECT list and the Ext class it backs have to drop the field in the same pass, or the mapping fails at run time"),

            // ---- layers the entity does not model ----
            ["generated"] = (Disposition.NotApplicable, "database-side value generation; the key it qualifies is elided"),
            ["from"] = (Disposition.NotApplicable, "identifier naming"),
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

    // ---------------------------------------------------------------- types
    //
    // The same question asked of the type vocabulary: what does this type imply
    // beyond the C# type it maps to, and does the implication reach the entity?
    // The mappers already throw on a type they do not know, so absence of a
    // mapping is loud; what is silent is a type that maps fine while the
    // constraint it carries reaches only some targets.

    private enum TypeDisposition
    {
        /// <summary>The type implies no constraint beyond the C# type it maps to.</summary>
        NoImpliedConstraint,

        /// <summary>It implies one, and the Model target carries it.</summary>
        Carried,

        /// <summary>It implies one that reaches another target and not this one.</summary>
        AsymmetricGap,
    }

    private static readonly IReadOnlyDictionary<string, (TypeDisposition Disposition, string Note)> TypeDispositions =
        new Dictionary<string, (TypeDisposition, string)>(StringComparer.Ordinal)
        {
            // ---- constraint-bearing, carried ----
            ["string"] = (TypeDisposition.Carried, "the (n) parameter becomes [StringLength(n)]"),
            ["decimal"] = (TypeDisposition.Carried, "the (p,s) parameters become [Column(TypeName)]"),
            ["email"] = (TypeDisposition.Carried, "bounded by the type rather than a parameter; the bound becomes [StringLength(n)]"),
            ["phone"] = (TypeDisposition.Carried, "as email"),
            ["url"] = (TypeDisposition.Carried, "as email"),

            // ---- constraint-bearing, not carried ----
            ["binary"] = (TypeDisposition.AsymmetricGap, "the (n) parameter sizes the column as VARBINARY(n); [MaxLength(n)] would be the counterpart, and its absence is asserted in FieldConstraintRenderTests"),

            // ---- no implied constraint ----
            ["identifier"] = (TypeDisposition.NoImpliedConstraint, "a key type; the property it names is elided"),
            ["boolean"] = (TypeDisposition.NoImpliedConstraint, "the CLR type is the whole of it"),
            ["integer"] = (TypeDisposition.NoImpliedConstraint, "range is the CLR type's"),
            ["long"] = (TypeDisposition.NoImpliedConstraint, "as integer"),
            ["short"] = (TypeDisposition.NoImpliedConstraint, "as integer"),
            ["byte"] = (TypeDisposition.NoImpliedConstraint, "as integer"),
            ["float"] = (TypeDisposition.NoImpliedConstraint, "as integer"),
            ["double"] = (TypeDisposition.NoImpliedConstraint, "as integer"),
            ["text"] = (TypeDisposition.NoImpliedConstraint, "explicitly unbounded — the column is NVARCHAR(MAX)"),
            ["json"] = (TypeDisposition.NoImpliedConstraint, "unbounded; the shape inside is not modelled"),
            ["date"] = (TypeDisposition.NoImpliedConstraint, "the CLR type is the whole of it"),
            ["time"] = (TypeDisposition.NoImpliedConstraint, "as date"),
            ["timestamp"] = (TypeDisposition.NoImpliedConstraint, "as date"),
            ["datetime"] = (TypeDisposition.NoImpliedConstraint, "deprecated spelling of timestamp"),
        };

    /// <summary>
    /// New primitives fail here until what they imply is recorded; removed ones
    /// fail here until their entry goes.
    /// </summary>
    [Fact]
    public void Every_primitive_has_a_recorded_type_disposition()
    {
        var undocumented = M3lPrimitives.All
            .Where(t => !TypeDispositions.ContainsKey(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var stale = TypeDispositions.Keys
            .Where(t => !M3lPrimitives.All.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(undocumented);
        Assert.Empty(stale);
    }

    /// <summary>
    /// Pins the type-axis gaps, so carrying one — or introducing another — is a
    /// deliberate edit rather than a state nobody notices.
    /// </summary>
    [Fact]
    public void Known_type_axis_gaps_are_the_ones_recorded()
    {
        var gaps = TypeDispositions
            .Where(kv => kv.Value.Disposition == TypeDisposition.AsymmetricGap)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["binary"], gaps);
    }

    /// <summary>
    /// The bridge between this record and the table the generators actually
    /// read. Giving a type an implicit bound without carrying it to the entity
    /// recreates the defect this ratchet was added for, so it cannot be done
    /// without the record disagreeing.
    /// </summary>
    [Fact]
    public void Every_type_with_an_implicit_bound_is_recorded_as_carried()
    {
        var notCarried = M3lPrimitives.ImplicitMaxLength.Keys
            .Where(t => TypeDispositions[t].Disposition != TypeDisposition.Carried)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(notCarried);
    }

    [Fact]
    public void Every_type_disposition_states_a_reason()
    {
        Assert.All(TypeDispositions, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value.Note), kv.Key));
    }
}
