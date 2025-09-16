namespace MDDBooster.Builders.MsSql;

/// <summary>
/// Configuration for MS-SQL builder
/// </summary>
public class MsSqlBuilderConfig : IBuilderConfig
{
    /// <summary>
    /// Path to the project directory
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Relative path for table files (from project path)
    /// </summary>
    public string TablePath { get; set; } = "dbo/Tables_";

    /// <summary>
    /// Generate individual files for each table
    /// </summary>
    public bool GenerateIndividualFiles { get; set; } = true;

    /// <summary>
    /// Generate schema only (no triggers, etc.)
    /// </summary>
    public bool SchemaOnly { get; set; } = true;

    /// <summary>
    /// Use CREATE IF NOT EXISTS syntax
    /// </summary>
    public bool UseCreateIfNotExists { get; set; } = true;

    /// <summary>
    /// Include indexes in generated SQL
    /// </summary>
    public bool IncludeIndexes { get; set; } = true;

    /// <summary>
    /// Include foreign key constraints in generated SQL
    /// </summary>
    public bool GenerateForeignKeys { get; set; } = true;

    /// <summary>
    /// Use CASCADE delete for foreign keys
    /// </summary>
    public bool CascadeDelete { get; set; } = true;

    /// <summary>
    /// Clear all files in the output directory before generating new ones
    /// </summary>
    public bool ClearOutputDirectoryBeforeGeneration { get; set; } = true;

    /// <summary>
    /// Schema name to use (overrides document namespace)
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Use document namespace as schema name
    /// </summary>
    public bool UseSchemaNamespace { get; set; } = false;

    /// <summary>
    /// Generate triggers for framework attributes
    /// </summary>
    public bool GenerateTriggers { get; set; } = false;

    /// <summary>
    /// Get the full output path for this builder
    /// </summary>
    public string GetFullOutputPath()
    {
        if (string.IsNullOrEmpty(ProjectPath))
            return string.Empty;

        if (string.IsNullOrEmpty(TablePath))
            return ProjectPath;

        return Path.Combine(ProjectPath, TablePath);
    }
}