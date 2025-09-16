namespace M3LParser.Helpers;

/// <summary>
/// Static class containing regex patterns for parsing
/// </summary>
public static class RegexPatterns
{
    /// <summary>
    /// Pattern to match a model definition line
    /// </summary>
    public static readonly Regex ModelDefinitionPattern = new Regex(
        @"^##\s+([^(:@#]+)(?:\s*\(([^)]+)\))?(?:\s*:\s*([^@#]+))?(?:\s*@(.+))?(?:\s*#(.+))?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match an interface or enum definition line
    /// </summary>
    public static readonly Regex SpecialDefinitionPattern = new Regex(
        @"^##\s+([^:]+)(?:\s*:\s*([^:]+))?(?:\s*::(\w+))(?:\s*#(.+))?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match a field definition
    /// </summary>
    public static readonly Regex FieldDefinitionPattern = new Regex(
        @"^-\s+([^:]+):\s*([^\s=@\[]+)(?:\(([^)]+)\))?(\?)?\s*(?:=\s*([^@\[]+))?(?:\s*@(.+))?(?:\s*\[(.+)\])?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match an enum value
    /// </summary>
    public static readonly Regex EnumValuePattern = new Regex(
        @"^-\s+([^:]+)(?::\s*(?:(\w+)\s*=\s*([^""]+))?(?:\s*""([^""]+)"")?)?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match a relation definition
    /// </summary>
    public static readonly Regex RelationPattern = new Regex(
        @"^-\s+([<>])([^""]+)(?:\s+""([^""]+)"")?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match a model-level relation
    /// </summary>
    public static readonly Regex ModelLevelRelationPattern = new Regex(
        @"^-\s+@relation\(([^,]+),\s*([<>]-)\s*([^,)]+)(?:,\s*from:\s*([^)]+))?(?:,\s*(.+))?\)(?:\s+""([^""]+)"")?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match a model-level index
    /// </summary>
    public static readonly Regex ModelLevelIndexPattern = new Regex(
        @"^-\s+@(index|unique)\(([^)]+)\)(?:\s+""([^""]+)"")?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pattern to match a metadata definition
    /// </summary>
    public static readonly Regex MetadataPattern = new Regex(
        @"^-\s+([^:]+)(?::\s*(.+))?$",
        RegexOptions.Compiled
    );
}
