namespace MDDBooster.Builders.ModelProject.Utilities;

/// <summary>
/// Handles type conversions between M3L and C# types
/// </summary>
public class ModelTypeConverter
{
    private readonly ModelProjectConfig _config;
    private readonly MDDDocument _document;
    private const int DEFAULT_STRING_LENGTH = 50;

    public ModelTypeConverter(MDDDocument document, ModelProjectConfig config)
    {
        _document = document;
        _config = config;
    }

    /// <summary>
    /// Get the C# type for a field
    /// </summary>
    public string GetCSharpType(MDDField field)
    {
        // Check if it's a reference to another entity
        if (field.BaseField.IsReference)
        {
            // If it's a reference, it should be a Guid or the appropriate type
            return field.BaseField.IsNullable ? "Guid?" : "Guid";
        }

        // Map M3L types to C# types
        string baseType = field.BaseField.Type.ToLowerInvariant() switch
        {
            "string" => "string",
            "text" => "string",
            "integer" => "int",
            "decimal" => "decimal",
            "boolean" => "bool",
            "datetime" => _config.UseDateTimeOffset ? "DateTimeOffset" : "DateTime",
            "timestamp" => _config.UseDateTimeOffset ? "DateTimeOffset" : "DateTime",
            "date" => _config.UseDateTimeOffset ? "DateTimeOffset" : "DateTime",
            "identifier" => "Guid",
            "guid" => "Guid",
            "enum" => GetEnumType(field),
            _ => "object" // Default fallback type
        };

        // Add nullable modifier if needed
        if (field.BaseField.IsNullable)
        {
            // For reference types in C# 8.0+, use ? suffix if using nullable reference types
            if (_config.UseNullableReferenceTypes && IsReferenceType(field.BaseField.Type))
            {
                return baseType + "?";
            }
            // For value types, use Nullable<T> syntax or ? shorthand
            else if (!IsReferenceType(field.BaseField.Type))
            {
                return baseType + "?";
            }
        }

        return baseType;
    }

    /// <summary>
    /// Get the appropriate SQL type for a field (for Column TypeName attribute)
    /// </summary>
    public string GetSqlType(MDDField field)
    {
        // Use specified length or default length for strings
        string length = !string.IsNullOrEmpty(field.BaseField.Length)
            ? field.BaseField.Length
            : DEFAULT_STRING_LENGTH.ToString();

        return field.BaseField.Type.ToLowerInvariant() switch
        {
            "identifier" => "uniqueidentifier",
            "string" => $"nvarchar({length})",
            "text" => "nvarchar(max)",
            "integer" => "int",
            "decimal" => string.IsNullOrEmpty(field.BaseField.Length) ? "decimal(18,2)" : $"decimal({field.BaseField.Length})",
            "boolean" => "bit",
            "datetime" => "datetime2",
            "timestamp" => "datetime2",
            "date" => "date",
            "enum" => "nvarchar(50)",
            "guid" => "uniqueidentifier",
            _ => "nvarchar(max)" // Default fallback type
        };
    }

    /// <summary>
    /// Get the enum type for a field
    /// </summary>
    public string GetEnumType(MDDField field)
    {
        // If the field has a reference to an enum, use that enum's name
        if (field.BaseField.IsReference)
        {
            var targetEnum = _document.Enums.FirstOrDefault(e =>
                e.BaseEnum.Name == field.BaseField.ReferenceTarget);

            if (targetEnum != null)
            {
                return targetEnum.BaseEnum.Name;
            }
        }

        // Check if the field has a nested type definition
        if (field.BaseField.Metadata.ContainsKey("type"))
        {
            string typeName = field.BaseField.Metadata["type"].ToString();
            var matchingEnum = _document.Enums.FirstOrDefault(e =>
                e.BaseEnum.Name == typeName);

            if (matchingEnum != null)
            {
                return matchingEnum.BaseEnum.Name;
            }
        }

        // Check Field Type if it matches any enum
        var enumByName = _document.Enums.FirstOrDefault(e =>
            e.BaseEnum.Name == field.BaseField.Type);

        if (enumByName != null)
        {
            return enumByName.BaseEnum.Name;
        }

        // Default fallback to string
        return "string";
    }

    /// <summary>
    /// Check if a field is a reference type in C#
    /// </summary>
    public bool IsReferenceType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "string" => true,
            "text" => true,
            "enum" => false, // Enums are value types
            _ => false // Most other types are value types
        };
    }
}