using M3LParser.Parsers;

namespace M3LParser;

/// <summary>
/// Main parser class for M3L files
/// </summary>
public class M3LParser
{
    private readonly M3LParserOptions _options;

    /// <summary>
    /// Initialize a new M3L parser
    /// </summary>
    public M3LParser(M3LParserOptions options = null)
    {
        _options = options ?? new M3LParserOptions();
    }

    /// <summary>
    /// Parse an M3L file from a file path
    /// </summary>
    public M3LDocument Parse(string filePath)
    {
        AppLog.Information("Parsing file: {FilePath}", filePath);

        try
        {
            var content = File.ReadAllText(filePath);
            AppLog.Debug("File loaded, size: {Size} bytes", content.Length);
            return ParseContent(content);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error reading or parsing file: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Parse M3L content from a string
    /// </summary>
    public M3LDocument ParseContent(string content)
    {
        AppLog.Debug("Beginning content parsing");

        try
        {
            // Apply pre-processing if configured in options
            if (_options.PreProcessContent != null)
            {
                content = _options.PreProcessContent(content);
            }

            // Create parsing context
            var context = new ParserContext(content);

            // Create document parser
            var documentParser = new DocumentParser(context);

            // Parse the document
            var document = documentParser.Parse();

            // Apply post-processing if configured in options
            if (_options.PostProcessDocument != null)
            {
                _options.PostProcessDocument(document);
            }

            return document;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Error parsing content");
            throw;
        }
    }
}