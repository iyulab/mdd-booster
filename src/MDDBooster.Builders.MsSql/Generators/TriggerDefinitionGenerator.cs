using MDDBooster.Utilities;
using MDDBooster.Builders.MsSql.Helpers;
using M3LParser.Helpers;

namespace MDDBooster.Builders.MsSql.Generators;

/// <summary>
/// Generator for SQL trigger definitions
/// </summary>
public class TriggerDefinitionGenerator
{
    private readonly MDDDocument _document;
    private readonly string _schemaName;

    public TriggerDefinitionGenerator(MDDDocument document, string schemaName)
    {
        _document = document;
        _schemaName = schemaName;
    }

    /// <summary>
    /// Generate all triggers for the document
    /// </summary>
    public string GenerateTriggers()
    {
        var sb = new StringBuilder();

        foreach (var model in _document.Models)
        {
            // Skip abstract models
            if (model.BaseModel.IsAbstract)
            {
                continue;
            }

            // Generate triggers only if needed - don't create empty triggers
            if (NeedsTriggers(model))
            {
                GenerateInsertTriggers(sb, model);
                GenerateUpdateTriggers(sb, model);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if a model needs triggers based on its fields' attributes
    /// </summary>
    private bool NeedsTriggers(MDDModel model)
    {
        var allFields = ModelUtilities.GetAllFields(_document, model);

        return allFields.Any(f =>
            f.ExtendedMetadata.ContainsKey("InsertValue") ||
            f.ExtendedMetadata.ContainsKey("UpdateValue"));
    }

    /// <summary>
    /// Generate all triggers for a specific model
    /// </summary>
    public string GenerateTriggersForModel(MDDModel model)
    {
        if (model.BaseModel.IsAbstract)
        {
            return string.Empty;
        }

        if (!NeedsTriggers(model))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        GenerateInsertTriggers(sb, model);
        GenerateUpdateTriggers(sb, model);
        return sb.ToString();
    }

    /// <summary>
    /// Generate insert triggers for a model
    /// </summary>
    public void GenerateInsertTriggers(StringBuilder sb, MDDModel model)
    {
        // Get all fields (including inherited ones)
        var allFields = ModelUtilities.GetAllFields(_document, model);

        // Check if any field has insert values
        var fieldsWithInsertValues = allFields
            .Where(f => f.ExtendedMetadata.ContainsKey("InsertValue"))
            .ToList();

        if (fieldsWithInsertValues.Any())
        {
            var tableName = StringHelper.NormalizeName(model.BaseModel.Name);

            sb.AppendLine($"CREATE TRIGGER [TR_{tableName}_Insert]");
            sb.AppendLine($"ON [{_schemaName}].[{tableName}]");
            sb.AppendLine("INSTEAD OF INSERT");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();

            // Build insert statement
            sb.Append("    INSERT INTO [" + _schemaName + "].[" + tableName + "] (");

            // Get all field names
            var fieldNames = allFields
                .Where(f => !f.ShouldExcludeFromSql())
                .Select(f => StringHelper.NormalizeName(f.BaseField.Name))
                .ToList();

            sb.AppendLine(string.Join(", ", fieldNames.Select(f => $"[{f}]")) + ")");

            // Build select statement
            sb.Append("    SELECT ");

            // Map fields with their values
            var fieldValues = new List<string>();
            foreach (var field in allFields.Where(f => !f.ShouldExcludeFromSql()))
            {
                var fieldName = StringHelper.NormalizeName(field.BaseField.Name);

                if (field.ExtendedMetadata.ContainsKey("InsertValue"))
                {
                    var insertValue = field.ExtendedMetadata["InsertValue"].ToString();
                    fieldValues.Add(SqlHelpers.GetSpecialCommandSqlValue(insertValue));
                }
                else
                {
                    fieldValues.Add($"i.[{fieldName}]");
                }
            }

            sb.AppendLine(string.Join(", ", fieldValues));
            sb.AppendLine("    FROM INSERTED i;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Generate update triggers for a model
    /// </summary>
    public void GenerateUpdateTriggers(StringBuilder sb, MDDModel model)
    {
        // Get all fields (including inherited ones)
        var allFields = ModelUtilities.GetAllFields(_document, model);

        // Check if any field has update values
        var fieldsWithUpdateValues = allFields
            .Where(f => f.ExtendedMetadata.ContainsKey("UpdateValue"))
            .ToList();

        if (fieldsWithUpdateValues.Any())
        {
            var tableName = StringHelper.NormalizeName(model.BaseModel.Name);

            sb.AppendLine($"CREATE TRIGGER [TR_{tableName}_Update]");
            sb.AppendLine($"ON [{_schemaName}].[{tableName}]");
            sb.AppendLine("INSTEAD OF UPDATE");
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();

            // Build update statement
            sb.AppendLine($"    UPDATE t SET");

            // Map fields with their update values
            var updateStatements = new List<string>();

            foreach (var field in allFields.Where(f => !f.ShouldExcludeFromSql()))
            {
                var fieldName = StringHelper.NormalizeName(field.BaseField.Name);

                if (field.ExtendedMetadata.ContainsKey("UpdateValue"))
                {
                    var updateValue = field.ExtendedMetadata["UpdateValue"].ToString();
                    updateStatements.Add($"        t.[{fieldName}] = {SqlHelpers.GetSpecialCommandSqlValue(updateValue)}");
                }
                else
                {
                    updateStatements.Add($"        t.[{fieldName}] = i.[{fieldName}]");
                }
            }

            sb.AppendLine(string.Join(",\n", updateStatements));
            sb.AppendLine($"    FROM [{_schemaName}].[{tableName}] t");
            sb.AppendLine("    INNER JOIN INSERTED i ON t._id = i._id;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            sb.AppendLine();
        }
    }
}