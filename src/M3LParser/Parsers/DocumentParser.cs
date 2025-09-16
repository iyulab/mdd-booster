namespace M3LParser.Parsers;

/// <summary>
/// Parser for the overall document structure
/// </summary>
public class DocumentParser : BaseParser
{
    private readonly ModelParser _modelParser;
    private readonly InterfaceParser _interfaceParser;
    private readonly EnumParser _enumParser;

    public DocumentParser(ParserContext context) : base(context)
    {
        _modelParser = new ModelParser(context);
        _interfaceParser = new InterfaceParser(context);
        _enumParser = new EnumParser(context);
    }

    /// <summary>
    /// Parse the entire document
    /// </summary>
    public M3LDocument Parse()
    {
        AppLog.Debug("Beginning document parsing");

        // Start from the beginning
        Context.CurrentLineIndex = 0;

        // Process document line by line
        while (Context.HasMoreLines)
        {
            // Skip empty lines and comments
            if (!SkipEmptyLinesAndComments())
                break;

            var line = Context.CurrentLineTrimmed;

            // Parse namespace
            if (line.StartsWith("# Namespace:"))
            {
                Context.Document.Namespace = line.Substring("# Namespace:".Length).Trim();
                AppLog.Debug("Parsed namespace: {Namespace}", Context.Document.Namespace);
                Context.NextLine();
            }
            // Parse other document titles that might contain namespace information
            else if (line.StartsWith("#") && !line.StartsWith("##"))
            {
                if (string.IsNullOrEmpty(Context.Document.Namespace))
                {
                    Context.Document.Namespace = line.Substring(1).Trim();
                    AppLog.Debug("Using document title as namespace: {Namespace}", Context.Document.Namespace);
                }
                Context.NextLine();
            }
            // Parse model, interface or enum
            else if (line.StartsWith("##"))
            {
                var definitionLine = line.Substring(2).Trim();

                if (definitionLine.Contains("::interface"))
                {
                    var interface_ = _interfaceParser.Parse(definitionLine);
                    AppLog.Debug("Added interface: {InterfaceName}", interface_.Name);
                    Context.Document.Interfaces.Add(interface_);
                }
                else if (definitionLine.Contains("::enum"))
                {
                    var enum_ = _enumParser.Parse(definitionLine);
                    AppLog.Debug("Added enum: {EnumName}", enum_.Name);
                    Context.Document.Enums.Add(enum_);
                }
                else
                {
                    var model = _modelParser.Parse(definitionLine);
                    AppLog.Debug("Added model: {ModelName}", model.Name);
                    Context.Document.Models.Add(model);
                }
            }
            else
            {
                // Unrecognized line, skip it
                Context.NextLine();
            }
        }

        // Don't apply default inheritance here, it should be handled by the InheritanceProcessor
        // to avoid circular references

        AppLog.Information("Parsing completed: Found {ModelCount} models, {InterfaceCount} interfaces, {EnumCount} enums",
            Context.Document.Models.Count, Context.Document.Interfaces.Count, Context.Document.Enums.Count);

        return Context.Document;
    }
}