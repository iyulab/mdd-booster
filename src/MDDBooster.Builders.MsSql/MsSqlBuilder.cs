namespace MDDBooster.Builders.MsSql;

/// <summary>
/// MS-SQL script generator
/// </summary>
public class MsSqlBuilder : IBuilder
{
    /// <summary>
    /// Builder type identifier
    /// </summary>
    public string BuilderType => "DatabaseProject";

    /// <summary>
    /// Create a builder config instance specific to this builder
    /// </summary>
    public IBuilderConfig CreateDefaultConfig()
    {
        return new MsSqlBuilderConfig
        {
            ProjectPath = string.Empty,
            TablePath = "dbo/Tables_",
            GenerateIndividualFiles = true,
            SchemaOnly = true,
            UseCreateIfNotExists = true,
            IncludeIndexes = true,
            ClearOutputDirectoryBeforeGeneration = true,
            GenerateTriggers = false,
            GenerateForeignKeys = true,
            CascadeDelete = true
        };
    }

    /// <summary>
    /// Process an MDD document with the provided configuration
    /// </summary>
    public bool Process(MDDDocument document, IBuilderConfig config)
    {
        if (!(config is MsSqlBuilderConfig msSqlConfig))
        {
            AppLog.Error("Invalid configuration type for MsSqlBuilder");
            return false;
        }

        string outputDir = msSqlConfig.GetFullOutputPath();
        if (string.IsNullOrEmpty(outputDir))
        {
            AppLog.Error("No output directory specified for MsSqlBuilder");
            return false;
        }

        AppLog.Information("Generating MS-SQL, output directory: {OutputDir}", outputDir);

        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(outputDir))
            {
                AppLog.Debug("Creating output directory: {OutputDir}", outputDir);
                Directory.CreateDirectory(outputDir);
            }
            // Clear existing files in the directory if configured to do so
            else if (msSqlConfig.ClearOutputDirectoryBeforeGeneration)
            {
                AppLog.Information("Clearing output directory: {OutputDir}", outputDir);
                ClearDirectory(outputDir);
            }

            // Validate cascade paths before generating SQL
            AppLog.Debug("Validating cascade paths for SQL Server compatibility");
            var cascadeValidator = new Processors.CascadePathValidator(document);
            var cascadeWarnings = cascadeValidator.ValidateCascadePaths();

            // Log cascade validation warnings
            foreach (var warning in cascadeWarnings)
            {
                if (warning.Severity == Processors.CascadePathSeverity.Error)
                {
                    AppLog.Warning("[CASCADE VALIDATION] {Message}", warning.Message);
                    AppLog.Warning("[CASCADE VALIDATION] {Suggestion}", warning.GetSuggestion());
                }
                else
                {
                    AppLog.Information("[CASCADE VALIDATION] {Message}", warning.Message);
                }
            }

            if (cascadeWarnings.Any(w => w.Severity == Processors.CascadePathSeverity.Error))
            {
                AppLog.Warning("[CASCADE VALIDATION] Found {Count} cascade path conflicts that may cause SQL Server errors",
                    cascadeWarnings.Count(w => w.Severity == Processors.CascadePathSeverity.Error));
                AppLog.Warning("[CASCADE VALIDATION] Use @reference(Table)! syntax to set NO ACTION cascade behavior");
            }
            else
            {
                AppLog.Information("[CASCADE VALIDATION] No cascade path conflicts detected");
            }

            // Create SQL generator
            AppLog.Debug("Creating SQL script generator");
            var scriptGenerator = new MsSqlScriptGenerator(
                document,
                msSqlConfig.UseSchemaNamespace,
                msSqlConfig.SchemaName,
                msSqlConfig.GenerateTriggers,
                msSqlConfig.GenerateForeignKeys);

            // Generate individual table files if enabled
            if (msSqlConfig.GenerateIndividualFiles)
            {
                GenerateIndividualTableFiles(
                    document,
                    outputDir,
                    msSqlConfig.SchemaOnly,
                    msSqlConfig.UseSchemaNamespace,
                    msSqlConfig.SchemaName,
                    msSqlConfig.GenerateTriggers,
                    msSqlConfig.GenerateForeignKeys,
                    msSqlConfig.CascadeDelete);
            }

            AppLog.Information("MS-SQL generation completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error generating MS-SQL");
            return false;
        }
    }

    /// <summary>
    /// Clears all files in the specified directory but leaves the directory structure intact
    /// </summary>
    private void ClearDirectory(string directory)
    {
        try
        {
            // Get all files in the directory
            string[] files = Directory.GetFiles(directory);
            int fileCount = files.Length;

            // Delete each file
            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                    AppLog.Debug("Deleted file: {FilePath}", file);
                }
                catch (Exception ex)
                {
                    AppLog.Warning(ex, "Failed to delete file: {FilePath}", file);
                }
            }

            AppLog.Information("Cleared {Count} files from directory: {Directory}", fileCount, directory);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error clearing directory: {Directory}", directory);
            throw;
        }
    }

    private static void GenerateIndividualTableFiles(
        MDDDocument document,
        string outputDir,
        bool schemaOnly,
        bool useSchemaNamespace,
        string schemaNameOverride,
        bool generateTriggers,
        bool generateForeignKeys,
        bool cascadeDelete)
    {
        AppLog.Information("Generating individual table SQL files");

        try
        {
            // Loop through all non-abstract models
            var nonAbstractModels = document.Models.Where(m => !m.BaseModel.IsAbstract).ToList();

            // Create generators
            string schemaName = useSchemaNamespace ? document.BaseDocument.Namespace : schemaNameOverride;
            var tableGenerator = new TableDefinitionGenerator(document, schemaName);
            var indexGenerator = new IndexDefinitionGenerator(document, schemaName);
            var triggerGenerator = new TriggerDefinitionGenerator(document, schemaName);
            var foreignKeyGenerator = new ForeignKeyConstraintGenerator(document, schemaName, cascadeDelete);

            foreach (var model in nonAbstractModels)
            {
                // Generate SQL for table definition
                var tableSql = tableGenerator.GenerateTable(model);

                // Add indexes
                var indexesSql = indexGenerator.GenerateIndexes(model);
                if (!string.IsNullOrWhiteSpace(indexesSql))
                {
                    tableSql += "\n\n-- Indexes\n" + indexesSql;
                }

                // Add foreign key constraints if enabled
                if (generateForeignKeys)
                {
                    var foreignKeysSql = foreignKeyGenerator.GenerateForeignKeyConstraints(model);
                    if (!string.IsNullOrWhiteSpace(foreignKeysSql))
                    {
                        tableSql += "\n\n-- Foreign Keys\n" + foreignKeysSql;
                    }
                }

                // Add triggers if not schema-only and triggers are enabled
                if (!schemaOnly && generateTriggers)
                {
                    var sb = new StringBuilder();
                    triggerGenerator.GenerateInsertTriggers(sb, model);
                    triggerGenerator.GenerateUpdateTriggers(sb, model);

                    var triggers = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(triggers))
                    {
                        tableSql += "\n\n-- Triggers\n" + triggers;
                    }
                }

                // Write to file
                string outputPath = Path.Combine(outputDir, $"{model.BaseModel.Name}.sql");
                AppLog.Debug("Writing table SQL to file: {OutputPath}", outputPath);
                File.WriteAllText(outputPath, tableSql);
            }

            AppLog.Information("Generated {Count} individual table SQL files", nonAbstractModels.Count);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error generating individual table SQL files");
        }
    }
}