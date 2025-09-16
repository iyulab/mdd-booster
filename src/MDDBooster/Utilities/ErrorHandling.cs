namespace MDDBooster.Utilities;

/// <summary>
/// Utilities for consistent error handling
/// </summary>
public static class ErrorHandling
{
    /// <summary>
    /// Executes a function safely, catching any exceptions and returning a default value
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    public static T ExecuteSafely<T>(Func<T> action, T defaultValue, string errorMessage, params object[] args)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, errorMessage, args);
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes an action safely, catching any exceptions
    /// </summary>
    public static void ExecuteSafely(Action action, string errorMessage, params object[] args)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, errorMessage, args);
        }
    }

    /// <summary>
    /// Executes an action safely, catching specific exceptions
    /// </summary>
    /// <typeparam name="TException">Type of exception to catch</typeparam>
    public static void ExecuteSafely<TException>(Action action, string errorMessage, params object[] args)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            AppLog.Error(ex, errorMessage, args);
        }
    }
}