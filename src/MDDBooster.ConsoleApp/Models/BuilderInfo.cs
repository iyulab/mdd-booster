namespace MDDBooster.ConsoleApp.Models;

/// <summary>
/// Information about a builder to apply
/// </summary>
public class BuilderInfo
{
    /// <summary>
    /// Type of builder
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Builder configuration (dynamic)
    /// </summary>
    public Dictionary<string, JsonElement> Config { get; set; } = new Dictionary<string, JsonElement>();
}
