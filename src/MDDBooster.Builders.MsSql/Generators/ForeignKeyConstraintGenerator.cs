using M3LParser.Helpers;
using MDDBooster.Builders.MsSql.Helpers;
using MDDBooster.Models;
using MDDBooster.Utilities;

namespace MDDBooster.Builders.MsSql.Generators;

/// <summary>
/// Generator for SQL foreign key constraints
/// </summary>
public class ForeignKeyConstraintGenerator
{
    private readonly MDDDocument _document;
    private readonly string _schemaName;
    private readonly bool _cascadeDelete;

    public ForeignKeyConstraintGenerator(MDDDocument document, string schemaName, bool cascadeDelete = true)
    {
        _document = document;
        _schemaName = schemaName;
        _cascadeDelete = cascadeDelete;
    }

    /// <summary>
    /// Generate foreign key constraints for a model
    /// </summary>
    public string GenerateForeignKeyConstraints(MDDModel model)
    {
        var sb = new StringBuilder();
        var tableName = StringHelper.NormalizeName(model.BaseModel.Name);

        // Get all fields including inherited ones
        var allFields = ModelUtilities.GetAllFields(_document, model);

        // Find fields with references
        var referenceFields = allFields.Where(f =>
            !f.ShouldExcludeFromSql() &&
            f.BaseField.IsReference).ToList();

        if (!referenceFields.Any())
        {
            return string.Empty;
        }

        AppLog.Debug("Found {Count} reference fields in model {ModelName}",
            referenceFields.Count, model.BaseModel.Name);

        foreach (var field in referenceFields)
        {
            var fieldName = StringHelper.NormalizeName(field.BaseField.Name);
            var targetModel = field.BaseField.ReferenceTarget;

            if (string.IsNullOrEmpty(targetModel))
            {
                AppLog.Warning("Reference field {FieldName} in {ModelName} has no target model",
                    field.BaseField.Name, model.BaseModel.Name);
                continue;
            }

            // Get target model's primary key
            // Default is "_id" but can be overridden if needed
            string targetPrimaryKey = "_id";
            var targetModelObj = _document.Models.FirstOrDefault(m => m.BaseModel.Name == targetModel);
            if (targetModelObj != null)
            {
                var primaryKeyField = ModelUtilities.GetAllFields(_document, targetModelObj)
                    .FirstOrDefault(f => f.BaseField.IsPrimaryKey);

                if (primaryKeyField != null)
                {
                    targetPrimaryKey = StringHelper.NormalizeName(primaryKeyField.BaseField.Name);
                }
            }

            // Generate constraint name
            var constraintName = $"FK_{tableName}_{fieldName}";

            // Determine ON DELETE behavior
            var onDeleteAction = _cascadeDelete ? "CASCADE" : "NO ACTION";

            // Check for custom deletion behavior in field metadata
            if (field.ExtendedMetadata.ContainsKey("OnDelete"))
            {
                var onDelete = field.ExtendedMetadata["OnDelete"].ToString();
                onDeleteAction = onDelete.ToUpperInvariant();
            }
            // Check for M3L cascade behavior (new syntax)
            else if (!string.IsNullOrEmpty(field.BaseField.CascadeBehavior))
            {
                onDeleteAction = field.BaseField.CascadeBehavior;
                AppLog.Debug("Using M3L cascade behavior for field {FieldName}: {CascadeBehavior}",
                    field.BaseField.Name, field.BaseField.CascadeBehavior);
            }
            else
            {
                AppLog.Debug("No cascade behavior specified for field {FieldName}, using default: {DefaultAction}",
                    field.BaseField.Name, onDeleteAction);
            }

            // Generate constraint SQL
            sb.AppendLine($"ALTER TABLE [{_schemaName}].[{tableName}] ADD CONSTRAINT [{constraintName}] FOREIGN KEY ([{fieldName}])");
            sb.AppendLine($"REFERENCES [{_schemaName}].[{StringHelper.NormalizeName(targetModel)}]([{targetPrimaryKey}]) ON DELETE {onDeleteAction}");
            sb.AppendLine("GO");
            sb.AppendLine();

            AppLog.Debug("Generated foreign key constraint {ConstraintName} for field {FieldName} in table {TableName}",
                constraintName, fieldName, tableName);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate all foreign key constraints for the document
    /// </summary>
    public string GenerateAllForeignKeyConstraints()
    {
        var sb = new StringBuilder();

        // Add header comment
        sb.AppendLine("-- Foreign key constraints");
        sb.AppendLine("-- ======================");
        sb.AppendLine();

        // Get only non-abstract models
        var nonAbstractModels = _document.Models
            .Where(m => !m.BaseModel.IsAbstract)
            .ToList();

        foreach (var model in nonAbstractModels)
        {
            var constraints = GenerateForeignKeyConstraints(model);
            if (!string.IsNullOrEmpty(constraints))
            {
                sb.Append(constraints);
            }
        }

        return sb.ToString();
    }
}