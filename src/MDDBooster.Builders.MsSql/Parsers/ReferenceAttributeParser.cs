using MDDBooster.Parsers;

namespace MDDBooster.Builders.MsSql.Parsers;

/// <summary>
/// Parser for reference-specific framework attributes
/// </summary>
public class ReferenceAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        var attr = new FrameworkAttribute
        {
            RawText = attributeText
        };

        if (attributeText.StartsWith("OnDelete", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "OnDelete";
            var match = Regex.Match(attributeText, @"OnDelete\(([^)]+)\)");
            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value.Trim('"', '\''));
            }
            else
            {
                // Default is CASCADE if no parameter specified
                attr.Parameters.Add("CASCADE");
            }
        }
        else if (attributeText.StartsWith("ForeignKey", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "ForeignKey";
            var match = Regex.Match(attributeText, @"ForeignKey\(([^)]+)\)");
            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value.Trim('"', '\''));
            }
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        return attributeText.StartsWith("OnDelete", StringComparison.OrdinalIgnoreCase) ||
               attributeText.StartsWith("ForeignKey", StringComparison.OrdinalIgnoreCase);
    }
}