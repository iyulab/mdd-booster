using System.Text.Json;

namespace MDDBooster.ConsoleApp;

internal class Program
{
#if DEBUG
    //private const string DefaultSettingsPath = @"D:\data\ironhive-appservice\mdd\settings.json";
    //private const string DefaultSettingsPath = @"D:\data\yesung\yesung-oms\mdd\settings.json";
    private const string DefaultSettingsPath = @"D:\data\Plands\mdd\settings.json";
#endif

    static async Task<int> Main(string[] args)
    {
#if DEBUG
        args = new string[] { "--settings", DefaultSettingsPath };
#endif

        // Pre-process arguments to handle positional settings file argument
        // If first argument doesn't start with '--' and looks like a settings file, treat it as --settings
        if (args.Length > 0 && !args[0].StartsWith("--") &&
            (args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) || args[0].Contains("settings")))
        {
            // Convert positional argument to --settings flag
            var newArgs = new List<string> { "--settings", args[0] };
            newArgs.AddRange(args.Skip(1));
            args = newArgs.ToArray();
        }

        // Create root command
        var rootCommand = new RootCommand("MDDBooster Console Application - M3L Parser and Code Generator with SQL Server Cascade Path Validation");

        // Add settings file option (legacy support)
        var settingsOption = new Option<string>(
            name: "--settings",
            description: "Path to the settings JSON file");

        // Add direct command options for easier usage
        var inputOption = new Option<string>(
            name: "--input",
            description: "Path to the M3L input file");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Path to the output directory");

        var builderOption = new Option<string>(
            name: "--builder",
            description: "Builder type (DatabaseProject, ModelProject, ServerProject)",
            getDefaultValue: () => "DatabaseProject");

        rootCommand.AddOption(settingsOption);
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(builderOption);

        // Set handler for main command - support both settings file and direct parameters
        rootCommand.SetHandler((settingsPath, inputPath, outputPath, builderType) =>
        {
            try
            {
                // Display current directory for debugging
                Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");

                // Ensure all assemblies are loaded to find builders
                LoadAllAssemblies();

                // Initialize BuilderManager to discover all available builders
                BuilderManager.Initialize();

                // Log available builders for debugging
                var availableBuilders = BuilderManager.GetAvailableBuilderTypes();
                Console.WriteLine($"Available builders: {string.Join(", ", availableBuilders)}");

                // Determine which mode to use
                if (!string.IsNullOrEmpty(settingsPath))
                {
                    // Settings file mode (explicit path provided)
                    ProcessWithSettingsFile(settingsPath);
                }
                else if (!string.IsNullOrEmpty(inputPath) && !string.IsNullOrEmpty(outputPath))
                {
                    // Direct parameters mode
                    ProcessWithDirectParameters(inputPath, outputPath, builderType);
                }
                else
                {
                    // No explicit settings provided - try default settings.json in current directory
                    string defaultSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");

                    if (File.Exists(defaultSettingsPath))
                    {
                        Console.WriteLine($"Using default settings file: {defaultSettingsPath}");
                        ProcessWithSettingsFile(defaultSettingsPath);
                    }
                    else
                    {
#if DEBUG
                        // Debug mode - use debug default settings
                        ProcessWithSettingsFile(DefaultSettingsPath);
#else
                        Console.WriteLine("Error: Either --settings or both --input and --output must be specified");
                        Console.WriteLine($"Note: No default settings.json found in current directory: {Directory.GetCurrentDirectory()}");
                        Console.WriteLine("Usage:");
                        Console.WriteLine("  mdd --settings <path-to-settings.json>");
                        Console.WriteLine("  mdd --input <path-to-m3l-file> --output <output-directory> [--builder <builder-type>]");
                        Console.WriteLine("  mdd   (uses ./settings.json if it exists)");
                        return;
#endif
                    }
                }

                Console.WriteLine("Processing completed successfully.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Console.ResetColor();
                Log.Error(ex, "Unhandled exception");
            }
        }, settingsOption, inputOption, outputOption, builderOption);

        // Execute root command
        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Loads all assemblies in the application directory to ensure all builders are discovered
    /// </summary>
    private static void LoadAllAssemblies()
    {
        try
        {
            // Get the current application directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Console.WriteLine($"Scanning for assemblies in: {baseDir}");

            // Find all DLL files in the directory
            var dllFiles = Directory.GetFiles(baseDir, "*.dll");
            Console.WriteLine($"Found {dllFiles.Length} assemblies");

            // Load each assembly
            foreach (var dllPath in dllFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(dllPath);
                    // Only load MDDBooster related assemblies
                    if (fileName.StartsWith("MDDBooster"))
                    {
                        Console.WriteLine($"Loading assembly: {fileName}");
                        Assembly.LoadFrom(dllPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading assembly {dllPath}: {ex.Message}");
                }
            }

            // Verify critical builders exist in loaded assemblies
            var requiredBuilders = new[] { "MsSqlBuilder", "ModelProjectBuilder", "ServerProjectBuilder" };
            foreach (var builderName in requiredBuilders)
            {
                bool builderFound = false;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetTypes().Any(t => t.Name == builderName))
                    {
                        Console.WriteLine($"Found {builderName} in assembly: {assembly.FullName}");
                        builderFound = true;
                        break;
                    }
                }

                if (!builderFound)
                {
                    Console.WriteLine($"WARNING: {builderName} type not found in any loaded assembly!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading assemblies: {ex.Message}");
        }
    }

    private static void DisplayVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"MDDBooster version {version}");
    }

