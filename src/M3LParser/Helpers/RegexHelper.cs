namespace M3LParser.Helpers;

/// <summary>
/// Helper methods for regular expressions
/// </summary>
public static class RegexHelper
{
    /// <summary>
    /// Extract a match group value safely
    /// </summary>
    public static string GetGroupValue(Match match, int groupIndex)
    {
        if (!match.Success || groupIndex >= match.Groups.Count)
            return string.Empty;

        return match.Groups[groupIndex].Value;
    }

    /// <summary>
    /// Extract a match group value safely by name
    /// </summary>
    public static string GetGroupValue(Match match, string groupName)
    {
        if (!match.Success || !match.Groups.ContainsKey(groupName))
            return string.Empty;

        return match.Groups[groupName].Value;
    }

    /// <summary>
    /// Extract all named groups from a regex match
    /// </summary>
    public static (bool Success, Dictionary<string, string> Groups) ExtractNamedGroups(string input, string pattern)
    {
        var match = Regex.Match(input, pattern);
        if (!match.Success)
            return (false, new Dictionary<string, string>());

        var result = new Dictionary<string, string>();
        foreach (var groupName in match.Groups.Keys)
        {
            if (!int.TryParse(groupName, out _)) // Skip numeric group names
                result[groupName] = match.Groups[groupName].Value;
        }

        return (true, result);
    }

    /// <summary>
    /// Extract a parameter value from an attribute
    /// </summary>
    public static string ExtractAttributeParameter(string attribute, string attributeName)
    {
        if (string.IsNullOrEmpty(attribute) || !attribute.StartsWith('@' + attributeName))
            return null;

        var match = Regex.Match(attribute, $@"@{attributeName}\(([^)]+)\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Check if an attribute is present (without parameters)
    /// </summary>
    public static bool HasAttribute(string attribute, string attributeName)
    {
        if (string.IsNullOrEmpty(attribute))
            return false;

        return attribute == '@' + attributeName || attribute.StartsWith('@' + attributeName + '(');
    }

    /// <summary>
    /// Check if the attribute is a reference attribute
    /// </summary>
    public static bool IsReferenceAttribute(string attribute)
    {
        if (string.IsNullOrEmpty(attribute))
            return false;

        return HasAttribute(attribute, "reference") || HasAttribute(attribute, "ref");
    }

    /// <summary>
    /// Extract the reference parameter from an attribute
    /// </summary>
    public static string ExtractReferenceParameter(string attribute)
    {
        if (string.IsNullOrEmpty(attribute))
            return null;

        var refParam = ExtractAttributeParameter(attribute, "reference");
        if (refParam != null)
            return refParam;

        return ExtractAttributeParameter(attribute, "ref");
    }

    /// <summary>
    /// Extract cascade behavior from reference attribute
    /// Supports: @reference(User)!, @ref(User)?, @cascade(no-action)
    /// </summary>
    public static string ExtractCascadeBehavior(string attributeText)
    {
        if (string.IsNullOrEmpty(attributeText))
            return "CASCADE"; // default

        // Check for explicit @cascade() attribute
        var cascadeParam = ExtractAttributeParameter(attributeText, "cascade");
        if (!string.IsNullOrEmpty(cascadeParam))
        {
            return cascadeParam.ToUpperInvariant() switch
            {
                "NO-ACTION" or "NOACTION" or "RESTRICT" => "NO ACTION",
                "SET-NULL" or "SETNULL" => "SET NULL",
                "CASCADE" => "CASCADE",
                _ => "CASCADE"
            };
        }

        // Check for suffix symbols on @reference() or @ref()
        if (attributeText.Contains("@reference(") || attributeText.Contains("@ref("))
        {
            if (attributeText.EndsWith(")!"))
                return "NO ACTION";
            else if (attributeText.EndsWith(")?"))
                return "SET NULL";
        }

        return "CASCADE"; // default
    }
}