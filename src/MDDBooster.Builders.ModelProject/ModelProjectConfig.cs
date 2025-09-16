namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// Configuration for the ModelProject builder
/// </summary>
public class ModelProjectConfig : IBuilderConfig
{
    #region Basic Configuration

    /// <summary>
    /// Path to the project directory where files will be generated
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Root namespace for generated code
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Path for model classes (relative to ProjectPath)
    /// </summary>
    public string ModelsPath { get; set; } = "Entity_";

    /// <summary>
    /// Path for interface files (relative to ProjectPath)
    /// </summary>
    public string InterfacesPath { get; set; } = "Models_";

    /// <summary>
    /// Path for enum files (relative to ProjectPath)
    /// </summary>
    public string EnumsPath { get; set; } = "Models_";

    /// <summary>
    /// Path for GraphQL search request classes (relative to ProjectPath)
    /// </summary>
    public string GqlSearchRequestPath { get; set; } = "Gql_";

    #endregion

    #region Generation Features

    /// <summary>
    /// Whether to generate navigation properties for relationships
    /// </summary>
    public bool GenerateNavigationProperties { get; set; } = true;

    /// <summary>
    /// Whether to generate interface files
    /// </summary>
    public bool GenerateInterface { get; set; } = true;

    /// <summary>
    /// Whether to generate GraphQL search request classes
    /// </summary>
    public bool GenerateGqlSearchRequest { get; set; } = false;

    /// <summary>
    /// Whether to generate abstract model classes
    /// </summary>
    public bool GenerateAbstractModels { get; set; } = true;

    /// <summary>
    /// Whether to clean up output directories before generation
    /// </summary>
    public bool Cleanup { get; set; } = true;

    #endregion

    #region Class Generation Options

    /// <summary>
    /// Whether to generate classes as partial classes
    /// </summary>
    public bool UsePartialClasses { get; set; } = true;

    /// <summary>
    /// Whether to implement INotifyPropertyChanged interface
    /// </summary>
    public bool ImplementINotifyPropertyChanged { get; set; } = false;

    /// <summary>
    /// Whether to use DateTimeOffset instead of DateTime
    /// </summary>
    public bool UseDateTimeOffset { get; set; } = false;

    /// <summary>
    /// Whether to use nullable reference types (C# 8.0+)
    /// </summary>
    public bool UseNullableReferenceTypes { get; set; } = true;

    /// <summary>
    /// Default string length for MaxLength attributes
    /// </summary>
    public int DefaultStringLength { get; set; } = 50;

    #endregion

    #region Enum Configuration

    /// <summary>
    /// Whether to generate enum fields as string properties with separate enum accessor properties
    /// When true, generates both string property and XXXEnum property with [NotMapped] attribute
    /// </summary>
    public bool GenerateEnumAsString { get; set; } = true;

    /// <summary>
    /// Suffix to append to enum accessor property names (default: "Enum")
    /// Examples: "Enum" -> RoleEnum, "Type" -> RoleType
    /// </summary>
    public string EnumPropertySuffix { get; set; } = "Enum";

    /// <summary>
    /// Whether to generate [NotMapped] attribute on enum accessor properties
    /// Required for Entity Framework to ignore enum properties during mapping
    /// </summary>
    public bool AddNotMappedToEnumProperties { get; set; } = true;

    /// <summary>
    /// Whether to generate [Ignore] attribute on enum accessor properties
    /// Used by some JSON serializers to ignore properties during serialization
    /// </summary>
    public bool AddIgnoreToEnumProperties { get; set; } = true;

    /// <summary>
    /// Whether to make enum accessor properties virtual
    /// Useful for inheritance scenarios and mocking frameworks
    /// </summary>
    public bool MakeEnumPropertiesVirtual { get; set; } = true;

    /// <summary>
    /// Custom field-to-enum type mappings
    /// Key: Field name pattern (case-insensitive), Value: Enum type name
    /// Example: ["Role"] = "MemberRoles", ["Status"] = "WorkflowStatus"
    /// </summary>
    public Dictionary<string, string> FieldToEnumMappings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to log warnings when enum types cannot be automatically determined
    /// Helpful for debugging enum resolution issues
    /// </summary>
    public bool LogEnumResolutionWarnings { get; set; } = true;

    /// <summary>
    /// Whether to generate enum properties with null safety checks
    /// When true, handles null/empty string values gracefully in enum conversion
    /// </summary>
    public bool UseNullSafeEnumConversion { get; set; } = true;

    #endregion

    #region Advanced Options

