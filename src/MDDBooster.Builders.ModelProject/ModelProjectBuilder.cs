namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Builder that generates C# entity model classes using centralized helpers
/// </summary>
public class ModelProjectBuilder : IBuilder
{
    public string BuilderType => "ModelProject";

    /// <summary>
    /// Create a default configuration for this builder
    /// </summary>
    public IBuilderConfig CreateDefaultConfig()
    {
        return new ModelProjectConfig
        {
            ProjectPath = string.Empty,
            Namespace = "YourNamespace",
            ModelsPath = "Entity_",
            InterfacesPath = "Models_",
            EnumsPath = "Models_",
            GqlSearchRequestPath = "Gql_",
            GenerateNavigationProperties = true,
            UsePartialClasses = true,
            ImplementINotifyPropertyChanged = false,
            UseDateTimeOffset = false,
            UseNullableReferenceTypes = true,
            DefaultStringLength = 50,
            GenerateGqlSearchRequest = true,
            Cleanup = true
        };
    }

    /// <summary>
    /// Process an MDD document with the provided configuration
    /// </summary>
    public bool Process(MDDDocument document, IBuilderConfig config)
    {
        AppLog.Information("Starting ModelProject builder processing");

        if (!(config is ModelProjectConfig modelConfig))
        {
            AppLog.Error("Invalid configuration type for ModelProjectBuilder");
            return false;
        }

        try
        {
            // Clear model utility cache before processing
            ModelUtilities.ClearFieldsCache();

            // Initialize helpers with common patterns
            InitializeHelpers();

            // Create generator with the given configuration
            var generator = new ModelGenerator(document, modelConfig);

            // Clean up directories if configured
            if (modelConfig.Cleanup)
            {
                CleanupDirectories(modelConfig);
            }

            // Generate model classes
            GenerateModelClasses(generator, modelConfig);

            // Generate interfaces
            GenerateInterfaces(generator, modelConfig);

            // Generate enums
            GenerateEnums(generator, modelConfig);

            // Generate GraphQL search requests if configured
            GenerateGqlSearchRequests(generator, modelConfig);

            AppLog.Information("ModelProject builder processing completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error generating model project");
            return false;
        }
    }

    /// <summary>
    /// Initialize helpers with project-specific patterns
    /// </summary>
    private void InitializeHelpers()
    {
        // Initialize navigation property helper with common patterns
        NavigationPropertyHelper.InitializeCommonPatterns();

        // Initialize attribute generation helper with common patterns
        AttributeGenerationHelper.InitializeCommonPatterns();

        // Add project-specific namespace mappings
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping("Entity_", "Entity");
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping("Gql_", "Gql");

        AppLog.Debug("Initialized helpers with common patterns");
    }

    /// <summary>
    /// Generate C# model classes
    /// </summary>
    private void GenerateModelClasses(ModelGenerator generator, ModelProjectConfig config)
    {
        AppLog.Information("Generating model classes");

        string outputDir = Path.Combine(config.ProjectPath, config.ModelsPath);
        EnsureDirectoryExists(outputDir);

        var models = generator.Document.Models.ToList();
        AppLog.Information("Found {ModelCount} models to generate", models.Count);

        foreach (var model in models)
        {
            ErrorHandling.ExecuteSafely(() => {
                // Skip abstract models if configured
                if (model.BaseModel.IsAbstract && !config.GenerateAbstractModels)
                {
                    AppLog.Debug("Skipping abstract model: {ModelName}", model.BaseModel.Name);
                    return;
                }

                // Generate model class code
                string modelCode = generator.GenerateModelClass(model);
                if (string.IsNullOrEmpty(modelCode))
                {
                    AppLog.Warning("Empty model code generated for {ModelName}", model.BaseModel.Name);
                    return;
                }

                // Write to file
                string fileName = $"{model.BaseModel.Name}.cs";
                string outputPath = Path.Combine(outputDir, fileName);
                File.WriteAllText(outputPath, modelCode);

                AppLog.Information("Generated model class: {FileName}", fileName);
            }, "Failed to generate model {ModelName}", model.BaseModel.Name);
        }

        AppLog.Information("Completed generating {Count} model classes", models.Count);
    }

    /// <summary>
    /// Generate C# interfaces
    /// </summary>
    private void GenerateInterfaces(ModelGenerator generator, ModelProjectConfig config)
    {
        if (!config.GenerateInterface)
        {
            AppLog.Information("Skipping interface generation as per configuration");
            return;
        }

        AppLog.Information("Generating interfaces");

        string outputDir = Path.Combine(config.ProjectPath, config.InterfacesPath);
        EnsureDirectoryExists(outputDir);

        var interfaces = generator.Document.Interfaces.ToList();
        AppLog.Information("Found {InterfaceCount} interfaces to generate", interfaces.Count);

        foreach (var iface in interfaces)
        {
            ErrorHandling.ExecuteSafely(() => {
                string interfaceCode = generator.GenerateInterface(iface);
                if (string.IsNullOrEmpty(interfaceCode))
                {
                    AppLog.Warning("Empty interface code generated for {InterfaceName}", iface.BaseInterface.Name);
                    return;
                }

                string fileName = $"{iface.BaseInterface.Name}.cs";
                string outputPath = Path.Combine(outputDir, fileName);
                File.WriteAllText(outputPath, interfaceCode);

                AppLog.Information("Generated interface: {FileName}", fileName);
            }, "Failed to generate interface {InterfaceName}", iface.BaseInterface.Name);
        }

        AppLog.Information("Completed generating {Count} interfaces", interfaces.Count);
    }

