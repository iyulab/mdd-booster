namespace MDDBooster.Builders.ModelProject.Utilities;

/// <summary>
/// Handles generation of C# attributes for model properties
/// </summary>
public class ModelAttributeGenerator
{
    private readonly ModelTypeConverter _typeConverter;
    private readonly ModelProjectConfig _config;
    private const int DEFAULT_STRING_LENGTH = 50;

    public ModelAttributeGenerator(ModelTypeConverter typeConverter, ModelProjectConfig config)
    {
        _typeConverter = typeConverter;
        _config = config;
    }

    /// <summary>
    /// Generate property annotations for a field
    /// </summary>
    public string GeneratePropertyAnnotations(MDDField field)
    {
        var sb = new StringBuilder();
        var addedAttributes = new HashSet<string>(); // Track added attributes to prevent duplication

        // Required attribute
        if (field.BaseField.IsRequired && !field.BaseField.IsNullable)
        {
            sb.AppendLine("\t[Required]");
            addedAttributes.Add("Required");
        }

        // Display attribute with name
        string fieldName = M3LParser.Helpers.StringHelper.NormalizeName(field.BaseField.Name);
        sb.AppendLine($"\t[Display(Name = \"{fieldName}\", ShortName = \"{fieldName}\")]");
        addedAttributes.Add("Display");

        // Add FK attribute for reference fields
        if (field.BaseField.IsReference && !string.IsNullOrEmpty(field.BaseField.ReferenceTarget))
        {
            sb.AppendLine($"\t[FK(typeof({field.BaseField.ReferenceTarget}))]");
            addedAttributes.Add("FK");
        }

        // MaxLength for string fields
        if (field.BaseField.Type.ToLowerInvariant() == "string" || field.BaseField.Type.ToLowerInvariant() == "text")
        {
            string length = !string.IsNullOrEmpty(field.BaseField.Length)
                ? field.BaseField.Length
                : DEFAULT_STRING_LENGTH.ToString();

            sb.AppendLine($"\t[MaxLength({length})]");
            addedAttributes.Add("MaxLength");
        }

        // Unique constraint
        if (field.BaseField.IsUnique)
        {
            sb.AppendLine("\t[Unique]");
            addedAttributes.Add("Unique");
        }

        // For multiline text (option to display as multiline in UI)
        if (field.BaseField.Type.ToLowerInvariant() == "text")
        {
            sb.AppendLine("\t[Multiline]");
            addedAttributes.Add("Multiline");
        }

        // Column definition for EF
        string sqlType = _typeConverter.GetSqlType(field);
        sb.AppendLine($"\t[Column(\"{fieldName}\", TypeName = \"{sqlType}\")]");
        addedAttributes.Add("Column");

        // Add DataType attribute based on field type and name
        // Only add if not already added
        if (!addedAttributes.Contains("DataType"))
        {
            string fieldType = field.BaseField.Type.ToLowerInvariant();
            bool dataTypeAdded = false;

            // DataType should be based on field type first, then name patterns if type is ambiguous
            if (fieldType == "datetime" || fieldType == "timestamp" || fieldType == "date")
            {
                // For date/time fields, use Date or DateTime based on field name
                if (field.BaseField.Name.Contains("Date", StringComparison.OrdinalIgnoreCase) &&
                    !field.BaseField.Name.Contains("Time", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("\t[DataType(DataType.Date)]");
                    dataTypeAdded = true;
                }
                else
                {
                    sb.AppendLine("\t[DataType(DataType.DateTime)]");
                    dataTypeAdded = true;
                }
            }
            else if (field.ExtendedMetadata.TryGetValue("DataType", out var dataType))
            {
                // Use DataType from metadata if available
                sb.AppendLine($"\t[DataType(DataType.{dataType})]");
                dataTypeAdded = true;
            }
            else if (IsPasswordField(field) && (fieldType == "string" || fieldType == "text"))
            {
                // Only add Password DataType to string fields actually containing passwords
                sb.AppendLine("\t[DataType(DataType.Password)]");
                dataTypeAdded = true;
            }
            else if (IsEmailField(field))
            {
                sb.AppendLine("\t[DataType(DataType.EmailAddress)]");
                dataTypeAdded = true;
            }
            else if (IsPhoneField(field))
            {
                sb.AppendLine("\t[DataType(DataType.PhoneNumber)]");
                dataTypeAdded = true;
            }
            else if (IsUrlField(field))
            {
                sb.AppendLine("\t[DataType(DataType.Url)]");
                dataTypeAdded = true;
            }

            if (dataTypeAdded)
            {
                addedAttributes.Add("DataType");
            }
        }

        // Add JsonIgnore for sensitive fields
        if (IsSensitiveField(field) && !addedAttributes.Contains("JsonIgnore"))
        {
            sb.AppendLine("\t[JsonIgnore]");
            addedAttributes.Add("JsonIgnore");
        }

        // Process framework attributes from MDD definition
        foreach (var attr in field.BaseField.FrameworkAttributes)
        {
            // Extract attribute name
            string attrName = ExtractAttributeName(attr);
            if (string.IsNullOrEmpty(attrName) || addedAttributes.Contains(attrName))
                continue;

            // Parse attribute with parameters
            if (attrName.Equals("Insert", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractAttributeValue(attr);
                if (!string.IsNullOrEmpty(value))
                {
                    sb.AppendLine($"\t[Insert(\"{value}\")]");
                    addedAttributes.Add("Insert");
                }
            }
            else if (attrName.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractAttributeValue(attr);
                if (!string.IsNullOrEmpty(value))
                {
                    sb.AppendLine($"\t[Update(\"{value}\")]");
                    addedAttributes.Add("Update");
                }
            }
            else if (attrName.Equals("DataType", StringComparison.OrdinalIgnoreCase) && !addedAttributes.Contains("DataType"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(attr, @"DataType\(DataType\.([^)]+)\)");
                if (match.Success)
                {
                    sb.AppendLine($"\t[DataType(DataType.{match.Groups[1].Value})]");
                    addedAttributes.Add("DataType");
                }
            }
            else if (attrName.Equals("JsonIgnore", StringComparison.OrdinalIgnoreCase) && !addedAttributes.Contains("JsonIgnore"))
            {
                sb.AppendLine("\t[JsonIgnore]");
                addedAttributes.Add("JsonIgnore");
            }
            else if (!addedAttributes.Contains(attrName))
            {
                // For other attributes, just add them directly
                sb.AppendLine($"\t[{attr}]");
                addedAttributes.Add(attrName);
            }
        }

        // Add attributes from extended metadata if not already added
        if (field.ExtendedMetadata.ContainsKey("UpdateValue") && !addedAttributes.Contains("Update"))
        {
            var updateValue = field.ExtendedMetadata["UpdateValue"].ToString();
            updateValue = FixAttributeQuotes(updateValue);
            sb.AppendLine($"\t[Update(\"{updateValue}\")]");
            addedAttributes.Add("Update");
        }

        if (field.ExtendedMetadata.ContainsKey("InsertValue") && !addedAttributes.Contains("Insert"))
        {
            var insertValue = field.ExtendedMetadata["InsertValue"].ToString();
            insertValue = FixAttributeQuotes(insertValue);
            sb.AppendLine($"\t[Insert(\"{insertValue}\")]");
            addedAttributes.Add("Insert");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts attribute name from attribute string
    /// </summary>
    private string ExtractAttributeName(string attr)
    {
        if (string.IsNullOrEmpty(attr))
            return string.Empty;

        // Extract the attribute name (before any parameters)
        var match = System.Text.RegularExpressions.Regex.Match(attr, @"^([^\(]+)");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return attr;
    }

    /// <summary>
    /// Extracts attribute value from attribute string
    /// </summary>
    private string ExtractAttributeValue(string attr)
    {
        if (string.IsNullOrEmpty(attr))
            return string.Empty;

        // Match content inside parentheses
        var match = System.Text.RegularExpressions.Regex.Match(attr, @"\(""([^""]+)""\)");
        if (match.Success)
            return match.Groups[1].Value;

        match = System.Text.RegularExpressions.Regex.Match(attr, @"\(([^\)]+)\)");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return string.Empty;
    }

    /// <summary>
    /// Fixes improper quotation in attribute values
    /// </summary>
    private string FixAttributeQuotes(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Remove double quotes if value is already quoted
        if (value.StartsWith("\"") && value.EndsWith("\""))
            return value.Substring(1, value.Length - 2);

        return value;
    }

    /// <summary>
    /// Check if a field is a password field
    /// </summary>
    private bool IsPasswordField(MDDField field)
    {
        // Check both field name and type - it should be a string field containing "Password"
        return (field.BaseField.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
               (field.BaseField.Type.ToLowerInvariant() == "string" ||
                field.BaseField.Type.ToLowerInvariant() == "text")) ||
               (field.ExtendedMetadata.ContainsKey("DataType") &&
                field.ExtendedMetadata["DataType"].ToString() == "Password");
    }

    /// <summary>
    /// Check if a field is an email field
    /// </summary>
    private bool IsEmailField(MDDField field)
    {
        return field.BaseField.Name.Contains("Email", StringComparison.OrdinalIgnoreCase) ||
               field.ExtendedMetadata.ContainsKey("DataType") &&
               field.ExtendedMetadata["DataType"].ToString() == "EmailAddress";
    }

    /// <summary>
    /// Check if a field is a phone field
    /// </summary>
    private bool IsPhoneField(MDDField field)
    {
        return field.BaseField.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
               field.ExtendedMetadata.ContainsKey("DataType") &&
               field.ExtendedMetadata["DataType"].ToString() == "PhoneNumber";
    }

    /// <summary>
    /// Check if a field is a URL field
    /// </summary>
    private bool IsUrlField(MDDField field)
    {
        return field.BaseField.Name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
               field.BaseField.Name.Contains("Uri", StringComparison.OrdinalIgnoreCase) ||
               field.ExtendedMetadata.ContainsKey("DataType") &&
               field.ExtendedMetadata["DataType"].ToString() == "Url";
    }

    /// <summary>
    /// Check if a field contains sensitive data that should be excluded from serialization
    /// </summary>
    private bool IsSensitiveField(MDDField field)
    {
        return field.BaseField.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
               field.BaseField.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
               field.BaseField.Name.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
               field.ExtendedMetadata.ContainsKey("JsonIgnore") ||
               field.ExtendedMetadata.ContainsKey("Sensitive");
    }
}