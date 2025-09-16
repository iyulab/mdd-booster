namespace MDDBooster.ConsoleApp.Models;

/// <summary>
/// Configuration for an MDD file to process
/// </summary>
public class MddConfig
{
    /// <summary>
    /// Path to the MDD file
    /// </summary>
    public string MddPath { get; set; } = string.Empty;

    /// <summary>
    /// List of builders to apply to this MDD file
    /// </summary>
    public List<BuilderInfo> Builders { get; set; } = new List<BuilderInfo>();
}
