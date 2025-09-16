namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Builder for creating ModelGenerator instances with fluent configuration
/// Uses centralized helpers for consistency
/// </summary>
public class ModelGeneratorBuilder
{
    private readonly MDDDocument _document;
    private readonly ModelProjectConfig _config = new ModelProjectConfig();

    public ModelGeneratorBuilder(MDDDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// Sets the root namespace for generated code
    /// </summary>
    public ModelGeneratorBuilder WithNamespace(string ns)
    {
        _config.Namespace = ns ?? throw new ArgumentNullException(nameof(ns));
        return this;
    }

    /// <summary>
    /// Sets the project path
    /// </summary>
    public ModelGeneratorBuilder WithProjectPath(string path)
    {
        _config.ProjectPath = path ?? throw new ArgumentNullException(nameof(path));
        return this;
    }

    /// <summary>
    /// Sets the path for model classes
    /// </summary>
    public ModelGeneratorBuilder WithModelsPath(string path)
    {
        _config.ModelsPath = path ?? throw new ArgumentNullException(nameof(path));
        return this;
    }

    /// <summary>
    /// Sets the path for interfaces
    /// </summary>
    public ModelGeneratorBuilder WithInterfacesPath(string path)
    {
        _config.InterfacesPath = path ?? throw new ArgumentNullException(nameof(path));
        return this;
    }

    /// <summary>
    /// Sets the path for enums
    /// </summary>
    public ModelGeneratorBuilder WithEnumsPath(string path)
    {
        _config.EnumsPath = path ?? throw new ArgumentNullException(nameof(path));
        return this;
    }

    /// <summary>
    /// Sets the path for GraphQL search request classes
    /// </summary>
    public ModelGeneratorBuilder WithGqlSearchRequestPath(string path)
    {
        _config.GqlSearchRequestPath = path ?? throw new ArgumentNullException(nameof(path));
        return this;
    }

    /// <summary>
    /// Configures whether to generate navigation properties
    /// </summary>
    public ModelGeneratorBuilder WithNavigationProperties(bool generate = true)
    {
        _config.GenerateNavigationProperties = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate partial classes
    /// </summary>
    public ModelGeneratorBuilder WithPartialClasses(bool usePartial = true)
    {
        _config.UsePartialClasses = usePartial;
        return this;
    }

    /// <summary>
    /// Configures whether to use nullable reference types
    /// </summary>
    public ModelGeneratorBuilder WithNullableReferenceTypes(bool useNullableRefs = true)
    {
        _config.UseNullableReferenceTypes = useNullableRefs;
        return this;
    }

    /// <summary>
    /// Configures whether to use DateTimeOffset instead of DateTime
    /// </summary>
    public ModelGeneratorBuilder WithDateTimeOffset(bool useDateTimeOffset = true)
    {
        _config.UseDateTimeOffset = useDateTimeOffset;
        return this;
    }

    /// <summary>
    /// Configures whether to implement INotifyPropertyChanged
    /// </summary>
    public ModelGeneratorBuilder WithPropertyChangeNotification(bool implement = true)
    {
        _config.ImplementINotifyPropertyChanged = implement;
        return this;
    }

    /// <summary>
    /// Configures whether to generate GraphQL search request classes
    /// </summary>
    public ModelGeneratorBuilder WithGqlSearchRequests(bool generate = true, string path = "Gql_")
    {
        _config.GenerateGqlSearchRequest = generate;
        if (generate && !string.IsNullOrEmpty(path))
        {
            _config.GqlSearchRequestPath = path;
        }
        return this;
    }

    /// <summary>
    /// Configures whether to generate interface files
    /// </summary>
    public ModelGeneratorBuilder WithInterfaceGeneration(bool generate = true)
    {
        _config.GenerateInterface = generate;
        return this;
    }

    /// <summary>
    /// Configures whether to generate abstract model classes
    /// </summary>
    public ModelGeneratorBuilder WithAbstractModels(bool generate = true)
    {
        _config.GenerateAbstractModels = generate;
        return this;
    }

    /// <summary>
    /// Configures directory cleanup behavior
    /// </summary>
    public ModelGeneratorBuilder WithCleanup(bool cleanup = true)
    {
        _config.Cleanup = cleanup;
        return this;
    }

    /// <summary>
    /// Sets the default string length for generated fields
    /// </summary>
    public ModelGeneratorBuilder WithDefaultStringLength(int length)
    {
        if (length <= 0)
            throw new ArgumentException("String length must be positive", nameof(length));

        _config.DefaultStringLength = length;
        return this;
    }

    /// <summary>
    /// Configures enum field generation behavior
    /// </summary>
    public ModelGeneratorBuilder WithEnumGeneration(
        bool generateAsString = true,
        string enumPropertySuffix = "Enum",
        bool addNotMapped = true,
        bool addIgnore = true,
        bool makeVirtual = true)
    {
        _config.GenerateEnumAsString = generateAsString;
        _config.EnumPropertySuffix = enumPropertySuffix ?? "Enum";
        _config.AddNotMappedToEnumProperties = addNotMapped;
        _config.AddIgnoreToEnumProperties = addIgnore;
        _config.MakeEnumPropertiesVirtual = makeVirtual;
        return this;
    }

    /// <summary>
    /// Configures enum fields to be generated as string properties with enum accessor properties
    /// </summary>
    public ModelGeneratorBuilder WithStringBasedEnums(string enumPropertySuffix = "Enum")
    {
        return WithEnumGeneration(true, enumPropertySuffix, true, true, true);
    }

    /// <summary>
    /// Configures enum fields to be generated as direct enum type properties (legacy behavior)
    /// </summary>
    public ModelGeneratorBuilder WithDirectEnumTypes()
    {
        return WithEnumGeneration(false, "Enum", false, false, false);
    }

    /// <summary>
    /// Adds a custom field-to-enum type mapping
    /// </summary>
    public ModelGeneratorBuilder WithFieldToEnumMapping(string fieldNamePattern, string enumTypeName)
    {
        _config.AddFieldToEnumMapping(fieldNamePattern, enumTypeName);
        return this;
    }

    /// <summary>
    /// Adds multiple field-to-enum type mappings
    /// </summary>
    public ModelGeneratorBuilder WithFieldToEnumMappings(Dictionary<string, string> mappings)
    {
        if (mappings != null)
        {
            foreach (var mapping in mappings)
            {
                _config.AddFieldToEnumMapping(mapping.Key, mapping.Value);
            }
        }
        return this;
    }

    /// <summary>
    /// Configures enum resolution warning logging
    /// </summary>
    public ModelGeneratorBuilder WithEnumResolutionWarnings(bool logWarnings = true)
    {
        _config.LogEnumResolutionWarnings = logWarnings;
        return this;
    }

    /// <summary>
    /// Adds custom navigation property pattern
    /// </summary>
    public ModelGeneratorBuilder WithCustomNavigationPattern(string fieldPattern, string propertyName)
    {
        NavigationPropertyHelper.AddCustomFieldPattern(fieldPattern, propertyName);
        return this;
    }

    /// <summary>
    /// Adds custom data type pattern
    /// </summary>
    public ModelGeneratorBuilder WithCustomDataTypePattern(string fieldPattern, string dataType)
    {
        AttributeGenerationHelper.AddDataTypePattern(fieldPattern, dataType);
        return this;
    }

    /// <summary>
    /// Adds custom namespace mapping
    /// </summary>
    public ModelGeneratorBuilder WithCustomNamespaceMapping(string path, string namespaceSegment)
    {
        MDDBooster.Helpers.NamespaceHelper.AddCustomNamespaceMapping(path, namespaceSegment);
        return this;
    }

    /// <summary>
    /// Adds custom type mapping
    /// </summary>
    public ModelGeneratorBuilder WithCustomTypeMapping(string m3lType, string csharpType)
    {
        TypeConversionHelper.AddCustomCSharpTypeMapping(m3lType, csharpType);
        return this;
    }

    /// <summary>
    /// Adds custom SQL type mapping
    /// </summary>
    public ModelGeneratorBuilder WithCustomSqlTypeMapping(string m3lType, string sqlType)
    {
        TypeConversionHelper.AddCustomSqlTypeMapping(m3lType, sqlType);
        return this;
    }

    /// <summary>
    /// Configures type conversion settings
    /// </summary>
    public ModelGeneratorBuilder WithTypeConversionSettings(bool useDateTimeOffset = false, int defaultStringLength = 50, string defaultDecimalPrecision = "18,2")
    {
        TypeConversionHelper.Config.UseDateTimeOffset = useDateTimeOffset;
        TypeConversionHelper.Config.DefaultStringLength = defaultStringLength;
        TypeConversionHelper.Config.DefaultDecimalPrecision = defaultDecimalPrecision;
        return this;
    }

    /// <summary>
    /// Configures attribute generation settings
    /// </summary>
    public ModelGeneratorBuilder WithAttributeSettings(bool autoGenerateJsonIgnore = true, int defaultStringLength = 50)
    {
        AttributeGenerationHelper.Config.AutoGenerateJsonIgnoreForSensitive = autoGenerateJsonIgnore;
        AttributeGenerationHelper.Config.DefaultStringLength = defaultStringLength;
        return this;
    }

    /// <summary>
    /// Validates the current configuration
    /// </summary>
    public ModelGeneratorBuilder ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_config.Namespace))
            throw new InvalidOperationException("Namespace must be specified");