    private static void InitializeLogging(LoggingSettings loggingSettings)
    {
        // Set minimum log level based on verbose flag
        var minLevel = loggingSettings.Verbose ?
            Serilog.Events.LogEventLevel.Debug :
            Serilog.Events.LogEventLevel.Information;

        // Configure Serilog with console output
        var serilogConfig = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
            );

        // Add file logging if specified
        if (!string.IsNullOrEmpty(loggingSettings.LogFilePath))
        {
            // Ensure log directory exists
            string? logDir = Path.GetDirectoryName(loggingSettings.LogFilePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            // Add file sink
            serilogConfig.WriteTo.File(
                loggingSettings.LogFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day
            );
        }

        // Create Serilog logger
        var serilogLogger = serilogConfig.CreateLogger();

        // Create Microsoft.Extensions.Logging factory with Serilog provider
        var serviceCollection = new ServiceCollection();
        var loggerFactory = serviceCollection
            .AddLogging(builder => builder.AddSerilog(serilogLogger, dispose: true))
            .BuildServiceProvider()
            .GetRequiredService<ILoggerFactory>();

        // Initialize the logging system
        MDDBooster.Logging.LoggingManager.Initialize(loggerFactory);

        // Create a logger for the Program class
        var logger = loggerFactory.CreateLogger<Program>();

        // Log startup info
        logger.LogInformation("MDDBooster starting up");
        logger.LogDebug("Verbose logging enabled: {Verbose}", loggingSettings.Verbose);

        if (!string.IsNullOrEmpty(loggingSettings.LogFilePath))
        {
            logger.LogInformation("Logging to file: {LogFilePath}", loggingSettings.LogFilePath);
        }
    }

    private static void ProcessWithSettingsFile(string settingsPath)
    {
        // Load application settings from the JSON file
        var settings = Settings.Load(settingsPath);

        // Initialize logging based on settings
        InitializeLogging(settings.Logging);

        // Log available builders
        Log.Information("Available builders: {Builders}",
            string.Join(", ", BuilderManager.GetAvailableBuilderTypes()));

        // Process each MDD file configuration
        foreach (var mddConfig in settings.MddConfigs)
        {
            ProcessMddConfig(mddConfig);
        }
    }

    private static void ProcessWithDirectParameters(string inputPath, string outputPath, string builderType)
    {
        // Validate input file
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        // Resolve paths to absolute paths
        var resolvedInputPath = Path.GetFullPath(inputPath);
        var resolvedOutputPath = Path.GetFullPath(outputPath);

        Console.WriteLine($"Resolved MDD path: {resolvedInputPath}");
        Console.WriteLine($"Resolved project path: {resolvedOutputPath}");

        // Initialize simple logging for direct mode
        InitializeLogging(new LoggingSettings { Verbose = false });

        // Parse the MDD file
        var document = ParseMddFile(resolvedInputPath);
        if (document == null)
        {
            throw new InvalidOperationException("Failed to parse MDD file");
        }

        // Create builder configuration as JsonElement
        var configObject = CreateDefaultBuilderConfig(builderType, resolvedOutputPath);
        var configJson = JsonSerializer.SerializeToElement(configObject);

        var builderInfo = new BuilderInfo
        {
            Type = builderType,
            Config = new Dictionary<string, JsonElement>()
        };

        // Convert the JsonElement to Dictionary<string, JsonElement>
        foreach (var property in configJson.EnumerateObject())
        {
            builderInfo.Config[property.Name] = property.Value;
        }

        // Apply the builder
        ApplyBuilder(document, builderInfo);
    }

