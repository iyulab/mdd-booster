namespace M3LParser.Parsers;

/// <summary>
/// Base class for all entity parsers with common utilities
/// </summary>
public abstract class BaseParser
{
    /// <summary>
    /// The parsing context
    /// </summary>
    protected readonly ParserContext Context;

    /// <summary>
    /// Initialize a new parser with the given context
    /// </summary>
    protected BaseParser(ParserContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// The lines of content being parsed
    /// </summary>
    protected string[] Lines => Context.Lines;

    /// <summary>
    /// The current line index
    /// </summary>
    protected int CurrentLineIndex => Context.CurrentLineIndex;

    /// <summary>
    /// The current line being processed
    /// </summary>
    protected string CurrentLine => Context.CurrentLine;

    /// <summary>
    /// The trimmed version of the current line
    /// </summary>
    protected string CurrentLineTrimmed => Context.CurrentLineTrimmed;

    /// <summary>
    /// Check if a line is the start of a new definition
    /// </summary>
    protected bool IsStartOfDefinition(string line)
    {
        return !string.IsNullOrEmpty(line) && line.StartsWith("##");
    }

    /// <summary>
    /// Check if a line is the end of the current definition
    /// </summary>
    protected bool IsEndOfDefinition(string line)
    {
        return IsStartOfDefinition(line) || (line.StartsWith("#") && !line.StartsWith("###"));
    }

    /// <summary>
    /// Check if a line is the start of a section
    /// </summary>
    protected bool IsStartOfSection(string line)
    {
        return !string.IsNullOrEmpty(line) && line.StartsWith("###");
    }

    /// <summary>
    /// Get the section name from a section start line
    /// </summary>
    protected string GetSectionName(string line)
    {
        if (!IsStartOfSection(line))
            return string.Empty;

        return line.Substring(3).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Check if a line is a field definition
    /// </summary>
    protected bool IsFieldDefinition(string line)
    {
        return !string.IsNullOrEmpty(line) && line.TrimStart().StartsWith("-");
    }

    /// <summary>
    /// Parse a list of parameters from a parameter string
    /// </summary>
    protected List<string> ParseParameters(string paramString)
    {
        AppLog.Debug("Parsing parameters: {Params}", paramString);
        var parameters = new List<string>();
        var currentParam = new StringBuilder();
        bool inQuotes = false;
        int nestedParenCount = 0;

        foreach (char c in paramString)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                currentParam.Append(c);
            }
            else if (c == '(' && !inQuotes)
            {
                nestedParenCount++;
                currentParam.Append(c);
            }
            else if (c == ')' && !inQuotes)
            {
                nestedParenCount--;
                currentParam.Append(c);
            }
            else if (c == ',' && !inQuotes && nestedParenCount == 0)
            {
                parameters.Add(currentParam.ToString().Trim());
                currentParam.Clear();
            }
            else
            {
                currentParam.Append(c);
            }
        }

        if (currentParam.Length > 0)
        {
            parameters.Add(currentParam.ToString().Trim());
        }

        if (parameters.Count > 0)
        {
            AppLog.Debug("Parsed {Count} parameters", parameters.Count);
            foreach (var param in parameters)
            {
                AppLog.Debug("  Parameter: {Param}", param);
            }
        }
        else
        {
            AppLog.Warning("No parameters parsed from string: {Params}", paramString);
        }

        return parameters;
    }

    /// <summary>
    /// Extract attributes from a string
    /// </summary>
    protected List<string> ExtractAttributes(string text)
    {
        var attributes = new List<string>();
        if (!text.Contains('@'))
            return attributes;

        var parts = text.Split('@');
        for (int i = 1; i < parts.Length; i++)
        {
            var attrText = parts[i].Trim();
            var endOfAttr = attrText.IndexOfAny(new[] { ' ', '\t' });

            if (endOfAttr > 0)
            {
                attrText = attrText.Substring(0, endOfAttr);
            }

            attributes.Add('@' + attrText);
        }

        return attributes;
    }

    /// <summary>
    /// Extract framework attributes in square brackets
    /// </summary>
    protected List<string> ExtractFrameworkAttributes(string text)
    {
        var attributes = new List<string>();
        if (!text.Contains('['))
            return attributes;

        var matches = Regex.Matches(text, @"\[([^\]]+)\]");
        foreach (Match match in matches)
        {
            attributes.Add(match.Groups[1].Value);
        }

        return attributes;
    }

    /// <summary>
    /// Skip empty lines and comments
    /// </summary>
    protected bool SkipEmptyLinesAndComments()
    {
        while (Context.HasMoreLines)
        {
            var line = Context.CurrentLineTrimmed;

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("<!--") ||
                line.StartsWith("-->"))
            {
                if (!Context.NextLine())
                    return false;

                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Parse entity name with inheritance information
    /// </summary>
    /// <typeparam name="T">Entity type with Name and Inherits properties</typeparam>
    protected void ParseEntityNameWithInheritance<T>(T entity, string namePart) where T : class
    {
        // Get property accessors via reflection
        var nameProperty = typeof(T).GetProperty("Name");
        var inheritsProperty = typeof(T).GetProperty("Inherits");

        if (nameProperty == null || inheritsProperty == null)
        {
            AppLog.Warning("Type {Type} doesn't have Name or Inherits property", typeof(T).Name);
            return;
        }

        if (namePart.Contains(':'))
        {
            var inheritanceParts = namePart.Split(':', StringSplitOptions.RemoveEmptyEntries);
            nameProperty.SetValue(entity, inheritanceParts[0].Trim());

            if (inheritanceParts.Length > 1)
            {
                var inheritsList = inheritanceParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .ToList();

                // Get the current inherits list if it exists
                var currentInherits = inheritsProperty.GetValue(entity) as IList<string>;
                if (currentInherits != null)
                {
                    foreach (var item in inheritsList)
                    {
                        currentInherits.Add(item);
                    }
                }
                else
                {
                    // Create a new list if one doesn't exist
                    inheritsProperty.SetValue(entity, inheritsList);
                }

                AppLog.Debug("Entity {EntityName} inherits from: {InheritanceList}",
                    nameProperty.GetValue(entity), string.Join(", ", inheritsList));
            }
        }
        else
        {
            nameProperty.SetValue(entity, namePart);
        }
    }
}