namespace M3LParser.Parsers;

/// <summary>
/// Parser for metadata definitions
/// </summary>
public class MetadataParser : BaseParser
{
    public MetadataParser(ParserContext context) : base(context)
    {
    }

    /// <summary>
    /// Parse a metadata definition
    /// </summary>
    public (string, object) Parse()
    {
        var currentLine = Context.CurrentLineTrimmed;

        // If not a metadata definition, return default
        if (!currentLine.StartsWith("-"))
        {
            Context.NextLine();
            return ("unknown", null);
        }

        // Remove the leading dash
        var content = currentLine.Substring(1).Trim();
        AppLog.Debug("Parsing metadata line: {Line}", content);

        Context.NextLine();

        // Parse key-value pair
        if (content.Contains(':'))
        {
            var parts = content.Split(':', 2);
            var key = parts[0].Trim();
            var value = parts[1].Trim();

            // Remove quotes if present
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
                return (key, value);
            }
            else if (value.ToLowerInvariant() == "true")
            {
                return (key, true);
            }
            else if (value.ToLowerInvariant() == "false")
            {
                return (key, false);
            }
            else if (int.TryParse(value, out int intValue))
            {
                return (key, intValue);
            }
            else if (double.TryParse(value, out double doubleValue))
            {
                return (key, doubleValue);
            }

            AppLog.Debug("Metadata: {Key} = {Value}", key, value);
            return (key, value);
        }

        // If no colon, treat as a flag (boolean true)
        AppLog.Debug("Metadata flag: {Key} = true", content);
        return (content, true);
    }
}