namespace MDDBooster.Helpers;

/// <summary>
/// Helper for working with field data and patterns across all builders
/// </summary>
public static class FieldHelper
{
    /// <summary>
    /// Checks if a field contains sensitive data
    /// </summary>
    public static bool IsSensitiveField(MDDField field)
    {
        if (field?.BaseField == null)
            return false;

        // Check extended metadata
        if (field.ExtendedMetadata.ContainsKey("Sensitive") ||
            field.ExtendedMetadata.ContainsKey("JsonIgnore"))
        {
            return true;
        }

        // Use pattern detection for sensitive field names
        var sensitivePatterns = new[] { "password", "secret", "token", "key", "hash", "salt", "credential" };
        string fieldName = field.BaseField.Name.ToLowerInvariant();

        return sensitivePatterns.Any(pattern => fieldName.Contains(pattern));
    }

    /// <summary>
    /// Checks if a field is computed/calculated
    /// </summary>
    public static bool IsComputedField(MDDField field)
    {
        if (field?.BaseField == null)
            return false;

        return field.ExtendedMetadata.ContainsKey("Computed") &&
               (bool)field.ExtendedMetadata["Computed"];
    }

    /// <summary>
    /// Checks if a field should be excluded from DTOs
    /// </summary>
    public static bool IsExcludedFromDto(MDDField field)
    {
        if (field?.BaseField == null)
            return false;

        return field.ExtendedMetadata.ContainsKey("ExcludeFromDto") &&
               (bool)field.ExtendedMetadata["ExcludeFromDto"];
    }

    /// <summary>
    /// Checks if a field is read-only
    /// </summary>
    public static bool IsReadOnlyField(MDDField field)
    {
        if (field?.BaseField == null)
            return false;

        return field.ExtendedMetadata.ContainsKey("ReadOnly") &&
               (bool)field.ExtendedMetadata["ReadOnly"];
    }

    /// <summary>
    /// Gets the data type from field metadata or infers from name patterns
    /// </summary>
    public static string GetDataType(MDDField field)
    {
        if (field?.BaseField == null)
            return null;

        // Check extended metadata first
        if (field.ExtendedMetadata.TryGetValue("DataType", out var metadataDataType))
        {
            return metadataDataType.ToString();
        }

        // Infer from field name patterns
        string fieldName = field.BaseField.Name.ToLowerInvariant();

        if (fieldName.Contains("email"))
            return "EmailAddress";
        if (fieldName.Contains("phone"))
            return "PhoneNumber";
        if (fieldName.Contains("url") || fieldName.Contains("uri"))
            return "Url";
        if (fieldName.Contains("password"))
            return "Password";

        // Infer from field type
        if (string.IsNullOrEmpty(field.BaseField.Type))
            return null;

        return field.BaseField.Type.ToLowerInvariant() switch
        {
            "datetime" or "timestamp" => GetDateTimeDataType(fieldName),
            "date" => "Date",
            _ => null
        };
    }

    /// <summary>
    /// Extracts attribute name from attribute string
    /// </summary>
    public static string ExtractAttributeName(string attributeText)
    {
        if (string.IsNullOrEmpty(attributeText))
            return string.Empty;

        int parenIndex = attributeText.IndexOf('(');
        return parenIndex > 0 ? attributeText.Substring(0, parenIndex).Trim() : attributeText.Trim();
    }

    /// <summary>
    /// Extracts attribute value from attribute string
    /// </summary>
    public static string ExtractAttributeValue(string attributeText)
    {
        if (string.IsNullOrEmpty(attributeText))
            return string.Empty;

        // Match content inside parentheses with quotes
        var match = System.Text.RegularExpressions.Regex.Match(attributeText, @"\(""([^""]+)""\)");
        if (match.Success)
            return match.Groups[1].Value;

        // Match content inside parentheses without quotes
        match = System.Text.RegularExpressions.Regex.Match(attributeText, @"\(([^)]+)\)");
        if (match.Success)
            return match.Groups[1].Value.Trim().Trim('"');

        return string.Empty;
    }

    private static string GetDateTimeDataType(string fieldName)
    {
        // Use pattern detection to determine if it's date-only or includes time
        var dateOnlyIndicators = new[] { "date", "birthday", "anniversary" };
        var timeIncluded = new[] { "time", "timestamp", "created", "updated", "modified" };

        if (dateOnlyIndicators.Any(indicator => fieldName.Contains(indicator)) &&
            !timeIncluded.Any(indicator => fieldName.Contains(indicator)))
        {
            return "Date";
        }

        return "DateTime";
    }
}