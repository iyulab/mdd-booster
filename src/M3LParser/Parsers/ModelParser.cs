namespace M3LParser.Parsers;

/// <summary>
/// Model definition parser (splits the original ModelParser into smaller units)
/// </summary>
public class ModelParser : BaseParser
{
    private readonly ModelDefinitionParser _definitionParser;
    private readonly ModelContentParser _contentParser;

    /// <summary>
    /// Initialize a new model parser
    /// </summary>
    public ModelParser(ParserContext context) : base(context)
    {
        _definitionParser = new ModelDefinitionParser(context);
        _contentParser = new ModelContentParser(context);
    }

    /// <summary>
    /// Parse a model definition
    /// </summary>
    public M3LModel Parse(string definitionLine)
    {
        var model = new M3LModel();

        AppLog.Debug("Parsing model at line {LineNumber}: {Line}", Context.CurrentLineIndex + 1, definitionLine);

        // Parse model definition (name, label, inheritance, attributes)
        _definitionParser.ParseModelDefinition(model, definitionLine);

        // Process model body
        if (!Context.NextLine())
            return model;

        // Parse model content (fields, relations, indexes, etc.)
        _contentParser.ParseModelContent(model);

        AppLog.Debug("Completed parsing model {ModelName} with {FieldCount} fields, {RelationCount} relations, {IndexCount} indexes",
            model.Name, model.Fields.Count, model.Relations.Count, model.Indexes.Count);

        return model;
    }
}