    /// <summary>
    /// Custom namespace mappings for specific paths
    /// Key: Path pattern, Value: Namespace segment
    /// </summary>
    public Dictionary<string, string> CustomNamespaceMappings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Custom type mappings for M3L types to C# types
    /// Key: M3L type name, Value: C# type name
    /// </summary>
    public Dictionary<string, string> CustomTypeMappings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Custom SQL type mappings for database column types
    /// Key: M3L type name, Value: SQL type format string
    /// </summary>
    public Dictionary<string, string> CustomSqlTypeMappings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to generate XML documentation comments for properties
    /// </summary>
    public bool GenerateXmlDocumentation { get; set; } = false;

    /// <summary>
    /// Whether to generate validation attributes based on field constraints
    /// </summary>
    public bool GenerateValidationAttributes { get; set; } = true;

    /// <summary>
    /// Whether to automatically generate JsonIgnore for sensitive fields
    /// </summary>
    public bool AutoGenerateJsonIgnoreForSensitive { get; set; } = true;

    #endregion

    #region Entity Framework Specific

    /// <summary>
    /// Whether to generate Entity Framework specific attributes
    /// Includes [Table], [Column], [ForeignKey], etc.
    /// </summary>
    public bool GenerateEntityFrameworkAttributes { get; set; } = true;

    /// <summary>
    /// Whether to use Entity Framework conventions for naming
    /// </summary>
    public bool UseEntityFrameworkConventions { get; set; } = true;

    /// <summary>
    /// Default decimal precision for SQL decimal types (format: "precision,scale")
    /// </summary>
    public string DefaultDecimalPrecision { get; set; } = "18,2";

    #endregion

    #region Methods

    /// <summary>
    /// Gets the full output path for generated files
    /// </summary>
    public string GetFullOutputPath() => ProjectPath;

    /// <summary>
    /// Adds a custom field-to-enum mapping
    /// </summary>
    /// <param name="fieldNamePattern">Field name pattern (case-insensitive)</param>
    /// <param name="enumTypeName">Enum type name to map to</param>
    public void AddFieldToEnumMapping(string fieldNamePattern, string enumTypeName)
    {
        if (!string.IsNullOrEmpty(fieldNamePattern) && !string.IsNullOrEmpty(enumTypeName))
        {
            FieldToEnumMappings[fieldNamePattern] = enumTypeName;
        }
    }

    /// <summary>
    /// Adds multiple field-to-enum mappings
    /// </summary>
    /// <param name="mappings">Dictionary of field patterns to enum type names</param>
    public void AddFieldToEnumMappings(Dictionary<string, string> mappings)
    {
        if (mappings != null)
        {
            foreach (var mapping in mappings)
            {
                AddFieldToEnumMapping(mapping.Key, mapping.Value);
            }
        }
    }

    /// <summary>
    /// Adds a custom namespace mapping
    /// </summary>
    /// <param name="pathPattern">Path pattern</param>
    /// <param name="namespaceSegment">Namespace segment to use</param>
    public void AddCustomNamespaceMapping(string pathPattern, string namespaceSegment)
    {
        if (!string.IsNullOrEmpty(pathPattern) && !string.IsNullOrEmpty(namespaceSegment))
        {
            CustomNamespaceMappings[pathPattern] = namespaceSegment;
        }
    }

    /// <summary>
    /// Adds a custom type mapping
    /// </summary>
    /// <param name="m3lType">M3L type name</param>
    /// <param name="csharpType">C# type name</param>
    public void AddCustomTypeMapping(string m3lType, string csharpType)
    {
        if (!string.IsNullOrEmpty(m3lType) && !string.IsNullOrEmpty(csharpType))
        {
            CustomTypeMappings[m3lType] = csharpType;
        }
    }

    /// <summary>
    /// Adds a custom SQL type mapping
    /// </summary>
    /// <param name="m3lType">M3L type name</param>
    /// <param name="sqlType">SQL type format string</param>
    public void AddCustomSqlTypeMapping(string m3lType, string sqlType)
    {
        if (!string.IsNullOrEmpty(m3lType) && !string.IsNullOrEmpty(sqlType))
        {
            CustomSqlTypeMappings[m3lType] = sqlType;
        }
    }

