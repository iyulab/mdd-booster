using Humanizer;

namespace MDDBooster.Builders.ModelProject.Helpers;

/// <summary>
/// Helper for generating navigation property names specific to model project generation
/// Uses Humanizer for intelligent naming and minimizes hardcoding
/// </summary>
public static class NavigationPropertyHelper
{
    /// <summary>
    /// Configuration for navigation property naming
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Custom field pattern mappings that can be added at runtime
        /// </summary>
        public static Dictionary<string, string> CustomFieldPatterns { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Custom role patterns that should be preserved as-is
        /// </summary>
        public static HashSet<string> PreservedRolePatterns { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether to use Humanizer for intelligent naming
        /// </summary>
        public static bool UseHumanizerIntelligence { get; set; } = true;

        /// <summary>
        /// Whether to prefer descriptive names over model names
        /// </summary>
        public static bool PreferDescriptiveNames { get; set; } = true;
    }

    /// <summary>
    /// Gets navigation property name from target model name and field name
    /// </summary>
    public static string GetNavigationPropertyName(string targetModelName, string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(targetModelName))
            return targetModelName ?? "Unknown";

        // Step 1: Check custom patterns first
        var customResult = CheckCustomPatterns(fieldName);
        if (!string.IsNullOrEmpty(customResult))
        {
            return customResult;
        }

        // Step 2: Extract base name by removing ID suffixes
        string baseName = ExtractBaseName(fieldName);

        // Step 3: If base name matches target exactly, use target model
        if (string.Equals(baseName, targetModelName, StringComparison.OrdinalIgnoreCase))
        {
            return targetModelName;
        }

        // Step 4: Use Humanizer intelligence if enabled
        if (Config.UseHumanizerIntelligence)
        {
            var humanizedResult = ApplyHumanizerIntelligence(fieldName, targetModelName, baseName);
            if (!string.IsNullOrEmpty(humanizedResult))
            {
                return humanizedResult;
            }
        }

        // Step 5: Default to meaningful base name or target model
        return GetMeaningfulName(baseName, targetModelName);
    }

    /// <summary>
    /// Gets back reference property name for collections
    /// </summary>
    public static string GetBackReferencePropertyName(string modelName, string fieldName)
    {
        // If field name contains model name, use field-based naming
        if (fieldName.Contains(modelName, StringComparison.OrdinalIgnoreCase))
        {
            return fieldName.Pluralize();
        }

        // Otherwise use model-based collection naming
        return GetCollectionPropertyName(modelName);
    }

    /// <summary>
    /// Gets collection property name using Humanizer
    /// </summary>
    public static string GetCollectionPropertyName(string modelName) => modelName.Pluralize();

    /// <summary>
    /// Adds a custom field pattern mapping
    /// </summary>
    public static void AddCustomFieldPattern(string fieldPattern, string propertyName)
    {
        if (!string.IsNullOrEmpty(fieldPattern) && !string.IsNullOrEmpty(propertyName))
        {
            Config.CustomFieldPatterns[fieldPattern] = propertyName;
        }
    }

    /// <summary>
    /// Adds a role pattern that should be preserved as-is
    /// </summary>
    public static void AddPreservedRolePattern(string rolePattern)
    {
        if (!string.IsNullOrEmpty(rolePattern))
        {
            Config.PreservedRolePatterns.Add(rolePattern);
        }
    }

    /// <summary>
    /// Initialize with common patterns for model projects
    /// </summary>
    public static void InitializeCommonPatterns()
    {
        // Add common preserved role patterns for model generation
        var commonRoles = new[]
        {
            "CreatedBy", "UpdatedBy", "ModifiedBy", "DeletedBy",
            "Owner", "Manager", "Supervisor", "Leader",
            "Author", "Editor", "Reviewer", "Approver",
            "Assignee", "Reporter", "Requester", "Requestee",
            "Parent", "Child", "Member", "Admin"
        };

        foreach (var role in commonRoles)
        {
            AddPreservedRolePattern(role);
        }
    }

    private static string CheckCustomPatterns(string fieldName)
    {
        // Check for exact matches first
        if (Config.CustomFieldPatterns.TryGetValue(fieldName, out var exactMatch))
        {
            return exactMatch;
        }

        // Check for pattern matches (starts with)
        foreach (var kvp in Config.CustomFieldPatterns)
        {
            if (fieldName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static string ExtractBaseName(string fieldName)
    {
        // Use Humanizer's intelligence to handle various ID suffix patterns
        var commonIdSuffixes = new[] { "_id", "Id", "_ID", "ID" };

        foreach (var suffix in commonIdSuffixes)
        {
            if (fieldName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var baseName = fieldName.Substring(0, fieldName.Length - suffix.Length);
                return string.IsNullOrEmpty(baseName) ? fieldName : baseName;
            }
        }

        return fieldName;
    }

    private static string ApplyHumanizerIntelligence(string fieldName, string targetModelName, string baseName)
    {
        // Step 1: Check if we should preserve certain role patterns
        foreach (var preservedPattern in Config.PreservedRolePatterns)
        {
            if (baseName.Equals(preservedPattern, StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith(preservedPattern, StringComparison.OrdinalIgnoreCase))
            {
                return preservedPattern.Pascalize();
            }
        }

        // Step 2: Use Humanizer to detect relationship patterns
        var relationshipResult = DetectRelationshipPattern(fieldName, baseName);
        if (!string.IsNullOrEmpty(relationshipResult))
        {
            return relationshipResult;
        }

        // Step 3: Check if field contains target model name
        if (fieldName.Contains(targetModelName, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractMeaningfulPart(fieldName, targetModelName);
        }

        // Step 4: Use Humanizer to humanize the base name
        if (!string.IsNullOrEmpty(baseName) && Config.PreferDescriptiveNames)
        {
            // Convert to human readable form, then back to Pascal case
            return baseName.Humanize().Pascalize();
        }

        return null;
    }

    private static string DetectRelationshipPattern(string fieldName, string baseName)
    {
        // Use Humanizer's intelligence to detect common relationship patterns
        var relationshipSuffixes = new[] { "By", "er", "or", "ee", "Owner", "Manager" };

        foreach (var suffix in relationshipSuffixes)
        {
            if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Preserve meaningful relationship names
                return baseName.Pascalize();
            }
        }

        // Detect action-based relationships (Created, Updated, etc.)
        var actionPrefixes = new[] { "Created", "Updated", "Modified", "Deleted", "Assigned" };

        foreach (var prefix in actionPrefixes)
        {
            if (baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return baseName.Pascalize();
            }
        }

        return null;
    }

    private static string ExtractMeaningfulPart(string fieldName, string targetModelName)
    {
        int idx = fieldName.IndexOf(targetModelName, StringComparison.OrdinalIgnoreCase);

        if (idx == 0)
        {
            // Target model is at the beginning, use it as-is
            return targetModelName.Pascalize();
        }

        if (idx > 0)
        {
            // Extract prefix and combine with target model
            string prefix = fieldName.Substring(0, idx);
            return (prefix + targetModelName).Pascalize();
        }

        // Target model is at the end or middle, use the whole field name
        return fieldName.Replace("Id", "").Replace("_id", "").Pascalize();
    }

    private static string GetMeaningfulName(string baseName, string targetModelName)
    {
        if (string.IsNullOrEmpty(baseName))
        {
            return targetModelName.Pascalize();
        }

        // If base name is more descriptive than target model, use it
        if (Config.PreferDescriptiveNames && baseName.Length > 2)
        {
            return baseName.Pascalize();
        }

        // Otherwise, use target model name
        return targetModelName.Pascalize();
    }
}