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
}
