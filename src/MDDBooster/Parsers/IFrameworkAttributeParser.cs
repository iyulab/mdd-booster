using MDDBooster.Models;

namespace MDDBooster.Parsers;

public interface IFrameworkAttributeParser
{
    /// <summary>
    /// Parses a framework attribute and returns a structured representation
    /// </summary>
    FrameworkAttribute Parse(string attributeText);

    /// <summary>
    /// Returns true if this parser can handle the given attribute text
    /// </summary>
    bool CanParse(string attributeText);
}
