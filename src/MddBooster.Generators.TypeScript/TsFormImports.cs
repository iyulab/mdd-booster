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
/// specifier that addresses <b>the generator's own output</b> is <i>computed</i>;
/// a specifier that names a <b>module the generator does not produce</b> is the
/// consumer's to supply. Neither has a default.
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

    /// <summary>Modules the generator does not produce. Consumer-supplied, all three.</summary>
    public required TsFormModuleImports Modules { get; init; }
}

/// <summary>
/// Where a generated form gets the things this generator does not write:
/// the layout components, the form controls, and the option-list helper.
/// </summary>
/// <remarks>
/// <b>None of the three has a default</b>, for the same reason
/// <see cref="TsFormImports.GeneratedTypesBase"/> has none. A default could only
/// be a guess — at a folder layout in the consumer's tree, or at a component
/// library this generator does not depend on. The first is wrong for every
/// consumer laid out differently; the second is knowledge of a package the
/// generator has no business knowing, and it turns every configuration-free
/// build into a compile error the day that package reorganises its exports.
/// Which library to point at is the consumer's decision, and the recommendation
/// belongs in that library's own documentation.
/// </remarks>
public sealed record TsFormModuleImports
{
    /// <summary>
    /// Supplies <c>FormSection</c> and <c>FormRow</c>.
    /// </summary>
    public required string Layout { get; init; }

    /// <summary>
    /// Supplies <c>UInput</c>, <c>UTextarea</c>, <c>USelect</c> and
    /// <c>UCheckbox</c> — whichever of them the rendered fields actually use.
    /// </summary>
    public required string Controls { get; init; }

    /// <summary>
    /// Supplies <c>enumToOptions</c>, which turns a generated <c>{Enum}Labels</c>
    /// map into the option list a <c>USelect</c> takes.
    /// </summary>
    /// <remarks>
    /// This generator writes the label maps but not the function that consumes
    /// them, so the module named here is the consumer's to provide.
    /// </remarks>
    public required string SelectOptions { get; init; }
}
