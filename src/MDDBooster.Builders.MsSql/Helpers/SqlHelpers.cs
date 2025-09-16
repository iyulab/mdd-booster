using M3LParser.Helpers;

namespace MDDBooster.Builders.MsSql.Helpers;

/// <summary>
/// Provides common helper methods for SQL generation to avoid code duplication
/// </summary>
public static class SqlHelpers
{
    /// <summary>
    /// Transform a default value for SQL Server compatibility
    /// </summary>
    public static string TransformDefaultValue(string defaultValue, string type)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return string.Empty;

        if (defaultValue == "@now")
            return "GETDATE()";

        if (defaultValue == "@by")
            return "N'@system'";

        return StringHelper.TransformDefaultValue(defaultValue, type, "sql");
    }

    /// <summary>
    /// Gets the SQL type for a field
    /// </summary>
    public static string GetSqlType(MDDField field)
    {
        if (field == null)
        {
            throw new ArgumentNullException(nameof(field), "Field cannot be null");
        }

        if (field.BaseField == null)
        {
            throw new InvalidOperationException("Field.BaseField cannot be null");
        }

        if (string.IsNullOrEmpty(field.BaseField.Name))
        {
            throw new InvalidOperationException("Field.BaseField.Name cannot be null or empty");
        }

        if (string.IsNullOrEmpty(field.BaseField.Type))
        {
            throw new InvalidOperationException($"Field '{field.BaseField.Name}' has null or empty Type");
        }

        string sqlType;

        switch (field.BaseField.Type.ToLowerInvariant())
        {
            case "identifier":
                sqlType = "UNIQUEIDENTIFIER";
                break;
            case "string":
                var length = string.IsNullOrEmpty(field.BaseField.Length) ? "50" : field.BaseField.Length;
                sqlType = $"NVARCHAR({length})";
                break;
            case "text":
                sqlType = "NVARCHAR(MAX)";
                break;
            case "integer":
                sqlType = "INT";
                break;
            case "decimal":
                var precision = "18,2"; // default
                if (!string.IsNullOrEmpty(field.BaseField.Length))
                {
                    precision = field.BaseField.Length;
                }
                sqlType = $"DECIMAL({precision})";
                break;
            case "boolean":
                sqlType = "BIT";
                break;
            case "timestamp":
            case "datetime":
                sqlType = "DATETIME2";
                break;
            case "date":
                sqlType = "DATE";
                break;
            case "enum":
                // For enums, use NVARCHAR(50) instead of INT
                sqlType = "NVARCHAR(50)";
                break;
            default:
                // Handle unknown types as NVARCHAR(50)
                AppLog.Warning("Unknown field type: {FieldType} for field: {FieldName}",
                    field.BaseField.Type, field.BaseField.Name);
                sqlType = "NVARCHAR(50)";
                break;
        }

        return sqlType;
    }

    /// <summary>
    /// Determines if a field is an index definition
    /// </summary>
    public static bool IsIndexDefinition(MDDField field)
    {
        if (field == null || field.BaseField == null || field.BaseField.Name == null)
        {
            return false;
        }

        // Check if the field name starts with '@index' or '@unique'
        return field.BaseField.Name.StartsWith("@index") ||
               field.BaseField.Name.StartsWith("@unique");
    }

    /// <summary>
    /// Extension method to check if a field should be excluded from SQL generation
    /// </summary>
    public static bool ShouldExcludeFromSql(this MDDField field)
    {
        // Primary key fields should NEVER be excluded, regardless of attributes
        if (field.BaseField.IsPrimaryKey)
        {
            AppLog.Debug("Field {FieldName} is a primary key - will ALWAYS be included in SQL despite any Without attributes",
                field.BaseField.Name);
            return false;
        }

        // Check for the Without framework attribute by name
        if (field.FrameworkAttributes != null)
        {
            foreach (var attr in field.FrameworkAttributes)
            {
                if (attr.Name.Equals("Without", StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Debug("Field {FieldName} has Without attribute - will be excluded from SQL", field.BaseField.Name);
                    return true;
                }
            }
        }

        // Also check in raw framework attributes from the base field
        if (field.BaseField.FrameworkAttributes != null)
        {
            foreach (var attr in field.BaseField.FrameworkAttributes)
            {
                if (attr.Contains("Without"))
                {
                    AppLog.Debug("Field {FieldName} has Without attribute in raw framework attributes - will be excluded from SQL", field.BaseField.Name);
                    return true;
                }
            }
        }

        return field.ExtendedMetadata != null &&
               field.ExtendedMetadata.ContainsKey("ExcludeFromGeneration") &&
               (bool)field.ExtendedMetadata["ExcludeFromGeneration"];
    }

    /// <summary>
    /// Gets the appropriate database value for a special command like @now or @by
    /// </summary>
    public static string GetSpecialCommandSqlValue(string command)
    {
        return command switch
        {
            "@now" => "GETDATE()",
            "@by" => "N'@system'",
            _ => command
        };
    }

    /// <summary>
    /// Create a safe SQL identifier from a name
    /// </summary>
    public static string CreateSafeSqlIdentifier(string name)
    {
        // First convert to PascalCase
        var identifier = StringHelper.NormalizeName(name);

        // Replace any remaining invalid characters with underscores
        identifier = Regex.Replace(identifier, @"[^\w]", "_");

        // Ensure it doesn't start with a number
        if (identifier.Length > 0 && char.IsDigit(identifier[0]))
        {
            identifier = "_" + identifier;
        }

        return identifier;
    }

    /// <summary>
    /// Creates a fully qualified SQL name with schema
    /// </summary>
    public static string GetFullySqlName(string schemaName, string objectName)
    {
        return $"[{schemaName}].[{objectName}]";
    }

    /// <summary>
    /// Generates a SQL comment block
    /// </summary>
    public static string GenerateSqlComment(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return string.Empty;

        var commentLines = comment.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (commentLines.Length == 1)
        {
            return $"-- {comment}";
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("/*");
            foreach (var line in commentLines)
            {
                sb.AppendLine($" * {line}");
            }
            sb.AppendLine(" */");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Formats a field value for SQL based on its data type
    /// </summary>
    public static string FormatSqlValue(string value, string fieldType)
    {
        if (string.IsNullOrEmpty(value))
            return "NULL";

        switch (fieldType.ToLowerInvariant())
        {
            case "string":
            case "text":
                return $"N'{value.Replace("'", "''")}'";
            case "identifier":
                // Handle GUIDs
                if (Guid.TryParse(value, out _))
                    return $"'{value}'";
                return value;
            case "boolean":
                // Convert boolean values
                return value.ToLowerInvariant() == "true" ? "1" : "0";
            case "datetime":
            case "timestamp":
            case "date":
                // Format date/time values
                if (DateTime.TryParse(value, out var dateTime))
                    return $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'";
                return value;
            default:
                // For numeric types, return as is
                return value;
        }
    }
}