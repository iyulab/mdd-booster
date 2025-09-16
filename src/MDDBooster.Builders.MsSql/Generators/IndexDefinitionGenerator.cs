using M3LParser.Helpers;
using MDDBooster.Builders.MsSql.Helpers;
using MDDBooster.Utilities;

namespace MDDBooster.Builders.MsSql.Generators;

/// <summary>
/// Generator for SQL index definitions
/// </summary>
public class IndexDefinitionGenerator
{
    private readonly MDDDocument _document;
    private readonly string _schemaName;

    public IndexDefinitionGenerator(MDDDocument document, string schemaName)
    {
        _document = document;
        _schemaName = schemaName;
    }

    /// <summary>
    /// Generate index SQL for a table
    /// </summary>
    public string GenerateIndexes(MDDModel model)
    {
        var sb = new StringBuilder();
        var tableName = StringHelper.NormalizeName(model.BaseModel.Name);

        // Get indexes from model
        var indexes = model.BaseModel.Indexes;
        if (indexes == null || !indexes.Any())
        {
            return string.Empty;
        }

        // Track unique constraints that are already defined in the table
        // These should not have duplicate indexes created
        var uniqueConstraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get all fields including inherited ones
        var allFields = ModelUtilities.GetAllFields(_document, model);
        
        // Find fields with unique constraints already defined
        foreach (var field in allFields)
        {
            if (field.BaseField.IsUnique && !field.BaseField.IsPrimaryKey)
            {
                var fieldName = StringHelper.NormalizeName(field.BaseField.Name);
                uniqueConstraints.Add(fieldName); // Add single field unique constraints
            }
        }
        
        // Also add multi-column unique constraints that are defined in the model
        foreach (var index in indexes.Where(i => i.IsUnique))
        {
            if (index.Fields != null && index.Fields.Any())
            {
                // Create a key that represents this field combination
                var constraintKey = string.Join("_", index.Fields.Select(StringHelper.NormalizeName));
                uniqueConstraints.Add(constraintKey);
            }
        }

        foreach (var index in indexes)
        {
            if (index.Fields == null || !index.Fields.Any())
            {
                continue;
            }

            // Skip creating indexes for unique constraints that already exist
            // to avoid duplicate index/constraint definitions
            if (index.IsUnique)
            {
                var fieldKey = string.Join("_", index.Fields.Select(StringHelper.NormalizeName));
                if (uniqueConstraints.Contains(fieldKey))
                {
                    AppLog.Debug("Skipping duplicate index creation for unique constraint: {ConstraintName} on table {TableName}",
                        fieldKey, tableName);
                    continue; // Skip this index since it's already created as a constraint
                }
            }

            var indexType = index.IsUnique ? "UNIQUE" : "";

            // Generate proper index name
            var indexName = index.Name;
            if (string.IsNullOrEmpty(indexName))
            {
                var prefix = index.IsUnique ? "UK" : "IX";
                indexName = $"{prefix}_{tableName}_{string.Join("_", index.Fields.Select(StringHelper.NormalizeName))}";
            }

            sb.AppendLine($"CREATE {indexType} INDEX [{indexName}] ON [{_schemaName}].[{tableName}]");
            sb.AppendLine("(");

            var fieldDefs = new List<string>();
            foreach (var field in index.Fields)
            {
                fieldDefs.Add($"    [{StringHelper.NormalizeName(field)}] ASC");
            }

            sb.AppendLine(string.Join(",\n", fieldDefs));
            sb.AppendLine(")");
            sb.AppendLine("GO");
        }

        return sb.ToString();
    }
}