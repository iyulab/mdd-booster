namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// Builder that generates GraphQL server classes and OData services
/// </summary>
public class ServerProjectBuilder : IBuilder
{
    public string BuilderType => "ServerProject";

    /// <summary>
    /// Create a default configuration for this builder
    /// </summary>
    public IBuilderConfig CreateDefaultConfig()
    {
        return new ServerProjectConfig
        {
            ProjectPath = string.Empty,
            Namespace = "YourNamespace.MainServer",
            GqlPath = "Gql_",
            ServicesPath = "Services_",
            GenerateIndividualFiles = true,
            Cleanup = true,
            GenerateRepositories = true,
            GenerateGraphTypes = true,
            GenerateQueries = true,
            GenerateFieldTypes = true,
            GenerateValidationRules = true,
            GenerateODataServices = true,
            UsePartialClasses = true,
            DefaultPageSize = 50,
            MaxPageSize = 1000
        };
    }

    /// <summary>
    /// Process an MDD document with the provided configuration
    /// </summary>
    public bool Process(MDDDocument document, IBuilderConfig config)
    {
        AppLog.Information("Starting ServerProject builder processing");

        if (!(config is ServerProjectConfig serverConfig))
        {
            AppLog.Error("Invalid configuration type for ServerProjectBuilder");
            return false;
        }

        try
        {
            // Create generator with the given configuration
            var generator = new ServerProjectGenerator(document, serverConfig);

            // Clean up directories if configured
            if (serverConfig.Cleanup)
            {
                CleanupDirectories(serverConfig);
            }

            // Generate main GraphQL files
            GenerateMainGraphQLFiles(generator, serverConfig);

            // Generate model-specific GraphQL files
            GenerateModelGraphQLFiles(generator, serverConfig);

            // Generate OData services
            if (serverConfig.GenerateODataServices)
            {
                GenerateODataServices(generator, serverConfig);
            }

            AppLog.Information("ServerProject builder processing completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error generating server project");
            return false;
        }
    }

    /// <summary>
    /// Generate main GraphQL files (Schema, Query, etc.)
    /// </summary>
    private void GenerateMainGraphQLFiles(ServerProjectGenerator generator, ServerProjectConfig config)
    {
        AppLog.Information("Generating main GraphQL files");

        string outputDir = Path.Combine(config.ProjectPath, config.GqlPath);
        EnsureDirectoryExists(outputDir);

        // Generate AppGqlQuery
        ErrorHandling.ExecuteSafely(() => {
            string queryCode = generator.GenerateAppGqlQuery();
            if (!string.IsNullOrEmpty(queryCode))
            {
                string outputPath = Path.Combine(outputDir, "AppGqlQuery.cs");
                File.WriteAllText(outputPath, queryCode);
                AppLog.Information("Generated AppGqlQuery.cs");
            }
        }, "Failed to generate AppGqlQuery");

        // Generate AppGqlSchema
        ErrorHandling.ExecuteSafely(() => {
            string schemaCode = generator.GenerateAppGqlSchema();
            if (!string.IsNullOrEmpty(schemaCode))
            {
                string outputPath = Path.Combine(outputDir, "AppGqlSchema.cs");
                File.WriteAllText(outputPath, schemaCode);
                AppLog.Information("Generated AppGqlSchema.cs");
            }
        }, "Failed to generate AppGqlSchema");

        // Generate AppGqlValidationRule
        if (config.GenerateValidationRules)
        {
            ErrorHandling.ExecuteSafely(() => {
                string validationCode = generator.GenerateAppGqlValidationRule();
                if (!string.IsNullOrEmpty(validationCode))
                {
                    string outputPath = Path.Combine(outputDir, "AppGqlValidationRule.cs");
                    File.WriteAllText(outputPath, validationCode);
                    AppLog.Information("Generated AppGqlValidationRule.cs");
                }
            }, "Failed to generate AppGqlValidationRule");
        }
    }

    /// <summary>
    /// Generate model-specific GraphQL files
    /// </summary>
    private void GenerateModelGraphQLFiles(ServerProjectGenerator generator, ServerProjectConfig config)
    {
        AppLog.Information("Generating model-specific GraphQL files");

        // Generate files for each non-abstract model
        foreach (var model in generator.Document.Models.Where(m => !m.BaseModel.IsAbstract))
        {
            GenerateModelFiles(generator, model, config);
        }
    }

    /// <summary>
    /// Generate OData services
    /// </summary>
    private void GenerateODataServices(ServerProjectGenerator generator, ServerProjectConfig config)
    {
        AppLog.Information("Generating OData services");

        string outputDir = Path.Combine(config.ProjectPath, config.ServicesPath);
        EnsureDirectoryExists(outputDir);

        // Generate DataContext
        ErrorHandling.ExecuteSafely(() => {
            string dataContextCode = generator.GenerateDataContext();
            if (!string.IsNullOrEmpty(dataContextCode))
            {
                string outputPath = Path.Combine(outputDir, "DataContext.cs");
                File.WriteAllText(outputPath, dataContextCode);
                AppLog.Information("Generated DataContext.cs");
            }
        }, "Failed to generate DataContext");

        // Generate EntitySetBuilder
        ErrorHandling.ExecuteSafely(() => {
            string entitySetBuilderCode = generator.GenerateEntitySetBuilder();
            if (!string.IsNullOrEmpty(entitySetBuilderCode))
            {
                string outputPath = Path.Combine(outputDir, "EntitySetBuilder.cs");
                File.WriteAllText(outputPath, entitySetBuilderCode);
                AppLog.Information("Generated EntitySetBuilder.cs");
            }
        }, "Failed to generate EntitySetBuilder");
    }

