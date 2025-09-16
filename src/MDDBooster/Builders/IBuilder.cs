using MDDBooster.Models;

namespace MDDBooster.Builders;

/// <summary>
/// Interface for all builders that can process MDD documents
/// </summary>
public interface IBuilder
{
    /// <summary>
    /// Builder type identifier
    /// </summary>
    string BuilderType { get; }

    /// <summary>
    /// Create a builder config instance specific to this builder
    /// </summary>
    IBuilderConfig CreateDefaultConfig();

    /// <summary>
    /// Process an MDD document with the provided configuration
    /// </summary>
    bool Process(MDDDocument document, IBuilderConfig config);
}

/// <summary>
/// Base interface for all builder configurations
/// </summary>
public interface IBuilderConfig
{
    /// <summary>
    /// Path to the project directory
    /// </summary>
    string ProjectPath { get; set; }

    /// <summary>
    /// Get the full output path for this builder
    /// </summary>
    string GetFullOutputPath();
}