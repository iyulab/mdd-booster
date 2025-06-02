using Humanizer;

namespace MDDBooster.Builders.ModelProject.Helpers;

/// <summary>
/// Helper for generating C# attributes specific to model project generation
/// </summary>
public static class AttributeGenerationHelper
{
    /// <summary>
    /// Configuration for attribute generation in model projects
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Custom attribute generators that can be added at runtime
        /// </summary>
        public static Dictionary<string, Func<MDDField, string[]>> CustomAttributeGenerators { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Field name patterns that should trigger specific DataType attributes
        /// </summary>
        public static Dictionary<string, string> DataTypePatterns { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Default string length for MaxLength attributes
        /// </summary>
        public static int DefaultStringLength { get; set; } = 50;

        /// <summary>
        /// Whether to generate JsonIgnore for sensitive fields
        /// </summary>
        public static bool AutoGenerateJsonIgnoreForSensitive { get; set; } = true;
    }

    /// <summary>
    /// Generates all attributes for a field in model project context
    /// </summary>
    public static string GeneratePropertyAnnotations(MDDField field, ModelProjectConfig config = null)
    {
        if (field?.BaseField == null)
            return string.Empty;

        var attributes = new List<string>();
        var addedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Generate core attributes
        GenerateCoreAttributes(field, attributes, addedAttributes);

        // Generate DataType attributes
        GenerateDataTypeAttributes(field, attributes, addedAttributes);

        // Generate validation attributes
        GenerateValidationAttributes(field, attributes, addedAttributes, config);

        // Generate framework-specific attributes
        GenerateFrameworkAttributes(field, attributes, addedAttributes);

        // Generate custom attributes
        GenerateCustomAttributes(field, attributes, addedAttributes);

        if (attributes.Count == 0)
            return string.Empty;

        // Join attributes with newlines and proper indentation, but don't add trailing newline
        return string.Join(Environment.NewLine, attributes.Select(attr => $"\t{attr}"));
    }

    /// <summary>
    /// Adds a custom attribute generator for model projects
    /// </summary>
    public static void AddCustomAttributeGenerator(string attributeName, Func<MDDField, string[]> generator)
    {
        if (!string.IsNullOrEmpty(attributeName) && generator != null)
        {
            Config.CustomAttributeGenerators[attributeName] = generator;
        }
    }

    /// <summary>
    /// Adds a field name pattern for DataType detection in model projects
    /// </summary>
    public static void AddDataTypePattern(string fieldNamePattern, string dataType)
    {
        if (!string.IsNullOrEmpty(fieldNamePattern) && !string.IsNullOrEmpty(dataType))
        {
            Config.DataTypePatterns[fieldNamePattern] = dataType;
        }
    }

    /// <summary>
    /// Initialize with common patterns for model projects
    /// </summary>
    public static void InitializeCommonPatterns()
    {
        // Initialize common DataType patterns for model generation
        var commonDataTypePatterns = new Dictionary<string, string>
        {
            ["email"] = "EmailAddress",
            ["phone"] = "PhoneNumber",
            ["url"] = "Url",
            ["uri"] = "Url",
            ["password"] = "Password",
            ["credit"] = "CreditCard",
            ["postal"] = "PostalCode",
            ["zip"] = "PostalCode"
        };

        foreach (var kvp in commonDataTypePatterns)
        {
            AddDataTypePattern(kvp.Key, kvp.Value);
        }
    }

    private static void GenerateCoreAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes)
    {
        // Required attribute
        if (field.BaseField.IsRequired && !field.BaseField.IsNullable)
        {
            attributes.Add("[Required]");
            addedAttributes.Add("Required");
        }

        // Display attribute with humanized name
        string fieldName = StringHelper.NormalizeName(field.BaseField.Name);
        string displayName = fieldName.Humanize(LetterCasing.Title);
        attributes.Add($"[Display(Name = \"{displayName}\", ShortName = \"{fieldName}\")]");
        addedAttributes.Add("Display");

        // FK attribute for reference fields
        if (field.BaseField.IsReference && !string.IsNullOrEmpty(field.BaseField.ReferenceTarget))
        {
            attributes.Add($"[FK(typeof({field.BaseField.ReferenceTarget}))]");
            addedAttributes.Add("FK");
        }

        // Unique constraint
        if (field.BaseField.IsUnique)
        {
            attributes.Add("[Unique]");
            addedAttributes.Add("Unique");
        }

        // Column attribute
        string sqlType = TypeConversionHelper.GetSqlType(field);
        attributes.Add($"[Column(\"{fieldName}\", TypeName = \"{sqlType}\")]");
        addedAttributes.Add("Column");
    }

