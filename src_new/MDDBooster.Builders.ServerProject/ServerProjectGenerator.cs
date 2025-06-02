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
                relationships.Add(new RelationshipInfo
                {
                    TargetModel = otherModel.BaseModel.Name,
                    ForeignKeyField = $"{otherModel.BaseModel.Name}_ids"
                });
            }
        }

        return relationships;
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