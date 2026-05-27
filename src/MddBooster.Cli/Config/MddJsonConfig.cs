using System.Text.Json.Serialization;

namespace MddBooster.Cli.Config;

public sealed class MddJsonConfig
{
    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("targets")]
    public List<MddJsonTarget> Targets { get; set; } = [];
}

public sealed class MddJsonTarget
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = "";

    // Sql target
    [JsonPropertyName("schema")]
    public string? Schema { get; set; }

    // Model target
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("dbContextName")]
    public string? DbContextName { get; set; }

    /// <summary>
    /// Optional: path to the SSDT SQL project root (relative to mdd.json or absolute).
    /// When set on a Model target, the generator scans <c>dbo/Views/</c> for
    /// <c>{Name}ExtView.sql</c> files to use as the highest-priority read backing.
    /// </summary>
    [JsonPropertyName("sqlProjectPath")]
    public string? SqlProjectPath { get; set; }

    // TypeScript target
    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }
}
