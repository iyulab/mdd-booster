using MDDBooster.Builders.ModelProject;

namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Registration module for ModelProject Builder components
/// Uses centralized helpers and provides convenient extension methods
/// </summary>
public static class ModelProjectModule
{
    /// <summary>
    /// Registers all ModelProject Builder components with the MDDParserOptions
    /// </summary>
    public static MDDParserOptions UseModelProjectBuilder(this MDDParserOptions options)
    {
        // Register framework attribute parsers
        options.FrameworkAttributeParsers.Add(new ModelAttributeParser());

        // Add model processors in correct order
        options.ModelProcessors.Add(new NestedFieldProcessor()); // Process nested metadata first
        options.ModelProcessors.Add(new ModelAttributeProcessor());

        // Initialize helpers with common patterns
        InitializeHelpers();

        return options;
    }

    /// <summary>
    /// Creates a model generator for the specified MDDDocument with basic settings
    /// </summary>
    public static ModelGenerator CreateModelGenerator(
        this MDDDocument document,
        string projectNamespace,
        string projectPath,
        bool generateNavigationProperties = true,
        bool usePartialClasses = true,
        bool useNullableReferenceTypes = true)
    {
        var config = new ModelProjectConfig
        {
            ProjectPath = projectPath,
            Namespace = projectNamespace,
            GenerateNavigationProperties = generateNavigationProperties,
            UsePartialClasses = usePartialClasses,
            UseNullableReferenceTypes = useNullableReferenceTypes,
            ModelsPath = "Entity_",
            InterfacesPath = "Models",
            EnumsPath = "Models"
        };

        return new ModelGenerator(document, config);
    }

    /// <summary>
    /// Creates a model generator builder for the specified MDDDocument
    /// </summary>
    public static ModelGeneratorBuilder CreateModelGeneratorBuilder(this MDDDocument document)
    {
        return new ModelGeneratorBuilder(document);
    }

    /// <summary>
    /// Creates a model generator with GraphQL search request generation enabled
    /// </summary>
    public static ModelGenerator CreateModelGeneratorWithGql(
        this MDDDocument document,
        string projectNamespace,
        string projectPath,
        bool generateNavigationProperties = true,
        bool usePartialClasses = true,
        bool useNullableReferenceTypes = true,
        string gqlPath = "Gql_")
    {
        var config = new ModelProjectConfig
        {
            ProjectPath = projectPath,
            Namespace = projectNamespace,
            GenerateNavigationProperties = generateNavigationProperties,
            UsePartialClasses = usePartialClasses,
            UseNullableReferenceTypes = useNullableReferenceTypes,
            GenerateGqlSearchRequest = true,
            GqlSearchRequestPath = gqlPath,
            ModelsPath = "Entity_",
            InterfacesPath = "Models",
            EnumsPath = "Models"
        };

        return new ModelGenerator(document, config);
    }

    /// <summary>
    /// Creates a ModelProjectConfig with GraphQL search requests enabled
    /// </summary>
    public static ModelProjectConfig CreateConfigWithGqlSearchRequests(
        string projectNamespace,
        string projectPath,
        string gqlPath = "Gql_")
    {
        return new ModelProjectConfig
        {
            Namespace = projectNamespace,
            ProjectPath = projectPath,
            GenerateGqlSearchRequest = true,
            GqlSearchRequestPath = gqlPath,
            ModelsPath = "Entity_",
            InterfacesPath = "Models",
            EnumsPath = "Models",
            GenerateNavigationProperties = true,
            UsePartialClasses = true,
            UseNullableReferenceTypes = true,
            Cleanup = true
        };
    }

    /// <summary>
    /// Extension method to easily configure model generation with fluent API
    /// </summary>
    public static ModelGeneratorBuilder ConfigureModelGeneration(
        this MDDDocument document,
        string projectNamespace,
        string projectPath)
    {
        return document.CreateModelGeneratorBuilder()
            .WithNamespace(projectNamespace)
            .WithProjectPath(projectPath);
    }

    /// <summary>
    /// Extension method to easily configure model generation with common defaults
    /// </summary>
    public static ModelGeneratorBuilder ConfigureStandardModelGeneration(
        this MDDDocument document,
        string projectNamespace,
        string projectPath)
    {
        return document.CreateModelGeneratorBuilder()
            .WithNamespace(projectNamespace)
            .WithProjectPath(projectPath)
            .WithModelsPath("Entity_")
            .WithInterfacesPath("Models")
            .WithEnumsPath("Models")
            .WithGqlSearchRequests(true, "Gql_")
            .WithNavigationProperties(true)
            .WithPartialClasses(true)
            .WithNullableReferenceTypes(true)
            .WithStringBasedEnums("Enum") // Enable string-based enum generation by default
            .WithCleanup(true);
    }

    /// <summary>
    /// Extension method to configure model generation for Entity Framework with string-based enums
    /// </summary>
    public static ModelGeneratorBuilder ConfigureEntityFrameworkModelGeneration(
        this MDDDocument document,
        string projectNamespace,
        string projectPath)
    {
        return document.CreateModelGeneratorBuilder()
            .WithNamespace(projectNamespace)
            .WithProjectPath(projectPath)
            .WithModelsPath("Entity_")
            .WithInterfacesPath("Models")
            .WithEnumsPath("Models")
            .WithNavigationProperties(true)
            .WithPartialClasses(true)
            .WithNullableReferenceTypes(true)
            .WithStringBasedEnums("Enum") // Use string-based enums for EF compatibility
            .WithEntityFrameworkSupport()
            .WithCleanup(true);
    }