    private static void GenerateDataTypeAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes)
    {
        if (addedAttributes.Contains("DataType"))
            return;

        string dataType = DetermineDataType(field);
        if (!string.IsNullOrEmpty(dataType))
        {
            attributes.Add($"[DataType(DataType.{dataType})]");
            addedAttributes.Add("DataType");
        }
    }

    private static void GenerateValidationAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes, ModelProjectConfig config)
    {
        // MaxLength for string fields
        if (IsStringField(field) && !addedAttributes.Contains("MaxLength"))
        {
            int length = GetFieldLength(field, config);
            attributes.Add($"[MaxLength({length})]");
            addedAttributes.Add("MaxLength");
        }

        // Multiline for text fields
        if (field.BaseField.Type.Equals("text", StringComparison.OrdinalIgnoreCase) && !addedAttributes.Contains("Multiline"))
        {
            attributes.Add("[Multiline]");
            addedAttributes.Add("Multiline");
        }

        // JsonIgnore for sensitive fields using the base helper
        if (Config.AutoGenerateJsonIgnoreForSensitive && MDDBooster.Helpers.FieldHelper.IsSensitiveField(field) && !addedAttributes.Contains("JsonIgnore"))
        {
            attributes.Add("[JsonIgnore]");
            addedAttributes.Add("JsonIgnore");
        }
    }

    private static void GenerateFrameworkAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes)
    {
        // Process framework attributes from MDD definition
        foreach (var attr in field.BaseField.FrameworkAttributes)
        {
            string attrName = MDDBooster.Helpers.FieldHelper.ExtractAttributeName(attr);
            if (string.IsNullOrEmpty(attrName) || addedAttributes.Contains(attrName))
                continue;

            string processedAttr = ProcessFrameworkAttribute(attr, attrName);
            if (!string.IsNullOrEmpty(processedAttr))
            {
                attributes.Add($"[{processedAttr}]");
                addedAttributes.Add(attrName);
            }
        }

        // Process attributes from extended metadata
        ProcessExtendedMetadataAttributes(field, attributes, addedAttributes);
    }

    private static void GenerateCustomAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes)
    {
        foreach (var kvp in Config.CustomAttributeGenerators)
        {
            if (addedAttributes.Contains(kvp.Key))
                continue;

            try
            {
                var customAttributes = kvp.Value(field);
                if (customAttributes?.Any() == true)
                {
                    foreach (var customAttr in customAttributes)
                    {
                        if (!string.IsNullOrEmpty(customAttr))
                        {
                            attributes.Add($"[{customAttr}]");
                        }
                    }
                    addedAttributes.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(ex, "Failed to generate custom attribute {AttributeName} for field {FieldName}",
                    kvp.Key, field.BaseField.Name);
            }
        }
    }

    private static string DetermineDataType(MDDField field)
    {
        // Use the base helper first
        string baseDataType = MDDBooster.Helpers.FieldHelper.GetDataType(field);
        if (!string.IsNullOrEmpty(baseDataType))
            return baseDataType;

        // Check custom patterns specific to model projects
        foreach (var kvp in Config.DataTypePatterns)
        {
            if (field.BaseField.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static bool IsStringField(MDDField field)
    {
        var stringTypes = new[] { "string", "text" };
        return stringTypes.Contains(field.BaseField.Type, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetFieldLength(MDDField field, ModelProjectConfig config)
    {
        if (!string.IsNullOrEmpty(field.BaseField.Length) && int.TryParse(field.BaseField.Length, out int length))
        {
            return length;
        }

        return config?.DefaultStringLength ?? Config.DefaultStringLength;
    }

    private static string ProcessFrameworkAttribute(string attr, string attrName)
    {
        // Handle specific framework attributes with parameter processing
        return attrName.ToLowerInvariant() switch
        {
            "insert" or "update" => ProcessValueAttribute(attr),
            "datatype" => ProcessDataTypeAttribute(attr),
            _ => attr
        };
    }

    private static string ProcessValueAttribute(string attr)
    {
        var match = System.Text.RegularExpressions.Regex.Match(attr, @"\(""([^""]+)""\)");
        if (match.Success)
        {
            return attr; // Already properly formatted
        }

        // Try to fix malformed attribute using base helper
        string value = MDDBooster.Helpers.FieldHelper.ExtractAttributeValue(attr);
        if (!string.IsNullOrEmpty(value))
        {
            string attrName = MDDBooster.Helpers.FieldHelper.ExtractAttributeName(attr);
            return $"{attrName}(\"{value}\")";
        }

        return attr;
    }

    private static string ProcessDataTypeAttribute(string attr)
    {
        var match = System.Text.RegularExpressions.Regex.Match(attr, @"DataType\(DataType\.([^)]+)\)");
        if (match.Success)
        {
            return attr; // Already properly formatted
        }

        return attr;
    }

    private static void ProcessExtendedMetadataAttributes(MDDField field, List<string> attributes, HashSet<string> addedAttributes)
    {
        // Process Update value
        if (field.ExtendedMetadata.TryGetValue("UpdateValue", out var updateValue) && !addedAttributes.Contains("Update"))
        {
            string cleanValue = CleanAttributeValue(updateValue.ToString());
            attributes.Add($"[Update(\"{cleanValue}\")]");
            addedAttributes.Add("Update");
        }

        // Process Insert value
        if (field.ExtendedMetadata.TryGetValue("InsertValue", out var insertValue) && !addedAttributes.Contains("Insert"))
        {
            string cleanValue = CleanAttributeValue(insertValue.ToString());
            attributes.Add($"[Insert(\"{cleanValue}\")]");
            addedAttributes.Add("Insert");
        }
    }

    private static string CleanAttributeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Remove surrounding quotes if present
        return value.Trim().Trim('"');
    }
}