namespace MDDBooster.Builders.MsSql;

/// <summary>
/// Registration module for MsSql Builder components
/// </summary>
public static class MsSqlBuilderModule
{
    /// <summary>
    /// Registers all MsSql Builder components with the MDDParserOptions
    /// </summary>
    public static MDDParserOptions UseMsSqlBuilder(this MDDParserOptions options)
    {
        // Register framework attribute parsers
        options.FrameworkAttributeParsers.Add(new MsSqlAttributeParser());
        options.FrameworkAttributeParsers.Add(new InsertAttributeParser());
        options.FrameworkAttributeParsers.Add(new UpdateAttributeParser());
        options.FrameworkAttributeParsers.Add(new ReferenceAttributeParser());

        // Register model processors
        options.ModelProcessors.Add(new MsSqlFrameworkAttributeProcessor());
        options.ModelProcessors.Add(new ReferenceAttributeProcessor());

        return options;
    }

    /// <summary>
    /// Creates an MsSql script generator for the specified MDDDocument
    /// </summary>
    public static MsSqlScriptGenerator CreateScriptGenerator(
        this MDDDocument document,
        bool useSchemaNamespace = true,
        string schemaNameOverride = "dbo",
        bool generateTriggers = false,
        bool generateForeignKeys = true)
    {
        return new MsSqlScriptGenerator(
            document,
            useSchemaNamespace,
            schemaNameOverride,
            generateTriggers,
            generateForeignKeys);
    }

    /// <summary>
    /// Creates a foreign key constraint generator for the specified MDDDocument
    /// </summary>
    public static ForeignKeyConstraintGenerator CreateForeignKeyGenerator(
        this MDDDocument document,
        bool useSchemaNamespace = true,
        string schemaNameOverride = "dbo",
        bool cascadeDelete = true)
    {
        string schemaName = useSchemaNamespace ? document.BaseDocument.Namespace : schemaNameOverride;
        return new ForeignKeyConstraintGenerator(document, schemaName, cascadeDelete);
    }
}