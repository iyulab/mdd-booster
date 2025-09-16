namespace M3LParser.Helpers;

/// <summary>
/// Utility methods for pattern matching and extraction
/// </summary>
public static class PatternMatchingHelper
{
    /// <summary>
    /// Match model definition pattern and extract groups
    /// </summary>
    public static (string Name, string Label, string Inheritance, string Attributes, string Description)
        MatchModelDefinition(string line)
    {
        var match = RegexPatterns.ModelDefinitionPattern.Match(line);
        if (!match.Success)
            return (string.Empty, null, null, null, null);

        return (
            RegexHelper.GetGroupValue(match, 1).Trim(),
            RegexHelper.GetGroupValue(match, 2).Trim(),
            RegexHelper.GetGroupValue(match, 3).Trim(),
            RegexHelper.GetGroupValue(match, 4).Trim(),
            RegexHelper.GetGroupValue(match, 5).Trim()
        );
    }

    /// <summary>
    /// Match special definition pattern (interface, enum) and extract groups
    /// </summary>
    public static (string Name, string Inheritance, string Type, string Description)
        MatchSpecialDefinition(string line)
    {
        var match = RegexPatterns.SpecialDefinitionPattern.Match(line);
        if (!match.Success)
            return (string.Empty, null, null, null);

        return (
            RegexHelper.GetGroupValue(match, 1).Trim(),
            RegexHelper.GetGroupValue(match, 2).Trim(),
            RegexHelper.GetGroupValue(match, 3).Trim(),
            RegexHelper.GetGroupValue(match, 4).Trim()
        );
    }

    /// <summary>
    /// Match field definition pattern and extract groups
    /// </summary>
    public static (string Name, string Type, string Length, bool IsNullable, string DefaultValue, string Attributes, string FrameworkAttrs)
        MatchFieldDefinition(string line)
    {
        var match = RegexPatterns.FieldDefinitionPattern.Match(line);
        if (!match.Success)
            return (string.Empty, string.Empty, null, false, null, null, null);

        bool isNullable = !string.IsNullOrEmpty(RegexHelper.GetGroupValue(match, 4));

        return (
            RegexHelper.GetGroupValue(match, 1).Trim(),
            RegexHelper.GetGroupValue(match, 2).Trim(),
            RegexHelper.GetGroupValue(match, 3).Trim(),
            isNullable,
            RegexHelper.GetGroupValue(match, 5).Trim(),
            RegexHelper.GetGroupValue(match, 6).Trim(),
            RegexHelper.GetGroupValue(match, 7).Trim()
        );
    }
}