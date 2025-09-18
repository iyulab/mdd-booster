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
    public static string ExtractCascadeBehavior(string attributeText, bool isFieldNullable = false)
    {
        if (string.IsNullOrEmpty(attributeText))
            return "CASCADE"; // default

        // Check for explicit @cascade() attribute
        var cascadeParam = ExtractAttributeParameter(attributeText, "cascade");
        if (!string.IsNullOrEmpty(cascadeParam))
        {
            return cascadeParam.ToUpperInvariant() switch
            {
                "NO-ACTION" or "NOACTION" => "NO ACTION",
                "RESTRICT" => "RESTRICT",
                "SET-NULL" or "SETNULL" => "SET NULL",
                "CASCADE" => "CASCADE",
                _ => "CASCADE"
            };
        }

        // Check for @no_action attribute
        if (attributeText.Contains("@no_action"))
        {
            return "NO ACTION";
        }

        // Check for @cascade attribute
        if (attributeText.Contains("@cascade"))
        {
            return "CASCADE";
        }

        // Check for @set_null attribute
        if (attributeText.Contains("@set_null"))
        {
            return "SET NULL";
        }

        // Check for @restrict attribute
        if (attributeText.Contains("@restrict"))
        {
            return "RESTRICT";
        }

        // Check for suffix symbols on @reference() or @ref()
        if (attributeText.Contains("@reference(") || attributeText.Contains("@ref("))
        {
            // Handle comments by looking for )!! or )! or )? before any comment marker
            if (attributeText.Contains(")!!"))
                return "RESTRICT";
            else if (attributeText.Contains(")!"))
                return "NO ACTION";
            else if (attributeText.Contains(")?"))
                return "SET NULL";
        }

        // Automatic decision logic for @reference(Model) without explicit symbols
        if (attributeText.Contains("@reference(") || attributeText.Contains("@ref("))
        {
            var cascadeBehavior = DetermineAutomaticCascadeBehavior(attributeText, isFieldNullable);
            if (!string.IsNullOrEmpty(cascadeBehavior))
            {
                return cascadeBehavior;
            }
        }

        return "CASCADE"; // default
    }

    /// <summary>
    /// Determine automatic cascade behavior based on field nullability and business logic
    /// </summary>
    private static string DetermineAutomaticCascadeBehavior(string attributeText, bool isFieldNullable = false, bool isReferencedPrimaryKeyNullable = false)
    {
        // This method is called when @reference(Model) has no explicit symbol
        // Decision logic based on nullable status, not hardcoded entity names

        // Rule 1: If FK field is nullable, prefer SET NULL (safe cleanup)
        if (isFieldNullable)
        {
            return "SET NULL";
        }

        // Rule 2: If FK is not nullable but references nullable PK, use NO ACTION (prevent orphans)
        if (!isFieldNullable && isReferencedPrimaryKeyNullable)
        {
            return "NO ACTION";
        }

        // Rule 3: If both FK and referenced PK are not nullable, use CASCADE (strong relationship)
        if (!isFieldNullable && !isReferencedPrimaryKeyNullable)
        {
            return "CASCADE";
        }

        // Default fallback to CASCADE for standard parent-child relationships
        return "CASCADE";
    }
}