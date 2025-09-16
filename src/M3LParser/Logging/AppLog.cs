using Microsoft.Extensions.Logging;

namespace M3LParser.Logging;

/// <summary>
/// Static access point for logging
/// </summary>
public static class AppLog
{
    private static ILogger _logger = new DebugLogger("M3LParser");

    /// <summary>
    /// Get or set the current logger instance
    /// </summary>
    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? new DebugLogger("M3LParser");
    }

    /// <summary>
    /// Log debug information
    /// </summary>
    public static void Debug(string message, params object[] args) =>
        Logger.LogDebug(message, args);

    /// <summary>
    /// Log information
    /// </summary>
    public static void Information(string message, params object[] args) =>
        Logger.LogInformation(message, args);

    /// <summary>
    /// Log a warning
    /// </summary>
    public static void Warning(string message, params object[] args) =>
        Logger.LogWarning(message, args);

    /// <summary>
    /// Log an error without an exception
    /// </summary>
    public static void Error(string message, params object[] args) =>
        Logger.LogError(message, args);

    /// <summary>
    /// Log an error with an exception
    /// </summary>
    public static void Error(string message, Exception exception, params object[] args) =>
        Logger.LogError(exception, message, args);

    public static void Error(Exception ex, string message, params object[] args) => 
        Logger.LogError(ex, message, args);

    public static void Warning(Exception ex, string message, params object[] args) =>
        Logger.LogWarning(ex, message, args);
}
