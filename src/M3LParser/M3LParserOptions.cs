namespace M3LParser;

/// <summary>
/// Configuration options for the M3L parser
/// </summary>
public class M3LParserOptions
{
    /// <summary>
    /// Function to pre-process content before parsing
    /// </summary>
    public Func<string, string> PreProcessContent { get; set; }

    /// <summary>
    /// Function to post-process the document after parsing
    /// </summary>
    public Action<M3LDocument> PostProcessDocument { get; set; }

    /// <summary>
    /// Enable or disable strict mode for parsing
    /// In strict mode, parsing errors will throw exceptions
    /// </summary>
    public bool StrictMode { get; set; } = false;

    /// <summary>
    /// Enable or disable debug logging
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Enable or disable automatic normalization of names
    /// </summary>
    public bool NormalizeNames { get; set; } = false; // Changed from true to false to prevent name normalization

    /// <summary>
    /// Enable or disable inheritance resolution
    /// </summary>
    public bool ResolveInheritance { get; set; } = false; // Changed from true to false to let MDDBooster handle inheritance
}