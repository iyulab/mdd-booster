namespace MddBooster.Generators.TypeScript;

/// <summary>
/// The module specifiers a generated form imports from.
/// </summary>
/// <remarks>
/// These were literals inside the renderer, which made the generated file assume
/// a folder layout on the consumer's side. Since the generated file is
/// <c>DO NOT EDIT</c>, the consumer could not correct that assumption — the only
/// way to compile the output was to build the assumed layout.
/// <para>
/// The split between this type's two members is the whole point of it. A
/// specifier that addresses <b>the generator's own output</b> is <i>computed</i>
/// and therefore has no default; a specifier that names a <b>module the
/// generator does not produce</b> is the consumer's to choose.
/// </para>
/// </remarks>
public sealed record TsFormImports
{
    /// <summary>
    /// Module specifier prefix for the generator's own TypeScript output —
    /// e.g. <c>"../types"</c>, yielding <c>'../types/entities_gen'</c>.
    /// </summary>
    /// <remarks>
    /// Required, and deliberately without a default. The correct value follows
    /// from where the caller writes the two file sets, so a default here would be
    /// a guess about someone else's directory layout — exactly the defect this
    /// type exists to remove. <see cref="TypeScriptGenerator"/> derives it.
    /// </remarks>
    public required string GeneratedTypesBase { get; init; }

    /// <summary>Modules the generator does not produce. Consumer-configurable.</summary>
    public TsFormModuleImports Modules { get; init; } = new();
}

/// <summary>
/// Where a generated form gets the things this generator does not write:
/// the layout components, the form controls, and the option-list helper.
/// </summary>
/// <remarks>
/// Every default is the string the generator emitted before these were settings,
/// so a consumer who configures nothing keeps a byte-identical file.
/// <para>
/// <b>No default names a specific downstream package.</b> Which component
/// library to point at is the consumer's decision, and the recommendation
/// belongs in that library's own documentation — a generator that ships one as
/// its default has acquired knowledge of a package it does not depend on. The
/// defaults below are therefore paths into the consumer's own tree, which is
/// what they have always been; they are kept for compatibility, not because
/// this generator endorses that layout.
/// </para>
/// </remarks>
public sealed record TsFormModuleImports
{
    /// <summary>
    /// Supplies <c>FormSection</c> and <c>FormRow</c>.
    /// </summary>
    public string Layout { get; init; } = "@iyulab/enterprise";

    /// <summary>
    /// Supplies <c>UInput</c>, <c>UTextarea</c>, <c>USelect</c> and
    /// <c>UCheckbox</c> — whichever of them the rendered fields actually use.
    /// </summary>
    public string Controls { get; init; } = "../components/ui";

    /// <summary>
    /// Supplies <c>enumToOptions</c>, which turns a generated <c>{Enum}Labels</c>
    /// map into the option list a <c>USelect</c> takes.
    /// </summary>
    /// <remarks>
    /// This generator writes the label maps but not the function that consumes
    /// them, so the module named here is the consumer's to provide.
    /// </remarks>
    public string SelectOptions { get; init; } = "../lib/select-options";
}
