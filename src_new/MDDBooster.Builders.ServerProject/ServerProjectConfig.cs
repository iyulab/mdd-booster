namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// Configuration for the ServerProject builder
/// </summary>
public class ServerProjectConfig : IBuilderConfig
{
    /// <summary>
    /// Path to the project directory
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Root namespace for the generated code
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Relative path for GraphQL files (from project path)
    /// </summary>
    public string GqlPath { get; set; } = "Gql_";

    /// <summary>
    /// Relative path for OData service files (from project path)
    /// </summary>
    public string ServicesPath { get; set; } = "Services_";

    /// <summary>
    /// Generate individual files for each model
    /// </summary>
    public bool GenerateIndividualFiles { get; set; } = true;

    /// <summary>
    /// Clean up output directories before generating files
    /// </summary>
    public bool Cleanup { get; set; } = true;

    /// <summary>
    /// Generate repository pattern classes
    /// </summary>
    public bool GenerateRepositories { get; set; } = true;

    /// <summary>
    /// Generate GraphQL schema classes
    /// </summary>
    public bool GenerateGraphTypes { get; set; } = true;

    /// <summary>
    /// Generate query classes
    /// </summary>
    public bool GenerateQueries { get; set; } = true;

    /// <summary>
    /// Generate field type classes
    /// </summary>
    public bool GenerateFieldTypes { get; set; } = true;

    /// <summary>
    /// Generate validation rules
    /// </summary>
    public bool GenerateValidationRules { get; set; } = true;

    /// <summary>
    /// Generate OData services (DataContext, EntitySetBuilder)
    /// </summary>
    public bool GenerateODataServices { get; set; } = true;

    /// <summary>
    /// Use partial classes
    /// </summary>
    public bool UsePartialClasses { get; set; } = true;

    /// <summary>
    /// Default page size for pagination
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Maximum page size for pagination
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Get the full output path for this builder
    /// </summary>
    public string GetFullOutputPath()
    {
        if (string.IsNullOrEmpty(ProjectPath))
            return string.Empty;

        if (string.IsNullOrEmpty(GqlPath))
            return ProjectPath;

        return Path.Combine(ProjectPath, GqlPath);
    }
}