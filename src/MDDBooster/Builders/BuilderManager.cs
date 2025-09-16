using System.Reflection;
using System.Text.Json;
using M3LParser.Logging;

namespace MDDBooster.Builders;

/// <summary>
/// Manager for discovering and creating builders
/// </summary>
public static class BuilderManager
{
    private static readonly Dictionary<string, Type> _builderTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type> _configTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialize the builder manager by discovering all available builders
    /// </summary>
    public static void Initialize()
    {
        // Clear existing registrations
        _builderTypes.Clear();
        _configTypes.Clear();

        // Get all assemblies in the current domain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            try
            {
                // Skip system assemblies
                if (assembly.IsDynamic || assembly.FullName.StartsWith("System.") || assembly.FullName.StartsWith("Microsoft."))
                    continue;

                // Find all types implementing IBuilder
                var builderTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IBuilder).IsAssignableFrom(t));

                foreach (var builderType in builderTypes)
                {
                    try
                    {
                        // Create an instance to get the builder type
                        var builder = (IBuilder)Activator.CreateInstance(builderType);
                        if (builder != null)
                        {
                            _builderTypes[builder.BuilderType] = builderType;

                            // Create a default config to get the config type
                            var config = builder.CreateDefaultConfig();
                            _configTypes[builder.BuilderType] = config.GetType();

                            AppLog.Debug("Registered builder: {BuilderType} ({BuilderClass})",
                                builder.BuilderType, builderType.FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning(ex, "Failed to initialize builder: {BuilderType}", builderType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(ex, "Error scanning assembly: {Assembly}", assembly.FullName);
            }
        }

        AppLog.Information("Builder manager initialized with {Count} builders", _builderTypes.Count);
    }

    /// <summary>
    /// Get all available builder types
    /// </summary>
    public static IEnumerable<string> GetAvailableBuilderTypes()
    {
        return _builderTypes.Keys;
    }

    /// <summary>
    /// Create a builder instance by type
    /// </summary>
    public static IBuilder CreateBuilder(string builderType)
    {
        if (_builderTypes.TryGetValue(builderType, out var type))
        {
            try
            {
                return (IBuilder)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "Failed to create builder instance: {BuilderType}", builderType);
            }
        }
        else
        {
            AppLog.Warning("Builder type not found: {BuilderType}", builderType);
        }

        return null;
    }

    /// <summary>
    /// Create a config instance for the specified builder type
    /// </summary>
    public static IBuilderConfig CreateConfig(string builderType)
    {
        var builder = CreateBuilder(builderType);
        return builder?.CreateDefaultConfig();
    }

    /// <summary>
    /// Convert a generic config JSON to a typed builder config
    /// </summary>
    public static IBuilderConfig ConvertConfig(string builderType, JsonElement jsonElement)
    {
        if (!_configTypes.TryGetValue(builderType, out var configType))
        {
            AppLog.Warning("Config type not found for builder: {BuilderType}", builderType);
            return null;
        }

        try
        {
            // Create default config
            var config = CreateConfig(builderType);
            if (config == null)
                return null;

            // Copy properties from JSON to config
            foreach (var property in configType.GetProperties())
            {
                if (jsonElement.TryGetProperty(char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1), out var element))
                {
                    try
                    {
                        object value = GetValueFromJsonElement(element, property.PropertyType);
                        property.SetValue(config, value);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning(ex, "Failed to set property {Property} on config {ConfigType}",
                            property.Name, configType.Name);
                    }
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to convert config for builder: {BuilderType}", builderType);
            return null;
        }
    }

    private static object GetValueFromJsonElement(JsonElement element, Type targetType)
    {
        if (targetType == typeof(string))
        {
            return element.GetString() ?? string.Empty;
        }
        else if (targetType == typeof(int))
        {
            return element.GetInt32();
        }
        else if (targetType == typeof(bool))
        {
            return element.GetBoolean();
        }
        else if (targetType == typeof(double))
        {
            return element.GetDouble();
        }
        else if (targetType == typeof(DateTime))
        {
            return element.GetDateTime();
        }
        else if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, element.GetString() ?? string.Empty);
        }
        else
        {
            return JsonSerializer.Deserialize(element.GetRawText(), targetType) ??
                throw new InvalidOperationException($"Failed to deserialize to {targetType.Name}");
        }
    }
}