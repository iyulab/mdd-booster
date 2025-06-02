namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Generator for C# model classes - Main class
/// </summary>
public partial class ModelGenerator
{
    public MDDDocument Document { get; }
    private readonly ModelProjectConfig _config;
    private readonly Dictionary<string, List<RelationInfo>> _modelRelations;
    private readonly GqlSearchRequestGenerator _gqlSearchRequestGenerator;

    internal class RelationInfo
    {
        public string SourceModel { get; set; }
        public string TargetModel { get; set; }
        public string PropertyName { get; set; }
        public bool IsToMany { get; set; }
        public string NavigationField { get; set; }
        public bool IsForeignKey { get; set; }
    }

    public ModelGenerator(MDDDocument document, ModelProjectConfig config)
    {
        Document = document;
        _config = config;

        // Configure helpers with current settings
        ConfigureHelpers();

        _gqlSearchRequestGenerator = new GqlSearchRequestGenerator(config);
        _modelRelations = BuildRelationshipMap();
    }

    /// <summary>
    /// Generate GraphQL search request classes for a model
    /// </summary>
    public string GenerateGqlSearchRequestClasses(MDDModel model)
    {
        return ErrorHandling.ExecuteSafely(() =>
        {
            return _gqlSearchRequestGenerator.GenerateSearchRequestClasses(Document, model);
        },
        string.Empty,
        "Failed to generate GraphQL search request classes for {ModelName}",
        model.BaseModel.Name);
    }

    private void ConfigureHelpers()
    {
        // Configure TypeConversionHelper for model projects
        TypeConversionHelper.Config.UseDateTimeOffset = _config.UseDateTimeOffset;
        TypeConversionHelper.Config.DefaultStringLength = _config.DefaultStringLength;

        // Configure AttributeGenerationHelper for model projects
        AttributeGenerationHelper.Config.DefaultStringLength = _config.DefaultStringLength;
        AttributeGenerationHelper.Config.AutoGenerateJsonIgnoreForSensitive = true;

        // Initialize common patterns
        NavigationPropertyHelper.InitializeCommonPatterns();
        AttributeGenerationHelper.InitializeCommonPatterns();
    }

    // Namespace methods using the base helper
    private string GetModelNamespace()
    {
        return MDDBooster.Helpers.NamespaceHelper.GetNamespace(
            _config.Namespace,
            _config.ModelsPath,
            Document.BaseDocument.Namespace);
    }

    private string GetInterfaceNamespace()
    {
        return MDDBooster.Helpers.NamespaceHelper.GetNamespace(
            _config.Namespace,
            _config.InterfacesPath,
            Document.BaseDocument.Namespace);
    }

    private string GetEnumNamespace()
    {
        return MDDBooster.Helpers.NamespaceHelper.GetNamespace(
            _config.Namespace,
            _config.EnumsPath,
            Document.BaseDocument.Namespace);
    }
}