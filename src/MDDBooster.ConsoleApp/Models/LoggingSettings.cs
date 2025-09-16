namespace MDDBooster.ConsoleApp.Models;

/// <summary>
/// Logging settings
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// Enable verbose logging
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Path to the log file (if empty, logs to console only)
    /// </summary>
    public string LogFilePath { get; set; } = string.Empty;
}