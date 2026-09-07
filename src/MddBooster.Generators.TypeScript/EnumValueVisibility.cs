using M3L.Native;
using MddBooster.Core.Naming;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// Interprets the enum-value attributes that keep a value out of input choices.
/// </summary>
/// <remarks>
/// M3L records enum value attributes without assigning them meaning — the same
/// arrangement as <c>@display_labels</c>. This is where that meaning is assigned
/// for the TypeScript target. Two attributes land on the same rule for different
/// reasons: <c>@system</c> says a value is written by the system and never picked
/// by a person, <c>@deprecated</c> says a value used to be picked and no longer
/// should be. Neither is a domain concept — every model that outlives its first
/// release acquires values in one state or the other.
/// <para>
/// They stay two names rather than one because the reason is what a reader of the
/// model needs: marking a retired choice <c>@system</c> would say something untrue
/// about who writes it. The generated output is identical, and deliberately so —
/// the rule they share is "not offered for authoring".
/// </para>
/// <para>
/// It constrains <em>authoring</em>, never <em>storage</em>. The value stays in
/// the SQL CHECK constraint, in the C# enum, and in the display label map, so
/// rows already holding it keep rendering. Only the generated form's choice list
/// leaves it out.
/// </para>
/// <para>
/// Both the label renderer and the form renderer route through here so the
/// convention (attribute name, generated map name) has one definition.
/// </para>
/// </remarks>
internal static class EnumValueVisibility
{
    /// <summary>
    /// Attribute names that take a value out of the input choices. Adding one here
    /// is the whole of adding a new reason — every renderer routes through the
    /// predicate below, so no output path has to learn the name.
    /// </summary>
    private static readonly string[] ExcludingAttributes = ["system", "deprecated"];

    internal static bool IsExcludedFromChoices(EnumValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Attributes?.Any(
            a => ExcludingAttributes.Contains(a.Name, StringComparer.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// True when this enum needs a separate input-choice map — i.e. at least one
    /// value is excluded from authoring. Enums where nothing is excluded keep using
    /// their label map directly, so no dead second map is generated.
    /// </summary>
    internal static bool HasExcludedValues(EnumNode enumNode)
    {
        ArgumentNullException.ThrowIfNull(enumNode);
        return enumNode.Values.Any(IsExcludedFromChoices);
    }

    /// <summary>Name of the generated input-choice map for a TS enum type.</summary>
    internal static string SelectableLabelsName(string typeName) => typeName + "SelectableLabels";

    /// <summary>
    /// Name of the generated function a form calls to get its choices.
    /// </summary>
    /// <remarks>
    /// The narrowed map cannot serve the form on its own. The same generated form
    /// binds an existing row (<c>{entity}FromEntity</c>), and a row may already hold
    /// an excluded value — for which the narrowed map has no entry, so the control
    /// would be asked to display a value its options do not contain. The function
    /// puts that one value back, and only while it is the current one.
    /// </remarks>
    internal static string ChoicesFunctionName(string typeName) =>
        NameCasing.ToCamelCase(typeName) + "Choices";

    /// <summary>
    /// TS type names (PascalCase) whose enums carry at least one excluded value.
    /// </summary>
    internal static HashSet<string> TypeNamesWithExcludedValues(IReadOnlyList<EnumNode> enums)
    {
        ArgumentNullException.ThrowIfNull(enums);
        return new HashSet<string>(
            enums.Where(HasExcludedValues).Select(e => NameCasing.ToPascalCase(e.Name)),
            StringComparer.Ordinal);
    }
}
