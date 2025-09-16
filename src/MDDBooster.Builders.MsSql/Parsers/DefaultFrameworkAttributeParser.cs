using MDDBooster.Models;
using MDDBooster.Parsers;

namespace MDDBooster.Builders.MsSql.Parsers;

public class DefaultFrameworkAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        // Parse framework attributes like [DataType(DataType.Password)][JsonIgnore]
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

                // Handle quoted parameters 
                if (parameters.StartsWith("\"") && parameters.EndsWith("\""))
                {
                    attr.Parameters.Add(parameters.Trim('"'));
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
        // This parser can handle all framework attributes
        return true;
    }
}