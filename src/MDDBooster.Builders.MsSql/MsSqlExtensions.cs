using MDDBooster.Models;
using MDDBooster.Builders.MsSql.Helpers;

namespace MDDBooster.Builders.MsSql;

public static class MsSqlExtensions
{

    /// <summary>
    /// Extension method to generate SQL for a field
    /// </summary>
    public static string ToSqlColumnDefinition(this MDDField field, string sqlType)
    {
        var nullableStr = field.BaseField.IsNullable ? "NULL" : "NOT NULL";
        var defaultStr = !string.IsNullOrEmpty(field.BaseField.DefaultValue)
            ? $" DEFAULT {SqlHelpers.TransformDefaultValue(field.BaseField.DefaultValue, field.BaseField.Type)}"
            : "";

        var result = $"[{field.BaseField.Name}] {sqlType} {nullableStr}{defaultStr}";

        // Check for special attributes
        if (field.BaseField.IsPrimaryKey)
        {
            result += " PRIMARY KEY";
        }

        return result;
    }

    /// <summary>
    /// Extension method to get SQL triggers for a specific field
    /// </summary>
    public static string GetSqlTriggers(this MDDField field, string tableName, string schema)
    {
        var sb = new StringBuilder();

        // Generate trigger for insert value
        if (field.ExtendedMetadata.ContainsKey("InsertValue"))
        {
            var insertValue = field.ExtendedMetadata["InsertValue"].ToString();
            string sqlValue = SqlHelpers.GetSpecialCommandSqlValue(insertValue);

            sb.AppendLine($"-- Trigger for inserting {field.BaseField.Name}");
            // Trigger definition would go here
        }

        // Generate trigger for update value
        if (field.ExtendedMetadata.ContainsKey("UpdateValue"))
        {
            var updateValue = field.ExtendedMetadata["UpdateValue"].ToString();
            string sqlValue = SqlHelpers.GetSpecialCommandSqlValue(updateValue);

            sb.AppendLine($"-- Trigger for updating {field.BaseField.Name}");
            // Trigger definition would go here
        }

        return sb.ToString();
    }
}