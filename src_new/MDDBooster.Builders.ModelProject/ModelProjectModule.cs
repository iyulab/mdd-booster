namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Registration module for ModelProject Builder components
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

        // Add model processors
        options.ModelProcessors.Add(new ModelAttributeProcessor());

        return options;
    }

    /// <summary>
    /// Creates a model generator for the specified MDDDocument
    /// </summary>
    public static ModelGenerator CreateModelGenerator(
        this MDDDocument document,
        string projectNamespace,
        bool generateNavigationProperties = true,
        bool usePartialClasses = true,
        bool useNullableReferenceTypes = true)
    {
        var config = new ModelProjectConfig
        {
            Namespace = projectNamespace,
            GenerateNavigationProperties = generateNavigationProperties,
            UsePartialClasses = usePartialClasses,
            UseNullableReferenceTypes = useNullableReferenceTypes
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
        bool generateNavigationProperties = true,
        bool usePartialClasses = true,
        bool useNullableReferenceTypes = true)
    {
        var config = new ModelProjectConfig
        {
            Namespace = projectNamespace,
            GenerateNavigationProperties = generateNavigationProperties,
            UsePartialClasses = usePartialClasses,
            UseNullableReferenceTypes = useNullableReferenceTypes,
            GenerateGqlSearchRequest = true,
            GqlSearchRequestPath = "Gql_"
        };

        return new ModelGenerator(document, config);
    }

    /// <summary>
    /// Extension method to enable GraphQL search request generation in ModelGeneratorBuilder
    /// </summary>
    public static ModelGeneratorBuilder WithGqlSearchRequests(this ModelGeneratorBuilder builder, bool generate = true, string path = "Gql_")
    {
        // Note: This extension method is implemented for consistency
        // The actual implementation would need access to the internal config of ModelGeneratorBuilder
        // For now, users should call the builder methods directly
        return builder.WithGqlSearchRequests(generate, path);
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
}