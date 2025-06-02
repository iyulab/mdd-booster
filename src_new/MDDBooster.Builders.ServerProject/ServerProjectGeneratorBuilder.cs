namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// Builder for creating ServerProjectGenerator instances with fluent configuration
/// </summary>
public class ServerProjectGeneratorBuilder
{
    private readonly MDDDocument _document;
    private readonly ServerProjectConfig _config = new ServerProjectConfig();

    public ServerProjectGeneratorBuilder(MDDDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Sets the root namespace for generated code
    /// </summary>
    public ServerProjectGeneratorBuilder WithNamespace(string ns)
    {
        _config.Namespace = ns;
        return this;
    }

    /// <summary>
    /// Sets the path for GraphQL files
    /// </summary>
    public ServerProjectGeneratorBuilder WithGqlPath(string path)
    {
        _config.GqlPath = path;
        return this;
    }

    /// <summary>
    /// Sets the path for OData service files
    /// </summary>
    public ServerProjectGeneratorBuilder WithServicesPath(string path)
    {
        _config.ServicesPath = path;
        return this;
    }

    /// <summary>
    /// Configures whether to generate individual files for each model
    /// </summary>
    public ServerProjectGeneratorBuilder WithIndividualFiles(bool generate = true)
    {
        _config.GenerateIndividualFiles = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate repository classes
    /// </summary>
    public ServerProjectGeneratorBuilder WithRepositories(bool generate = true)
    {
        _config.GenerateRepositories = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate GraphQL schema classes
    /// </summary>
    public ServerProjectGeneratorBuilder WithGraphTypes(bool generate = true)
    {
        _config.GenerateGraphTypes = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate query classes
    /// </summary>
    public ServerProjectGeneratorBuilder WithQueries(bool generate = true)
    {
        _config.GenerateQueries = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate field type classes
    /// </summary>
    public ServerProjectGeneratorBuilder WithFieldTypes(bool generate = true)
    {
        _config.GenerateFieldTypes = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate validation rules
    /// </summary>
    public ServerProjectGeneratorBuilder WithValidationRules(bool generate = true)
    {
        _config.GenerateValidationRules = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate OData services
    /// </summary>
    public ServerProjectGeneratorBuilder WithODataServices(bool generate = true)
    {
        _config.GenerateODataServices = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate partial classes
    /// </summary>
    public ServerProjectGeneratorBuilder WithPartialClasses(bool usePartial = true)
    {
        _config.UsePartialClasses = usePartial;
        return this;
    }

    /// <summary>
    /// Configures whether to clean up output directories before generation
    /// </summary>
    public ServerProjectGeneratorBuilder WithCleanup(bool cleanup = true)
    {
        _config.Cleanup = cleanup;
        return this;
    }

    /// <summary>
    /// Sets the default page size for pagination
    /// </summary>
    public ServerProjectGeneratorBuilder WithDefaultPageSize(int pageSize)
    {
        _config.DefaultPageSize = pageSize;
        return this;
    }

    /// <summary>
    /// Sets the maximum page size for pagination
    /// </summary>
    public ServerProjectGeneratorBuilder WithMaxPageSize(int maxPageSize)
    {
        _config.MaxPageSize = maxPageSize;
        return this;
    }

    /// <summary>
    /// Builds the ServerProjectGenerator with the configured settings
    /// </summary>
    public ServerProjectGenerator Build()
    {
        return new ServerProjectGenerator(_document, _config);
    }
}