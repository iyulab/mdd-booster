namespace M3LParser.Parsers;

/// <summary>
/// Parser for index definitions
/// </summary>
public class IndexParser : BaseParser
{
    public IndexParser(ParserContext context) : base(context)
    {
    }

    /// <summary>
    /// Parse an index definition
    /// </summary>
    public M3LIndex Parse()
    {
        var currentLine = Context.CurrentLineTrimmed;

        // If not an index definition, return null
        if (!currentLine.StartsWith("-"))
        {
            return null;
        }

        var index = new M3LIndex();
        AppLog.Debug("Parsing index at line {LineNumber}", Context.CurrentLineIndex + 1);

        // Remove the leading dash
        var content = currentLine.Substring(1).Trim();

        // Parse the index name and description
        if (content.Contains("(") && content.Contains(")"))
        {
            var match = Regex.Match(content, @"(.+?)\((.+?)\)");
            if (match.Success)
            {
                index.Name = match.Groups[1].Value.Trim();
                index.Description = match.Groups[2].Value;
                AppLog.Debug("Index name: {IndexName}, description: {Description}", index.Name, index.Description);
            }
            else
            {
                index.Name = content;
                AppLog.Debug("Index name: {IndexName}", index.Name);
            }
        }
        else
        {
            index.Name = content;
            AppLog.Debug("Index name: {IndexName}", index.Name);
        }

        // Check for extended index properties
        if (!Context.NextLine())
            return index;

        while (Context.HasMoreLines)
        {
            var subLine = Context.CurrentLineTrimmed;

            // End of extended index definition
            if (!subLine.StartsWith("-") || !subLine.Substring(1).TrimStart().StartsWith("-"))
            {
                break;
            }

            // Parse subproperty
            var subProperty = subLine.Substring(1).Trim().Substring(1).Trim();
            if (subProperty.StartsWith("fields:"))
            {
                var fieldsText = subProperty.Substring("fields:".Length).Trim();
                if (fieldsText.StartsWith("[") && fieldsText.EndsWith("]"))
                {
                    fieldsText = fieldsText.Substring(1, fieldsText.Length - 2);
                }

                var fields = fieldsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim())
                    .ToList();

                index.Fields.AddRange(fields);
                AppLog.Debug("Index fields: {Fields}", string.Join(", ", fields));
            }
            else if (subProperty.StartsWith("unique:"))
            {
                var uniqueValue = subProperty.Substring("unique:".Length).Trim().ToLowerInvariant();
                index.IsUnique = uniqueValue == "true";
                AppLog.Debug("Index is unique: {IsUnique}", index.IsUnique);
            }
            else if (subProperty.StartsWith("fulltext:"))
            {
                var fulltextValue = subProperty.Substring("fulltext:".Length).Trim().ToLowerInvariant();
                index.IsFullText = fulltextValue == "true";
                AppLog.Debug("Index is fulltext: {IsFullText}", index.IsFullText);
            }

            Context.NextLine();
        }

        return index;
    }
}