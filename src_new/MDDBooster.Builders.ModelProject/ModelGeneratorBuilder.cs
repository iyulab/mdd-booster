namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Builder for creating ModelGenerator instances with fluent configuration
/// </summary>
public class ModelGeneratorBuilder
{
    private readonly MDDDocument _document;
    private readonly ModelProjectConfig _config = new ModelProjectConfig();

    public ModelGeneratorBuilder(MDDDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Sets the root namespace for generated code
    /// </summary>
    public ModelGeneratorBuilder WithNamespace(string ns)
    {
        _config.Namespace = ns;
        return this;
    }

    /// <summary>
    /// Sets the path for model classes
    /// </summary>
    public ModelGeneratorBuilder WithModelsPath(string path)
    {
        _config.ModelsPath = path;
        return this;
    }

    /// <summary>
    /// Sets the path for interfaces
    /// </summary>
    public ModelGeneratorBuilder WithInterfacesPath(string path)
    {
        _config.InterfacesPath = path;
        return this;
    }

    /// <summary>
    /// Sets the path for enums
    /// </summary>
    public ModelGeneratorBuilder WithEnumsPath(string path)
    {
        _config.EnumsPath = path;
        return this;
    }

    /// <summary>
    /// Sets the path for GraphQL search request classes
    /// </summary>
    public ModelGeneratorBuilder WithGqlSearchRequestPath(string path)
    {
        _config.GqlSearchRequestPath = path;
        return this;
    }

    /// <summary>
    /// Configures whether to generate navigation properties
    /// </summary>
    public ModelGeneratorBuilder WithNavigationProperties(bool generate = true)
    {
        _config.GenerateNavigationProperties = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate partial classes
    /// </summary>
    public ModelGeneratorBuilder WithPartialClasses(bool usePartial = true)
    {
        _config.UsePartialClasses = usePartial;
        return this;
    }

    /// <summary>
    /// Configures whether to use nullable reference types
    /// </summary>
    public ModelGeneratorBuilder WithNullableReferenceTypes(bool useNullableRefs = true)
    {
        _config.UseNullableReferenceTypes = useNullableRefs;
        return this;
    }

    /// <summary>
    /// Configures whether to use DateTimeOffset instead of DateTime
    /// </summary>
    public ModelGeneratorBuilder WithDateTimeOffset(bool useDateTimeOffset = true)
    {
        _config.UseDateTimeOffset = useDateTimeOffset;
        return this;
    }

    /// <summary>
    /// Configures whether to implement INotifyPropertyChanged
    /// </summary>
    public ModelGeneratorBuilder WithPropertyChangeNotification(bool implement = true)
    {
        _config.ImplementINotifyPropertyChanged = implement;
        return this;
    }

    /// <summary>
    /// Configures whether to generate GraphQL search request classes
    /// </summary>
    public ModelGeneratorBuilder WithGqlSearchRequests(bool generate = true, string path = "Gql_")
    {
        _config.GenerateGqlSearchRequest = generate;
        if (generate && !string.IsNullOrEmpty(path))
        {
            _config.GqlSearchRequestPath = path;
        }
        return this;
    }

    /// <summary>
    /// Configures whether to generate interface files
    /// </summary>
    public ModelGeneratorBuilder WithInterfaceGeneration(bool generate = true)
    {
        _config.GenerateInterface = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate abstract model classes
    /// </summary>
    public ModelGeneratorBuilder WithAbstractModels(bool generate = true)
    {
        _config.GenerateAbstractModels = generate;
        return this;
    }

    /// <summary>
    /// Configures directory cleanup behavior
    /// </summary>
    public ModelGeneratorBuilder WithCleanup(bool cleanup = true)
    {
        _config.Cleanup = cleanup;
        return this;
    }

    /// <summary>
    /// Sets the default string length for generated fields
    /// </summary>
    public ModelGeneratorBuilder WithDefaultStringLength(int length)
    {
        _config.DefaultStringLength = length;
        return this;
    }

    /// <summary>
    /// Builds the ModelGenerator with the configured settings
    /// </summary>
    public ModelGenerator Build()
    {
        return new ModelGenerator(_document, _config);
    }
}