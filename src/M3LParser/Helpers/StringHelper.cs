namespace M3LParser.Helpers;

/// <summary>
/// Helper methods for string manipulation
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// Extract the name part from a definition line
    /// </summary>
    public static string ExtractName(string line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        // Remove leading ## if present
        if (line.StartsWith("##"))
            line = line.Substring(2).Trim();

        // Extract name before any special characters
        int endOfName = line.IndexOfAny(new[] { '(', ':', '#', '@', ' ' });
        if (endOfName == -1)
            return line.Trim();

        return line.Substring(0, endOfName).Trim();
    }

    /// <summary>
    /// Extract the label part from a definition line (text in parentheses)
    /// </summary>
    public static string ExtractLabel(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var match = Regex.Match(line, @"\((.+?)\)");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return null;
    }

    /// <summary>
    /// Extract the inheritance part from a definition line (text after colon)
    /// </summary>
    public static List<string> ExtractInheritance(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains(':'))
            return new List<string>();

        // Find the colon
        int colonIndex = line.IndexOf(':');

        // Check for special keywords after colon (::interface, ::enum)
        if (colonIndex + 1 < line.Length && line[colonIndex + 1] == ':')
            return new List<string>();

        // Extract the inheritance part
        var inheritancePart = line.Substring(colonIndex + 1);

        // Remove any trailing parts (after #, @, etc.)
        int endOfInheritance = inheritancePart.IndexOfAny(new[] { '#', '@' });
        if (endOfInheritance != -1)
            inheritancePart = inheritancePart.Substring(0, endOfInheritance);

        // Split by comma and trim
        return inheritancePart.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    /// <summary>
    /// Extract the description part from a definition line (text after # symbol)
    /// </summary>
    public static string ExtractDescription(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains('#'))
            return null;

        // This handles # as a comment marker, not ## as a heading marker
        int hashIndex = line.IndexOf('#');
        if (hashIndex == 0 || (hashIndex > 0 && line[hashIndex - 1] == '#'))
            return null;

        return line.Substring(hashIndex + 1).Trim();
    }

    /// <summary>
    /// Extract attributes from a definition line (text after @ symbol)
    /// </summary>
    public static List<string> ExtractAttributes(string line)
    {
        var attributes = new List<string>();
        if (string.IsNullOrEmpty(line) || !line.Contains('@'))
            return attributes;

        var parts = line.Split('@', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < parts.Length; i++)
        {
            var attrText = parts[i].Trim();
            var endOfAttr = attrText.IndexOfAny(new[] { ' ', '\t', '(', ')' });

            if (endOfAttr > 0)
            {
                if (attrText[endOfAttr] == '(')
                {
                    // Find the closing parenthesis
                    int closeParenIndex = attrText.IndexOf(')', endOfAttr);
                    if (closeParenIndex != -1)
                        attrText = attrText.Substring(0, closeParenIndex + 1);
                    else
                        attrText = attrText.Substring(0, endOfAttr);
                }
                else
                {
                    attrText = attrText.Substring(0, endOfAttr);
                }
            }

            attributes.Add('@' + attrText);
        }

        return attributes;
    }

    /// <summary>
    /// Extract framework attributes from a definition line (text in square brackets)
    /// </summary>
    public static List<string> ExtractFrameworkAttributes(string line)
    {
        var attributes = new List<string>();
        if (string.IsNullOrEmpty(line) || !line.Contains('['))
            return attributes;

        var matches = Regex.Matches(line, @"\[([^\]]+)\]");
        foreach (Match match in matches)
        {
            attributes.Add(match.Groups[1].Value);
        }

        return attributes;
    }

    /// <summary>
    /// Normalize a name according to common naming conventions
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.StartsWith('_'))
            return name;

        // Just log the name before normalization for debugging
        AppLog.Debug("Normalizing name: '{Name}'", name);

        // Remove invalid characters
        var validChars = new List<char>();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                validChars.Add(c);
            else if (char.IsWhiteSpace(c))
                validChars.Add('_');
        }
        name = new string(validChars.ToArray());

        // Check if name already starts with uppercase letter
        bool startsWithUppercase = name.Length > 0 && char.IsUpper(name[0]);

        // Only convert to PascalCase if requested AND name doesn't already start with uppercase
        if (name.Length > 0 && !startsWithUppercase)
        {
            // Split by underscores
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);

            // Capitalize first letter of each part
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
                }
            }
            name = string.Join("", parts);
        }

        AppLog.Debug("Normalized name: '{NormalizedName}'", name);
        return name;
    }

    /// <summary>
    /// Transform a default value for use in different output formats
    /// </summary>
    public static string TransformDefaultValue(string defaultValue, string type, string format = "sql")
    {
        if (string.IsNullOrEmpty(defaultValue))
            return string.Empty;

        if (format.Equals("sql", StringComparison.OrdinalIgnoreCase))
        {
            // Handle SQL Server specific transformations
            if (defaultValue == "now()")
            {
                return "GETDATE()";
            }
            else if (defaultValue == "true")
            {
                return "1";
            }
            else if (defaultValue == "false")
            {
                return "0";
            }
            else if (defaultValue.StartsWith("\"") && defaultValue.EndsWith("\""))
            {
                // String literal - use N prefix for Unicode and ensure proper quoting
                return $"N'{defaultValue.Trim('"').Replace("'", "''")}'"
    ;
            }
            else if (defaultValue == "@now")
            {
                return "GETDATE()";
            }
            else if (defaultValue == "@by")
            {
                return "N'system'";
            }

            // Default handling based on type
            switch (type.ToLowerInvariant())
            {
                case "string":
                case "text":
                case "enum": // Added enum type specifically to ensure proper quoting
                             // For plain string values (not already quoted), add N prefix and quotes
                    return $"N'{defaultValue.Replace("'", "''")}'";
                case "boolean":
                    return defaultValue.ToLowerInvariant() == "true" ? "1" : "0";
                default:
                    return defaultValue;
            }
        }
        else if (format.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            // Handle C# specific transformations
            if (defaultValue == "now()")
            {
                return "DateTime.Now";
            }
            else if (defaultValue == "@now")
            {
                return "DateTime.Now";
            }
            else if (defaultValue == "@by")
            {
                return "\"system\"";
            }

            // Default handling based on type
            switch (type.ToLowerInvariant())
            {
                case "string":
                case "text":
                case "enum": // Added enum type for consistent handling
                    return $"\"{defaultValue.Replace("\"", "\\\"")}\"";
                case "boolean":
                    return defaultValue.ToLowerInvariant(); // true/false in C#
                default:
                    return defaultValue;
            }
        }

        // Default: return as-is
        return defaultValue;
    }

    /// <summary>
    /// Extract text between two marker strings
    /// </summary>
    public static string ExtractBetween(string content, string startMarker, string endMarker)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        int startIdx = content.IndexOf(startMarker);
        if (startIdx < 0)
            return string.Empty;

        startIdx += startMarker.Length;
        int endIdx = content.IndexOf(endMarker, startIdx);

        if (endIdx < 0)
            return string.Empty;

        return content.Substring(startIdx, endIdx - startIdx);
    }

    /// <summary>
    /// Escape special characters in a string for use in regular expressions
    /// </summary>
    public static string EscapeRegex(string str)
    {
        return Regex.Escape(str);
    }

    /// <summary>
    /// Indent a block of text by a specified number of spaces
    /// </summary>
    public static string IndentText(string text, int indentSize)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string indentation = new string(' ', indentSize);
        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                lines[i] = indentation + lines[i];
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}