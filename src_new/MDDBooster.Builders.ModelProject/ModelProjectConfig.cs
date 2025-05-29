namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Configuration for the ModelProject builder
/// </summary>
public class ModelProjectConfig : IBuilderConfig
{
    public string ProjectPath { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string ModelsPath { get; set; } = "Entity";
    public string InterfacesPath { get; set; } = "Models";
    public string EnumsPath { get; set; } = "Models";
    public string GqlSearchRequestPath { get; set; } = "Gql_";
    public bool GenerateNavigationProperties { get; set; } = true;
    public bool GenerateInterface { get; set; } = true;
    public bool GenerateGqlSearchRequest { get; set; } = false;
    public bool GenerateAbstractModels { get; set; } = true;
    public bool UsePartialClasses { get; set; } = true;
    public bool ImplementINotifyPropertyChanged { get; set; } = false;
    public bool UseDateTimeOffset { get; set; } = false;
    public bool UseNullableReferenceTypes { get; set; } = true;
    public int DefaultStringLength { get; set; } = 50;
    public bool Cleanup { get; set; } = true;

    public string GetFullOutputPath() => ProjectPath;
}