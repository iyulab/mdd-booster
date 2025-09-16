namespace MDDBooster.Builders.ModelProject.Parsers;

/// <summary>
/// Parser for model-specific framework attributes
/// </summary>
public class ModelAttributeParser : IFrameworkAttributeParser
{
    public FrameworkAttribute Parse(string attributeText)
    {
        var attr = new FrameworkAttribute
        {
            RawText = attributeText
        };

        // Handle Display attribute
        if (attributeText.StartsWith("Display", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "Display";

            var match = System.Text.RegularExpressions.Regex.Match(
                attributeText,
                @"Display\((?:Name\s*=\s*""([^""]+)"")(?:,\s*ShortName\s*=\s*""([^""]+)"")?\)");

            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value);

                if (match.Groups[2].Success)
                {
                    attr.Parameters.Add(match.Groups[2].Value);
                }
            }
        }
        // Handle DataType attribute
        else if (attributeText.StartsWith("DataType", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "DataType";

            var match = System.Text.RegularExpressions.Regex.Match(
                attributeText,
                @"DataType\(DataType\.([^)]+)\)");

            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value);
            }
        }
        // Handle Required attribute
        else if (attributeText.Equals("Required", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "Required";
        }
        // Handle MaxLength attribute
        else if (attributeText.StartsWith("MaxLength", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "MaxLength";

            var match = System.Text.RegularExpressions.Regex.Match(
                attributeText,
                @"MaxLength\((\d+)\)");

            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value);
            }
        }
        // Handle Column attribute
        else if (attributeText.StartsWith("Column", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "Column";

            var match = System.Text.RegularExpressions.Regex.Match(
                attributeText,
                @"Column\(""([^""]+)"",\s*TypeName\s*=\s*""([^""]+)""\)");

            if (match.Success)
            {
                attr.Parameters.Add(match.Groups[1].Value);
                attr.Parameters.Add(match.Groups[2].Value);
            }
        }
        // Handle JsonIgnore attribute
        else if (attributeText.Equals("JsonIgnore", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "JsonIgnore";
        }
        // Handle Multiline attribute
        else if (attributeText.Equals("Multiline", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "Multiline";
        }
        // Handle Unique attribute
        else if (attributeText.Equals("Unique", StringComparison.OrdinalIgnoreCase))
        {
            attr.Name = "Unique";
        }
        else
        {
            // Default handling for other attributes
            var match = System.Text.RegularExpressions.Regex.Match(
                attributeText,
                @"^([^\(]+)(?:\(([^\)]+)\))?$");

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
                        attr.Parameters = parameters
                            .Split(',')
                            .Select(p => p.Trim())
                            .ToList();
                    }
                }
            }
            else
            {
                attr.Name = attributeText;
            }
        }

        return attr;
    }

    public bool CanParse(string attributeText)
    {
        // List of entity framework and validation related attributes this parser can handle
        var supportedAttributes = new[]
        {
            "Display", "DataType", "Required", "MaxLength", "MinLength",
            "Column", "Table", "Key", "JsonIgnore", "Multiline",
            "Range", "StringLength", "EmailAddress", "Phone", "Url",
            "CreditCard", "RegularExpression", "Unique", "Computed"
        };

        foreach (var attr in supportedAttributes)
        {
            if (attributeText.StartsWith(attr, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}