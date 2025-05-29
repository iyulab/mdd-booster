namespace MDDBooster.Builders.ModelProject.Processors;

/// <summary>
/// Processor for model-specific attributes
/// </summary>
public class ModelAttributeProcessor : IModelProcessor
{
    public void Process(MDDDocument document)
    {
        AppLog.Information("Processing model attributes");

        // Process field-level attributes
        foreach (var model in document.Models)
        {
            foreach (var field in model.Fields)
            {
                ProcessFieldAttributes(field);
            }
        }

        // Process interface field attributes
        foreach (var interface_ in document.Interfaces)
        {
            foreach (var field in interface_.Fields)
            {
                ProcessFieldAttributes(field);
            }
        }

        AppLog.Information("Completed processing model attributes");
    }

    /// <summary>
    /// Process attributes on a field
    /// </summary>
    private void ProcessFieldAttributes(MDDField field)
    {
        // Process framework attributes
        foreach (var attr in field.FrameworkAttributes)
        {
            // Handle DataType attribute
            if (attr.Name.Equals("DataType", StringComparison.OrdinalIgnoreCase) && attr.Parameters.Count > 0)
            {
                field.ExtendedMetadata["DataType"] = attr.Parameters[0];
            }
            // Handle JsonIgnore attribute
            else if (attr.Name.Equals("JsonIgnore", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["JsonIgnore"] = true;
            }
            // Handle Computed attribute (fields that are computed in the database)
            else if (attr.Name.Equals("Computed", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["Computed"] = true;
            }
            // Handle Sensitive attribute (fields that contain sensitive data)
            else if (attr.Name.Equals("Sensitive", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["Sensitive"] = true;
            }
            // Handle ExcludeFromDto attribute (fields that should be excluded from DTOs)
            else if (attr.Name.Equals("ExcludeFromDto", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["ExcludeFromDto"] = true;
            }
            // Handle ReadOnly attribute
            else if (attr.Name.Equals("ReadOnly", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["ReadOnly"] = true;
            }
        }

        // Process field name patterns - with improved type checking
        string fieldName = field.BaseField.Name.ToLowerInvariant();
        string fieldType = field.BaseField.Type.ToLowerInvariant();

        // Password fields should be marked as sensitive, but only if they're strings
        if (fieldName.Contains("password") && !field.ExtendedMetadata.ContainsKey("DataType")
            && (fieldType == "string" || fieldType == "text"))
        {
            field.ExtendedMetadata["DataType"] = "Password";

            // Also mark as sensitive if not already marked
            if (!field.ExtendedMetadata.ContainsKey("Sensitive"))
            {
                field.ExtendedMetadata["Sensitive"] = true;
            }
        }

        // Email fields
        if (fieldName.Contains("email") && !field.ExtendedMetadata.ContainsKey("DataType"))
        {
            field.ExtendedMetadata["DataType"] = "EmailAddress";
        }

        // Phone fields
        if (fieldName.Contains("phone") && !field.ExtendedMetadata.ContainsKey("DataType"))
        {
            field.ExtendedMetadata["DataType"] = "PhoneNumber";
        }

        // URL fields
        if ((fieldName.Contains("url") || fieldName.Contains("uri")) &&
            !field.ExtendedMetadata.ContainsKey("DataType"))
        {
            field.ExtendedMetadata["DataType"] = "Url";
        }

        // DateTime fields
        if ((fieldType == "datetime" || fieldType == "timestamp" || fieldType == "date") &&
            !field.ExtendedMetadata.ContainsKey("DataType"))
        {
            // If field contains "date" but not "time", use Date, otherwise use DateTime
            if (fieldName.Contains("date") && !fieldName.Contains("time"))
            {
                field.ExtendedMetadata["DataType"] = "Date";
            }
            else
            {
                field.ExtendedMetadata["DataType"] = "DateTime";
            }
        }
    }
}