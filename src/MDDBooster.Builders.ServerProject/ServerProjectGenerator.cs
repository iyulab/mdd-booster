namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// Generator for GraphQL server classes and OData services - Base class
/// </summary>
public partial class ServerProjectGenerator
{
    public MDDDocument Document { get; }
    private readonly ServerProjectConfig _config;

    public ServerProjectGenerator(MDDDocument document, ServerProjectConfig config)
    {
        Document = document;
        _config = config;
    }

    /// <summary>
    /// Get GraphQL type for a field
    /// </summary>
    private string GetGraphQLType(MDDField field)
    {
        // Null safety check for field type
        if (string.IsNullOrEmpty(field?.BaseField?.Type))
        {
            AppLog.Warning("Field type is null or empty for field: {FieldName}", field?.BaseField?.Name ?? "Unknown");
            return "StringGraphType"; // Default fallback
        }

        return field.BaseField.Type.ToLowerInvariant() switch
        {
            "string" => "StringGraphType",
            "text" => "StringGraphType",
            "integer" => "IntGraphType",
            "decimal" => "DecimalGraphType",
            "boolean" => "BooleanGraphType",
            "datetime" => "DateTimeGraphType",
            "timestamp" => "DateTimeGraphType",
            "date" => "DateTimeGraphType",
            "identifier" => "IdGraphType",
            "guid" => "GuidGraphType",
            "enum" => "StringGraphType", // Enums as strings in GraphQL
            _ => "StringGraphType" // Default fallback
        };
    }

    /// <summary>
    /// Get relationship fields for a model
    /// </summary>
    private List<RelationshipInfo> GetRelationshipFields(MDDModel model)
    {
        var relationships = new List<RelationshipInfo>();

        // Find models that reference this model
        foreach (var otherModel in Document.Models.Where(m => !m.BaseModel.IsAbstract && m.BaseModel.Name != model.BaseModel.Name))
        {
            var allFields = ModelUtilities.GetAllFields(Document, otherModel);
            var referenceFields = allFields.Where(f =>
                f.BaseField.IsReference &&
                f.BaseField.ReferenceTarget == model.BaseModel.Name).ToList();

            foreach (var refField in referenceFields)
            {
                // Create unique relationship info
                var relationshipInfo = new RelationshipInfo
                {
                    TargetModel = otherModel.BaseModel.Name,
                    ForeignKeyField = $"{otherModel.BaseModel.Name}_ids"
                };

                // Check if this relationship already exists to avoid duplicates
                if (!relationships.Any(r => r.TargetModel == relationshipInfo.TargetModel))
                {
                    relationships.Add(relationshipInfo);
                }
            }
        }

        return relationships;
    }

    /// <summary>
    /// Get the primary key field name for a model
    /// </summary>
    private string GetPrimaryKeyFieldName(MDDModel model)
    {
        // Get all fields for the model
        var allFields = ModelUtilities.GetAllFields(Document, model);

        // Look for field with Key attribute or Id property
        var keyField = allFields.FirstOrDefault(f =>
            f.BaseField.IsPrimaryKey ||
            string.Equals(f.BaseField.Name, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.BaseField.Name, "_id", StringComparison.OrdinalIgnoreCase));

        if (keyField != null)
        {
            return keyField.BaseField.Name;
        }

        // Fallback: look for any field ending with "Id"
        var idField = allFields.FirstOrDefault(f =>
            f.BaseField.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase));

        if (idField != null)
        {
            return idField.BaseField.Name;
        }

        // Ultimate fallback
        AppLog.Warning("Could not find primary key field for model: {ModelName}, using 'Id'", model.BaseModel.Name);
        return "Id";
    }

    /// <summary>
    /// Represents a relationship between models
    /// </summary>
    private class RelationshipInfo
    {
        public string TargetModel { get; set; } = string.Empty;
        public string ForeignKeyField { get; set; } = string.Empty;
    }
}