    private static object CreateDefaultBuilderConfig(string builderType, string outputPath)
    {
        return builderType switch
        {
            "DatabaseProject" => new
            {
                ProjectPath = outputPath,
                TablePath = "dbo/Tables_",
                GenerateIndividualFiles = true,
                SchemaOnly = true,
                UseCreateIfNotExists = true,
                IncludeIndexes = true,
                ClearOutputDirectoryBeforeGeneration = true,
                GenerateTriggers = false,
                GenerateForeignKeys = true,
                CascadeDelete = true,
                SchemaName = "dbo",
                UseSchemaNamespace = false
            },
            "ModelProject" => new
            {
                ProjectPath = outputPath,
                GenerateRepositories = false,
                GenerateServices = false
            },
            "ServerProject" => new
            {
                ProjectPath = outputPath,
                GenerateControllers = true,
                GenerateServices = true
            },
            _ => throw new ArgumentException($"Unknown builder type: {builderType}")
        };
    }

    private static void ProcessMddConfig(MddConfig mddConfig)
    {
        // Validate MDD file path
        if (string.IsNullOrEmpty(mddConfig.MddPath))
        {
            Console.WriteLine("Warning: Empty MDD file path in configuration. Skipping.");
            return;
        }

        if (!File.Exists(mddConfig.MddPath))
        {
            Console.WriteLine($"Warning: MDD file not found: {mddConfig.MddPath}. Skipping.");
            return;
        }

        Log.Information("Processing MDD file: {FilePath}", mddConfig.MddPath);

        try
        {
            // Parse the MDD file
            var document = ParseMddFile(mddConfig.MddPath);
            if (document == null)
                return;

            // Apply each configured builder
            foreach (var builderInfo in mddConfig.Builders)
            {
                ApplyBuilder(document, builderInfo);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing MDD file: {FilePath}", mddConfig.MddPath);
            Console.WriteLine($"Error processing MDD file: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }

    private static MDDDocument? ParseMddFile(string filePath)
    {
        Log.Information("Parsing MDD file: {FilePath}", filePath);

        try
        {
            // Create parser with all available builder options
            var options = new MDDParserOptions()
                .UseMsSqlBuilder()
                .UseModelProjectBuilder()
                .UseServerProjectBuilder();

            var parser = new MDDBoosterParser(options);

            // Parse the file
            Log.Debug("Parsing MDD file with MDDBoosterParser");
            var document = parser.Parse(filePath);

            // Display summary of the parsed document
            Log.Information("Successfully parsed file with namespace: {Namespace}", document.BaseDocument.Namespace);
            Console.WriteLine($"Namespace: {document.BaseDocument.Namespace}");
            Console.WriteLine($"Models: {document.Models.Count}");
            Console.WriteLine($"Interfaces: {document.Interfaces.Count}");
            Console.WriteLine($"Enums: {document.Enums.Count}");

            return document;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error parsing file: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.ResetColor();
            Log.Error(ex, "Error parsing file: {FilePath}", filePath);
            return null;
        }
    }

    private static void ApplyBuilder(MDDDocument document, BuilderInfo builderInfo)
    {
        Log.Information("Applying builder: {BuilderType}", builderInfo.Type);

        // Create builder instance
        var builder = BuilderManager.CreateBuilder(builderInfo.Type);
        if (builder == null)
        {
            Log.Error("Builder not found: {BuilderType}", builderInfo.Type);
            Console.WriteLine($"ERROR: Builder type '{builderInfo.Type}' not found. Available types: {string.Join(", ", BuilderManager.GetAvailableBuilderTypes())}");
            return;
        }

        try
        {
            // Convert builder config from JSON to the builder-specific config type
            var jsonElement = JsonSerializer.SerializeToElement(builderInfo.Config);
            var config = BuilderManager.ConvertConfig(builderInfo.Type, jsonElement);

            if (config == null)
            {
                Log.Error("Failed to create config for builder: {BuilderType}", builderInfo.Type);
                return;
            }

            // Ensure the project path exists
            if (config is IBuilderConfig builderConfig)
            {
                string projectPath = builderConfig.ProjectPath;
                if (!string.IsNullOrEmpty(projectPath) && !Directory.Exists(projectPath))
                {
                    Log.Warning("Project directory does not exist: {ProjectPath}. Attempting to create it.", projectPath);
                    try
                    {
                        Directory.CreateDirectory(projectPath);
                        Log.Information("Created project directory: {ProjectPath}", projectPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to create project directory: {ProjectPath}", projectPath);
                        Console.WriteLine($"ERROR: Failed to create project directory: {projectPath}. {ex.Message}");
                        return;
                    }
                }
            }

            // Process the document with the builder
            bool success = builder.Process(document, config);

            if (success)
            {
                Log.Information("Builder {BuilderType} completed successfully", builderInfo.Type);
            }
            else
            {
                Log.Error("Builder {BuilderType} failed to process document", builderInfo.Type);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying builder {BuilderType}", builderInfo.Type);
            Console.WriteLine($"Error applying builder {builderInfo.Type}: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }
}