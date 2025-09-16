namespace MDDBooster.Builders.ModelProject.Helpers;

/// <summary>
/// Helper for type conversions specific to model project generation
/// Extends the base functionality for C# model generation
/// </summary>
public static class TypeConversionHelper
{
    /// <summary>
    /// Configuration for model project type conversions
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Custom type mappings for model projects
        /// </summary>
        public static Dictionary<string, string> CustomCSharpTypeMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Custom SQL type mappings for model projects
        /// </summary>
        public static Dictionary<string, string> CustomSqlTypeMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether to use DateTimeOffset instead of DateTime
        /// </summary>
        public static bool UseDateTimeOffset { get; set; } = false;

        /// <summary>
        /// Default string length for SQL types
        /// </summary>
        public static int DefaultStringLength { get; set; } = 50;

        /// <summary>
        /// Default decimal precision for SQL types
        /// </summary>
        public static string DefaultDecimalPrecision { get; set; } = "18,2";

        /// <summary>
        /// Whether to generate enum properties as string (true) or actual enum type (false)
        /// When true, enum fields will be generated as string properties with separate enum accessor properties
        /// </summary>
        public static bool GenerateEnumAsString { get; set; } = true;
    }

    private static readonly Dictionary<string, string> BaseCSharpTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "string",
        ["text"] = "string",
        ["integer"] = "int",
        ["decimal"] = "decimal",
        ["boolean"] = "bool",
        ["identifier"] = "Guid",
        ["guid"] = "Guid"
    };

    private static readonly Dictionary<string, string> BaseSqlTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["identifier"] = "uniqueidentifier",
        ["string"] = "nvarchar({0})",
        ["text"] = "nvarchar(max)",
        ["integer"] = "int",
        ["decimal"] = "decimal({0})",
        ["boolean"] = "bit",
        ["datetime"] = "datetime2",
        ["timestamp"] = "datetime2",
        ["date"] = "date",
        ["guid"] = "uniqueidentifier",
        ["enum"] = "nvarchar(50)"
    };

    /// <summary>
    /// Gets the C# type for a field in model generation context
    /// </summary>
    public static string GetCSharpType(MDDField field, MDDDocument document, bool useNullableReferenceTypes = false)
    {
        if (field?.BaseField == null)
            return "object";

        // Check for reference fields first
        if (field.BaseField.IsReference)
        {
            return field.BaseField.IsNullable ? "Guid?" : "Guid";
        }

        string fieldType = field.BaseField.Type?.ToLowerInvariant() ?? "string";

        // Check custom mappings first
        if (Config.CustomCSharpTypeMappings.TryGetValue(fieldType, out var customType))
        {
            return ApplyNullability(customType, field, useNullableReferenceTypes);
        }

        // Handle datetime types with configuration
        if (IsDateTimeType(fieldType))
        {
            string dateTimeType = Config.UseDateTimeOffset ? "DateTimeOffset" : "DateTime";
            return field.BaseField.IsNullable ? $"{dateTimeType}?" : dateTimeType;
        }

        // Handle enum types - always return string when GenerateEnumAsString is true
        if (fieldType == "enum" || IsEnumField(field, document))
        {
            if (Config.GenerateEnumAsString)
            {
                // Return string type for the main property, enum accessor will be generated separately
                return ApplyNullability("string", field, useNullableReferenceTypes);
            }
            else
            {
                // Return actual enum type (legacy behavior)
                string enumType = GetEnumTypeName(field, document);
                return field.BaseField.IsNullable ? $"{enumType}?" : enumType;
            }
        }

        // Use base mappings
        if (BaseCSharpTypeMappings.TryGetValue(fieldType, out var baseType))
        {
            return ApplyNullability(baseType, field, useNullableReferenceTypes);
        }

        // Default fallback
        return ApplyNullability("object", field, useNullableReferenceTypes);
    }

    /// <summary>
    /// Gets the SQL type for a field in model generation context
    /// </summary>
    public static string GetSqlType(MDDField field)
    {
        if (field?.BaseField == null)
            return "nvarchar(max)";

        string fieldType = field.BaseField.Type?.ToLowerInvariant() ?? "string";

        // Check custom mappings first
        if (Config.CustomSqlTypeMappings.TryGetValue(fieldType, out var customType))
        {
            return FormatSqlType(customType, field);
        }

        // Use base mappings
        if (BaseSqlTypeMappings.TryGetValue(fieldType, out var baseType))
        {
            return FormatSqlType(baseType, field);
        }

        // Default fallback
        return "nvarchar(max)";
    }

    /// <summary>
    /// Checks if a type is a reference type in C# for model generation
    /// </summary>
    public static bool IsReferenceType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        string lowerType = typeName.ToLowerInvariant();

        // Arrays and collections are reference types
        if (lowerType.EndsWith("[]") ||
            lowerType.StartsWith("list<") ||
            lowerType.StartsWith("icollection<") ||
            lowerType.StartsWith("ienumerable<"))
            return true;

        // Reference types
        var referenceTypes = new[] { "string", "text", "object" };
        return referenceTypes.Contains(lowerType);
    }

    /// <summary>
    /// Checks if a field represents an enum type
    /// </summary>
    public static bool IsEnumField(MDDField field, MDDDocument document)
    {
        if (field?.BaseField == null || document == null)
            return false;

        // Check if field type is enum
        if (!string.IsNullOrEmpty(field.BaseField.Type) &&
            field.BaseField.Type.Equals("enum", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if field references an enum
        if (field.BaseField.IsReference && !string.IsNullOrEmpty(field.BaseField.ReferenceTarget))
        {
            return document.Enums.Any(e => e.BaseEnum.Name == field.BaseField.ReferenceTarget);
        }

        // Check if there's an enum with the same name as the field type
        return !string.IsNullOrEmpty(field.BaseField.Type) &&
               document.Enums.Any(e => e.BaseEnum.Name.Equals(field.BaseField.Type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the enum type name for a field
    /// </summary>
    public static string GetEnumTypeName(MDDField field, MDDDocument document)
    {
        if (field?.BaseField == null || document == null)
            return "string";

        // First check if enum type is specified in extended metadata
        if (field.ExtendedMetadata.TryGetValue("type", out var enumTypeFromMetadata))
        {
            var enumTypeName = enumTypeFromMetadata.ToString();
            if (document.Enums.Any(e => e.BaseEnum.Name.Equals(enumTypeName, StringComparison.OrdinalIgnoreCase)))
            {
                return enumTypeName;
            }
        }

        // If the field has a reference to an enum, use that enum's name
        if (field.BaseField.IsReference && !string.IsNullOrEmpty(field.BaseField.ReferenceTarget))
        {
            var targetEnum = document.Enums.FirstOrDefault(e =>
                e.BaseEnum.Name == field.BaseField.ReferenceTarget);

            if (targetEnum != null)
            {
                return targetEnum.BaseEnum.Name;
            }
        }

        // Check if the field has a nested type definition in base metadata
        if (field.BaseField.Metadata.Any())
        {
            var typeMetadata = field.BaseField.Metadata.FirstOrDefault(kvp =>
                kvp.Key.Equals("type", StringComparison.OrdinalIgnoreCase));

            if (typeMetadata.Key != null)
            {
                string typeName = typeMetadata.Value?.ToString();
                var matchingEnum = document.Enums.FirstOrDefault(e =>
                    e.BaseEnum.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (matchingEnum != null)
                {
                    return matchingEnum.BaseEnum.Name;
                }
            }
        }

        // Check Field Type if it matches any enum
        var enumByType = !string.IsNullOrEmpty(field.BaseField.Type) ? document.Enums.FirstOrDefault(e =>
            e.BaseEnum.Name.Equals(field.BaseField.Type, StringComparison.OrdinalIgnoreCase)) : null;

        if (enumByType != null)
        {
            return enumByType.BaseEnum.Name;
        }

        // Try intelligent field name to enum matching
        return FindEnumByFieldName(field.BaseField.Name, document);
    }

    private static string FindEnumByFieldName(string fieldName, MDDDocument document)
    {
        // Try exact field name match first
        var exactMatch = document.Enums.FirstOrDefault(e =>
            e.BaseEnum.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            return exactMatch.BaseEnum.Name;
        }

        // Try field name with common enum suffixes
        var enumSuffixes = new[] { "s", "Type", "Types", "Status", "Statuses", "Kind", "Kinds", "Roles", "States" };
        foreach (var suffix in enumSuffixes)
        {
            var enumWithSuffix = document.Enums.FirstOrDefault(e =>
                e.BaseEnum.Name.Equals(fieldName + suffix, StringComparison.OrdinalIgnoreCase));
            if (enumWithSuffix != null)
            {
                return enumWithSuffix.BaseEnum.Name;
            }
        }

        // Try removing common suffixes from field name and then adding enum suffixes
        var fieldNameVariations = new[]
        {
            fieldName.Replace("Type", "").Replace("Status", "").Replace("Kind", ""),
            fieldName.TrimEnd('s'),
            fieldName
        };

        foreach (var variation in fieldNameVariations.Where(v => !string.IsNullOrEmpty(v)))
        {
            foreach (var suffix in enumSuffixes)
            {
                var potentialEnumName = variation + suffix;
                var matchingEnum = document.Enums.FirstOrDefault(e =>
                    e.BaseEnum.Name.Equals(potentialEnumName, StringComparison.OrdinalIgnoreCase));
                if (matchingEnum != null)
                {
                    return matchingEnum.BaseEnum.Name;
                }
            }
        }

        // Look for partial matches
        var partialMatch = document.Enums.FirstOrDefault(e =>
            e.BaseEnum.Name.Contains(fieldName, StringComparison.OrdinalIgnoreCase) ||
            fieldName.Contains(e.BaseEnum.Name, StringComparison.OrdinalIgnoreCase));

        if (partialMatch != null)
        {
            return partialMatch.BaseEnum.Name;
        }

        // Return the first enum if any exist, otherwise return a default
        return document.Enums.FirstOrDefault()?.BaseEnum.Name ?? "object";
    }

    /// <summary>
    /// Adds a custom C# type mapping for model projects
    /// </summary>
    public static void AddCustomCSharpTypeMapping(string m3lType, string csharpType)
    {
        if (!string.IsNullOrEmpty(m3lType) && !string.IsNullOrEmpty(csharpType))
        {
            Config.CustomCSharpTypeMappings[m3lType] = csharpType;
        }
    }

    /// <summary>
    /// Adds a custom SQL type mapping for model projects
    /// </summary>
    public static void AddCustomSqlTypeMapping(string m3lType, string sqlType)
    {
        if (!string.IsNullOrEmpty(m3lType) && !string.IsNullOrEmpty(sqlType))
        {
            Config.CustomSqlTypeMappings[m3lType] = sqlType;
        }
    }

    private static string ApplyNullability(string baseType, MDDField field, bool useNullableReferenceTypes)
    {
        if (!field.BaseField.IsNullable)
            return baseType;

        // For reference types with nullable reference types enabled
        if (useNullableReferenceTypes && IsReferenceType(baseType))
        {
            return baseType + "?";
        }

        // For value types, always use nullable syntax
        if (!IsReferenceType(baseType))
        {
            return baseType + "?";
        }

        return baseType;
    }

    private static bool IsDateTimeType(string typeName)
    {
        var dateTimeTypes = new[] { "datetime", "timestamp", "date" };
        return dateTimeTypes.Contains(typeName);
    }

    private static string FormatSqlType(string sqlType, MDDField field)
    {
        // If SQL type contains format placeholders, apply them
        if (sqlType.Contains("{0}"))
        {
            string parameter = GetSqlTypeParameter(field, sqlType);
            return string.Format(sqlType, parameter);
        }

        return sqlType;
    }

    private static string GetSqlTypeParameter(MDDField field, string sqlType)
    {
        // For string types, use length
        if (sqlType.Contains("nvarchar"))
        {
            if (!string.IsNullOrEmpty(field.BaseField.Length) && int.TryParse(field.BaseField.Length, out int length))
            {
                return length.ToString();
            }
            return Config.DefaultStringLength.ToString();
        }

        // For decimal types, use precision
        if (sqlType.Contains("decimal"))
        {
            return !string.IsNullOrEmpty(field.BaseField.Length)
                ? field.BaseField.Length
                : Config.DefaultDecimalPrecision;
        }

        return Config.DefaultStringLength.ToString();
    }
}