        if (string.IsNullOrEmpty(_config.ProjectPath))
            throw new InvalidOperationException("Project path must be specified");

        if (_config.DefaultStringLength <= 0)
            throw new InvalidOperationException("Default string length must be positive");

        if (string.IsNullOrEmpty(_config.EnumPropertySuffix))
            throw new InvalidOperationException("Enum property suffix cannot be empty");

        return this;
    }

    /// <summary>
    /// Builds the ModelGenerator with the configured settings
    /// </summary>
    public ModelGenerator Build()
    {
        ValidateConfiguration();

        // Configure TypeConversionHelper with enum settings
        TypeConversionHelper.Config.GenerateEnumAsString = _config.GenerateEnumAsString;

        return new ModelGenerator(_document, _config);
    }

    /// <summary>
    /// Gets a copy of the current configuration
    /// </summary>
    public ModelProjectConfig GetConfiguration()
    {
        return new ModelProjectConfig
        {
            ProjectPath = _config.ProjectPath,
            Namespace = _config.Namespace,
            ModelsPath = _config.ModelsPath,
            InterfacesPath = _config.InterfacesPath,
            EnumsPath = _config.EnumsPath,
            GqlSearchRequestPath = _config.GqlSearchRequestPath,
            GenerateNavigationProperties = _config.GenerateNavigationProperties,
            GenerateInterface = _config.GenerateInterface,
            GenerateGqlSearchRequest = _config.GenerateGqlSearchRequest,
            GenerateAbstractModels = _config.GenerateAbstractModels,
            UsePartialClasses = _config.UsePartialClasses,
            ImplementINotifyPropertyChanged = _config.ImplementINotifyPropertyChanged,
            UseDateTimeOffset = _config.UseDateTimeOffset,
            UseNullableReferenceTypes = _config.UseNullableReferenceTypes,
            DefaultStringLength = _config.DefaultStringLength,
            Cleanup = _config.Cleanup,
            GenerateEnumAsString = _config.GenerateEnumAsString,
            EnumPropertySuffix = _config.EnumPropertySuffix,
            AddNotMappedToEnumProperties = _config.AddNotMappedToEnumProperties,
            AddIgnoreToEnumProperties = _config.AddIgnoreToEnumProperties,
            MakeEnumPropertiesVirtual = _config.MakeEnumPropertiesVirtual
        };
    }
}