    /// <summary>
    /// Generate all files for a specific model
    /// </summary>
    private void GenerateModelFiles(ServerProjectGenerator generator, MDDModel model, ServerProjectConfig config)
    {
        string modelDir = Path.Combine(config.ProjectPath, config.GqlPath, $"Gql{model.BaseModel.Name}");
        EnsureDirectoryExists(modelDir);

        // Generate FieldType
        if (config.GenerateFieldTypes)
        {
            ErrorHandling.ExecuteSafely(() => {
                string fieldTypeCode = generator.GenerateFieldType(model);
                if (!string.IsNullOrEmpty(fieldTypeCode))
                {
                    string outputPath = Path.Combine(modelDir, $"{model.BaseModel.Name}FieldType.cs");
                    File.WriteAllText(outputPath, fieldTypeCode);
                    AppLog.Information("Generated {FileName}", $"{model.BaseModel.Name}FieldType.cs");
                }
            }, "Failed to generate FieldType for {ModelName}", model.BaseModel.Name);
        }

        // Generate GraphType
        if (config.GenerateGraphTypes)
        {
            ErrorHandling.ExecuteSafely(() => {
                string graphTypeCode = generator.GenerateGraphType(model);
                if (!string.IsNullOrEmpty(graphTypeCode))
                {
                    string outputPath = Path.Combine(modelDir, $"{model.BaseModel.Name}GraphType.cs");
                    File.WriteAllText(outputPath, graphTypeCode);
                    AppLog.Information("Generated {FileName}", $"{model.BaseModel.Name}GraphType.cs");
                }
            }, "Failed to generate GraphType for {ModelName}", model.BaseModel.Name);
        }

        // Generate Query
        if (config.GenerateQueries)
        {
            ErrorHandling.ExecuteSafely(() => {
                string queryCode = generator.GenerateQuery(model);
                if (!string.IsNullOrEmpty(queryCode))
                {
                    string outputPath = Path.Combine(modelDir, $"{model.BaseModel.Name}Query.cs");
                    File.WriteAllText(outputPath, queryCode);
                    AppLog.Information("Generated {FileName}", $"{model.BaseModel.Name}Query.cs");
                }
            }, "Failed to generate Query for {ModelName}", model.BaseModel.Name);
        }

        // Generate Repository
        if (config.GenerateRepositories)
        {
            ErrorHandling.ExecuteSafely(() => {
                string repositoryCode = generator.GenerateRepository(model);
                if (!string.IsNullOrEmpty(repositoryCode))
                {
                    string outputPath = Path.Combine(modelDir, $"{model.BaseModel.Name}Repository.cs");
                    File.WriteAllText(outputPath, repositoryCode);
                    AppLog.Information("Generated {FileName}", $"{model.BaseModel.Name}Repository.cs");
                }
            }, "Failed to generate Repository for {ModelName}", model.BaseModel.Name);
        }
    }

    /// <summary>
    /// Clean up output directories before generation
    /// </summary>
    private void CleanupDirectories(ServerProjectConfig config)
    {
        ErrorHandling.ExecuteSafely(() => {
            string gqlDir = Path.Combine(config.ProjectPath, config.GqlPath);
            if (Directory.Exists(gqlDir))
            {
                AppLog.Information("Cleaning up GraphQL directory: {Directory}", gqlDir);
                ClearDirectory(gqlDir);
            }

            if (config.GenerateODataServices)
            {
                string servicesDir = Path.Combine(config.ProjectPath, config.ServicesPath);
                if (Directory.Exists(servicesDir))
                {
                    AppLog.Information("Cleaning up Services directory: {Directory}", servicesDir);
                    ClearDirectory(servicesDir);
                }
            }
        }, "Error cleaning up directories");
    }

    /// <summary>
    /// Clears all files in a directory but leaves the directory structure intact
    /// </summary>
    private void ClearDirectory(string directory)
    {
        ErrorHandling.ExecuteSafely(() => {
            // Delete all .cs files recursively
            var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
            int deletedCount = 0;

            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                    AppLog.Debug("Deleted file: {FilePath}", file);
                }
                catch (Exception ex)
                {
                    AppLog.Warning(ex, "Failed to delete file: {FilePath}", file);
                }
            }

            AppLog.Information("Cleaned {Count} files from directory: {Directory}", deletedCount, directory);
        }, "Error clearing directory: {Directory}", directory);
    }

    /// <summary>
    /// Ensure directory exists, create if it doesn't
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            AppLog.Debug("Creating directory: {DirectoryPath}", path);
            Directory.CreateDirectory(path);
        }
    }
}