    /// <summary>
    /// Extension method to add custom patterns to the model generator
    /// </summary>
    public static ModelGeneratorBuilder WithCommonCustomizations(this ModelGeneratorBuilder builder)
    {
        return builder
            .WithCustomNavigationPattern("CreatedBy", "CreatedBy")
            .WithCustomNavigationPattern("UpdatedBy", "UpdatedBy")
            .WithCustomNavigationPattern("Owner", "Owner")
            .WithCustomDataTypePattern("email", "EmailAddress")
            .WithCustomDataTypePattern("phone", "PhoneNumber")
            .WithCustomDataTypePattern("url", "Url")
            .WithCustomDataTypePattern("password", "Password");
    }

    /// <summary>
    /// Extension method to configure for Entity Framework
    /// </summary>
    public static ModelGeneratorBuilder WithEntityFrameworkSupport(this ModelGeneratorBuilder builder)
    {
        return builder
            .WithNavigationProperties(true)
            .WithCustomSqlTypeMapping("string", "nvarchar({0})")
            .WithCustomSqlTypeMapping("text", "nvarchar(max)")
            .WithCustomSqlTypeMapping("identifier", "uniqueidentifier")
            .WithTypeConversionSettings(useDateTimeOffset: false, defaultStringLength: 255);
    }

    /// <summary>
    /// Extension method to configure for GraphQL support
    /// </summary>
    public static ModelGeneratorBuilder WithGraphQLSupport(this ModelGeneratorBuilder builder, string gqlPath = "Gql_")
    {
        return builder
            .WithGqlSearchRequests(true, gqlPath)
            .WithCustomNamespaceMapping(gqlPath, "Gql")
            .WithAttributeSettings(autoGenerateJsonIgnore: true);
    }

    /// <summary>
    /// Extension method to configure enum generation for specific scenarios
    /// </summary>
    public static ModelGeneratorBuilder WithEnumConfiguration(this ModelGeneratorBuilder builder, EnumGenerationMode mode)
    {
        return mode switch
        {
            EnumGenerationMode.StringBased => builder.WithStringBasedEnums("Enum"),
            EnumGenerationMode.DirectEnum => builder.WithDirectEnumTypes(),
            EnumGenerationMode.StringBasedWithCustomSuffix => builder.WithStringBasedEnums("Type"),
            _ => builder.WithStringBasedEnums("Enum")
        };
    }

    /// <summary>
    /// Initialize helpers with model project specific patterns
    /// </summary>
    private static void InitializeHelpers()
    {
        // Initialize navigation property helper
        NavigationPropertyHelper.InitializeCommonPatterns();

        // Initialize attribute generation helper
        AttributeGenerationHelper.InitializeCommonPatterns();

        // Add model project specific namespace mappings
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping("Entity_", "Entity");
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping("Models_", "Models");
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping("Gql_", "Gql");

        AppLog.Debug("Initialized ModelProject helpers with common patterns");
    }

    /// <summary>
    /// Gets statistics about the model generation process
    /// </summary>
    public static ModelGenerationStats GetGenerationStats(this ModelGenerator generator)
    {
        return new ModelGenerationStats
        {
            TotalModels = generator.Document.Models.Count,
            AbstractModels = generator.Document.Models.Count(m => m.BaseModel.IsAbstract),
            ConcreteModels = generator.Document.Models.Count(m => !m.BaseModel.IsAbstract),
            TotalInterfaces = generator.Document.Interfaces.Count,
            TotalEnums = generator.Document.Enums.Count,
            ModelsWithRelationships = generator.Document.Models.Count(m =>
                generator.GetModelRelationships(m.BaseModel.Name).Any()),
            EnumFields = generator.Document.Models.SelectMany(m => m.Fields)
                .Count(f => TypeConversionHelper.IsEnumField(f, generator.Document))
        };
    }

    /// <summary>
    /// Statistics about model generation
    /// </summary>
    public class ModelGenerationStats
    {
        public int TotalModels { get; set; }
        public int AbstractModels { get; set; }
        public int ConcreteModels { get; set; }
        public int TotalInterfaces { get; set; }
        public int TotalEnums { get; set; }
        public int ModelsWithRelationships { get; set; }
        public int EnumFields { get; set; }

        public override string ToString()
        {
            return $"Models: {TotalModels} ({ConcreteModels} concrete, {AbstractModels} abstract), " +
                   $"Interfaces: {TotalInterfaces}, Enums: {TotalEnums}, " +
                   $"Models with relationships: {ModelsWithRelationships}, " +
                   $"Enum fields: {EnumFields}";
        }
    }

    /// <summary>
    /// Enum generation modes
    /// </summary>
    public enum EnumGenerationMode
    {
        /// <summary>
        /// Generate enum fields as string properties with XXXEnum accessor properties
        /// </summary>
        StringBased,

        /// <summary>
        /// Generate enum fields as direct enum type properties (legacy)
        /// </summary>
        DirectEnum,

        /// <summary>
        /// Generate enum fields as string properties with XXXType accessor properties
        /// </summary>
        StringBasedWithCustomSuffix
    }
}
