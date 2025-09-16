using MDDBooster.Models;

namespace MDDBooster.Parsers;

// Example of a specialized framework attribute parser
public class DataTypeAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        var attr = new FrameworkAttribute
        {
            RawText = attributeText,
            Name = "DataType"
        };

        var match = Regex.Match(attributeText, @"DataType\(DataType\.([^\)]+)\)");
        if (match.Success)
        {
            attr.Parameters.Add(match.Groups[1].Value);
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        return attributeText.Contains("DataType");
    }
}