using M3L.Native;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// Interprets the <c>@system</c> attribute on an M3L enum value.
/// </summary>
/// <remarks>
/// M3L records enum value attributes without assigning them meaning — the same
/// arrangement as <c>@display_labels</c>. This is where that meaning is assigned
/// for the TypeScript target: a value marked <c>@system</c> is written by the
/// system, never picked by a person.
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
    private const string SystemAttribute = "system";

    internal static bool IsSystemValue(EnumValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Attributes?.Any(
            a => string.Equals(a.Name, SystemAttribute, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// True when this enum needs a separate input-choice map — i.e. at least one
    /// value is system-only. Enums where nothing is excluded keep using their
    /// label map directly, so no dead second map is generated.
    /// </summary>
    internal static bool HasSystemValues(EnumNode enumNode)
    {
        ArgumentNullException.ThrowIfNull(enumNode);
        return enumNode.Values.Any(IsSystemValue);
    }

    /// <summary>Name of the generated input-choice map for a TS enum type.</summary>
    internal static string SelectableLabelsName(string typeName) => typeName + "SelectableLabels";

    /// <summary>
    /// TS type names (PascalCase) whose enums carry at least one <c>@system</c> value.
    /// </summary>
    internal static HashSet<string> TypeNamesWithSystemValues(IReadOnlyList<EnumNode> enums)
    {
        ArgumentNullException.ThrowIfNull(enums);
        return new HashSet<string>(
            enums.Where(HasSystemValues).Select(e => TypeScriptTypeMapper.PascalCase(e.Name)),
            StringComparer.Ordinal);
    }
}