    /// <summary>
    /// Creates a copy of this configuration
    /// </summary>
    /// <returns>Deep copy of the configuration</returns>
    public ModelProjectConfig Clone()
    {
        return new ModelProjectConfig
        {
            // Basic Configuration
            ProjectPath = ProjectPath,
            Namespace = Namespace,
            ModelsPath = ModelsPath,
            InterfacesPath = InterfacesPath,
            EnumsPath = EnumsPath,
            GqlSearchRequestPath = GqlSearchRequestPath,

            // Generation Features
            GenerateNavigationProperties = GenerateNavigationProperties,
            GenerateInterface = GenerateInterface,
            GenerateGqlSearchRequest = GenerateGqlSearchRequest,
            GenerateAbstractModels = GenerateAbstractModels,
            Cleanup = Cleanup,

            // Class Generation Options
            UsePartialClasses = UsePartialClasses,
            ImplementINotifyPropertyChanged = ImplementINotifyPropertyChanged,
            UseDateTimeOffset = UseDateTimeOffset,
            UseNullableReferenceTypes = UseNullableReferenceTypes,
            DefaultStringLength = DefaultStringLength,

            // Enum Configuration
            GenerateEnumAsString = GenerateEnumAsString,
            EnumPropertySuffix = EnumPropertySuffix,
            AddNotMappedToEnumProperties = AddNotMappedToEnumProperties,
            AddIgnoreToEnumProperties = AddIgnoreToEnumProperties,
            MakeEnumPropertiesVirtual = MakeEnumPropertiesVirtual,
            FieldToEnumMappings = new Dictionary<string, string>(FieldToEnumMappings, StringComparer.OrdinalIgnoreCase),
            LogEnumResolutionWarnings = LogEnumResolutionWarnings,
            UseNullSafeEnumConversion = UseNullSafeEnumConversion,

            // Advanced Options
            CustomNamespaceMappings = new Dictionary<string, string>(CustomNamespaceMappings, StringComparer.OrdinalIgnoreCase),
            CustomTypeMappings = new Dictionary<string, string>(CustomTypeMappings, StringComparer.OrdinalIgnoreCase),
            CustomSqlTypeMappings = new Dictionary<string, string>(CustomSqlTypeMappings, StringComparer.OrdinalIgnoreCase),
            GenerateXmlDocumentation = GenerateXmlDocumentation,
            GenerateValidationAttributes = GenerateValidationAttributes,
            AutoGenerateJsonIgnoreForSensitive = AutoGenerateJsonIgnoreForSensitive,

            // Entity Framework Specific
            GenerateEntityFrameworkAttributes = GenerateEntityFrameworkAttributes,
            UseEntityFrameworkConventions = UseEntityFrameworkConventions,
            DefaultDecimalPrecision = DefaultDecimalPrecision
        };
    }

    /// <summary>
    /// Validates the configuration and throws exceptions for invalid settings
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(ProjectPath))
            throw new InvalidOperationException("ProjectPath must be specified");

        if (string.IsNullOrEmpty(Namespace))
            throw new InvalidOperationException("Namespace must be specified");

        if (DefaultStringLength <= 0)
            throw new InvalidOperationException("DefaultStringLength must be positive");

        if (string.IsNullOrEmpty(EnumPropertySuffix))
            throw new InvalidOperationException("EnumPropertySuffix cannot be empty");

        if (string.IsNullOrEmpty(ModelsPath))
            throw new InvalidOperationException("ModelsPath cannot be empty");
    }

    /// <summary>
    /// Returns a string representation of the configuration for debugging
    /// </summary>
    public override string ToString()
    {
        return $"ModelProjectConfig: Namespace={Namespace}, ProjectPath={ProjectPath}, " +
               $"EnumAsString={GenerateEnumAsString}, EnumSuffix={EnumPropertySuffix}, " +
               $"NavigationProps={GenerateNavigationProperties}, Cleanup={Cleanup}";
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// Creates a default configuration for Entity Framework projects
    /// </summary>
    public static ModelProjectConfig CreateEntityFrameworkConfig(string projectNamespace, string projectPath)
    {
        return new ModelProjectConfig
        {
            Namespace = projectNamespace,
            ProjectPath = projectPath,
            GenerateNavigationProperties = true,
            GenerateEnumAsString = true,
            UsePartialClasses = true,
            UseNullableReferenceTypes = true,
            GenerateEntityFrameworkAttributes = true,
            UseEntityFrameworkConventions = true,
            Cleanup = true
        };
    }

    /// <summary>
    /// Creates a default configuration for GraphQL projects
    /// </summary>
    public static ModelProjectConfig CreateGraphQLConfig(string projectNamespace, string projectPath)
    {
        return new ModelProjectConfig
        {
            Namespace = projectNamespace,
            ProjectPath = projectPath,
            GenerateGqlSearchRequest = true,
            GenerateNavigationProperties = false,
            GenerateEnumAsString = true,
            UsePartialClasses = true,
            UseNullableReferenceTypes = true,
            AutoGenerateJsonIgnoreForSensitive = true,
            Cleanup = true
        };
    }

    /// <summary>
    /// Creates a minimal configuration with basic settings
    /// </summary>
    public static ModelProjectConfig CreateMinimalConfig(string projectNamespace, string projectPath)
    {
        return new ModelProjectConfig
        {
            Namespace = projectNamespace,
            ProjectPath = projectPath,
            GenerateNavigationProperties = false,
            GenerateInterface = false,
            GenerateGqlSearchRequest = false,
            GenerateEnumAsString = false,
            UsePartialClasses = false,
            Cleanup = false
        };
    }

    #endregion
}