    /// <summary>
    /// Generate C# enums
    /// </summary>
    private void GenerateEnums(ModelGenerator generator, ModelProjectConfig config)
    {
        AppLog.Information("Generating enums");

        string outputDir = Path.Combine(config.ProjectPath, config.EnumsPath);
        EnsureDirectoryExists(outputDir);

        var enums = generator.Document.Enums.ToList();
        AppLog.Information("Found {EnumCount} enums to generate", enums.Count);

        foreach (var enum_ in enums)
        {
            ErrorHandling.ExecuteSafely(() => {
                string enumCode = generator.GenerateEnum(enum_);
                if (string.IsNullOrEmpty(enumCode))
                {
                    AppLog.Warning("Empty enum code generated for {EnumName}", enum_.BaseEnum.Name);
                    return;
                }

                string fileName = $"{enum_.BaseEnum.Name}.cs";
                string outputPath = Path.Combine(outputDir, fileName);
                File.WriteAllText(outputPath, enumCode);

                AppLog.Information("Generated enum: {FileName}", fileName);
            }, "Failed to generate enum {EnumName}", enum_.BaseEnum.Name);
        }

        AppLog.Information("Completed generating {Count} enums", enums.Count);
    }

    /// <summary>
    /// Generate GraphQL search request classes
    /// </summary>
    private void GenerateGqlSearchRequests(ModelGenerator generator, ModelProjectConfig config)
    {
        if (!config.GenerateGqlSearchRequest)
        {
            AppLog.Information("Skipping GraphQL search request generation as per configuration");
            return;
        }

        AppLog.Information("Generating GraphQL search request classes");

        string outputDir = Path.Combine(config.ProjectPath, config.GqlSearchRequestPath);
        EnsureDirectoryExists(outputDir);

        var eligibleModels = generator.Document.Models.Where(m => !m.BaseModel.IsAbstract).ToList();
        AppLog.Information("Found {ModelCount} eligible models for GraphQL search request generation", eligibleModels.Count);

        foreach (var model in eligibleModels)
        {
            ErrorHandling.ExecuteSafely(() => {
                string gqlCode = generator.GenerateGqlSearchRequestClasses(model);
                if (string.IsNullOrEmpty(gqlCode))
                {
                    AppLog.Warning("Empty GraphQL search request code generated for {ModelName}", model.BaseModel.Name);
                    return;
                }

                string fileName = $"{model.BaseModel.Name}SearchRequest.cs";
                string outputPath = Path.Combine(outputDir, fileName);
                File.WriteAllText(outputPath, gqlCode);

                AppLog.Information("Generated GraphQL search request: {FileName}", fileName);
            }, "Failed to generate GraphQL search request for {ModelName}", model.BaseModel.Name);
        }

        AppLog.Information("Completed GraphQL search request generation for {ProcessedCount} models", eligibleModels.Count);
    }

    /// <summary>
    /// Clean up output directories before generation
    /// </summary>
    private void CleanupDirectories(ModelProjectConfig config)
    {
        AppLog.Information("Cleaning up output directories");

        ErrorHandling.ExecuteSafely(() => {
            var directoriesToClean = new List<(string path, string description)>
            {
                (Path.Combine(config.ProjectPath, config.ModelsPath), "models"),
                (Path.Combine(config.ProjectPath, config.EnumsPath), "enums")
            };

            // Add interfaces directory if generating interfaces and it's different from models
            if (config.GenerateInterface)
            {
                string interfacesDir = Path.Combine(config.ProjectPath, config.InterfacesPath);
                if (interfacesDir != Path.Combine(config.ProjectPath, config.ModelsPath))
                {
                    directoriesToClean.Add((interfacesDir, "interfaces"));
                }
            }

            // Add GraphQL directory if generating search requests
            if (config.GenerateGqlSearchRequest)
            {
                directoriesToClean.Add((Path.Combine(config.ProjectPath, config.GqlSearchRequestPath), "GraphQL search requests"));
            }

            foreach (var (path, description) in directoriesToClean)
            {
                if (Directory.Exists(path))
                {
                    ClearDirectory(path, description);
                }
            }
        }, "Error cleaning up directories");
    }

    /// <summary>
    /// Clears all .cs files in a directory
    /// </summary>
    private void ClearDirectory(string directory, string description)
    {
        ErrorHandling.ExecuteSafely(() => {
            string[] files = Directory.GetFiles(directory, "*.cs");
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

            AppLog.Information("Cleaned {Count} {Description} files from directory: {Directory}",
                deletedCount, description, directory);
        }, "Error clearing {Description} directory: {Directory}", description, directory);
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