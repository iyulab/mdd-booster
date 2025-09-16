namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// Registration module for ServerProject Builder components
/// </summary>
public static class ServerProjectModule
{
    /// <summary>
    /// Registers all ServerProject Builder components with the MDDParserOptions
    /// </summary>
    public static MDDParserOptions UseServerProjectBuilder(this MDDParserOptions options)
    {
        // No specific parsers or processors needed for server project generation
        // The builder uses the standard model information from the document

        return options;
    }

    /// <summary>
    /// Creates a server project generator for the specified MDDDocument
    /// </summary>
    public static ServerProjectGenerator CreateServerProjectGenerator(
        this MDDDocument document,
        string projectNamespace,
        string gqlPath = "Gql_",
        string servicesPath = "Services_",
        bool usePartialClasses = true,
        bool generateODataServices = true)
    {
        var config = new ServerProjectConfig
        {
            Namespace = projectNamespace,
            GqlPath = gqlPath,
            ServicesPath = servicesPath,
            UsePartialClasses = usePartialClasses,
            GenerateODataServices = generateODataServices,
            GenerateIndividualFiles = true,
            GenerateRepositories = true,
            GenerateGraphTypes = true,
            GenerateQueries = true,
            GenerateFieldTypes = true,
            GenerateValidationRules = true
        };

        return new ServerProjectGenerator(document, config);
    }

    /// <summary>
    /// Creates a server project generator builder for the specified MDDDocument
    /// </summary>
    public static ServerProjectGeneratorBuilder CreateServerProjectGeneratorBuilder(this MDDDocument document)
    {
        return new ServerProjectGeneratorBuilder(document);
    }
}