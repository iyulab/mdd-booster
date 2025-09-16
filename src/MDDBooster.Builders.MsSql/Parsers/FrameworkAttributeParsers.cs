using MDDBooster.Models;
using MDDBooster.Parsers;

namespace MDDBooster.Builders.MsSql.Parsers;

// Default framework attribute parser for SQL Server
public class MsSqlAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        // Parse framework attributes for SQL Server
        var attr = new FrameworkAttribute
        {
            RawText = attributeText
        };

        // Extract name and parameters
        var match = Regex.Match(attributeText, @"^([^\(]+)(?:\(([^\)]+)\))?$");
        if (match.Success)
        {
            attr.Name = match.Groups[1].Value.Trim();

            if (match.Groups.Count > 2 && match.Groups[2].Success)
            {
                var parameters = match.Groups[2].Value;

                // Handle special case for string literals in SQL Server
                if (parameters.StartsWith("\"") && parameters.EndsWith("\""))
                {
                    attr.Parameters.Add(parameters);
                }
                else
                {
                    attr.Parameters = parameters.Split(',')
                        .Select(p => p.Trim())
                        .ToList();
                }
            }
        }
        else
        {
            attr.Name = attributeText;
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        // Special handling for SQL Server specific attributes
        var sqlServerAttributes = new[] { "Insert", "Update", "Without" };
        foreach (var attr in sqlServerAttributes)
        {
            if (attributeText.StartsWith(attr, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

// Specialized parser for Insert attribute
public class InsertAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        var attr = new FrameworkAttribute
        {
            RawText = attributeText,
            Name = "Insert"
        };

        var match = Regex.Match(attributeText, @"Insert\(""([^""]+)""\)");
        if (match.Success)
        {
            attr.Parameters.Add(match.Groups[1].Value);
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        return attributeText.StartsWith("Insert", StringComparison.OrdinalIgnoreCase);
    }
}

// Specialized parser for Update attribute
public class UpdateAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        var attr = new FrameworkAttribute
        {
            RawText = attributeText,
            Name = "Update"
        };

        var match = Regex.Match(attributeText, @"Update\(""([^""]+)""\)");
        if (match.Success)
        {
            attr.Parameters.Add(match.Groups[1].Value);
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        return attributeText.StartsWith("Update", StringComparison.OrdinalIgnoreCase);
    }
}