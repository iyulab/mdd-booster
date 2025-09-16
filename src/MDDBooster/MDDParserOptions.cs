using MDDBooster.Parsers;
using MDDBooster.Processors;

namespace MDDBooster;

public class MDDParserOptions
{
    /// <summary>
    /// Collection of framework attribute parsers
    /// </summary>
    public List<IFrameworkAttributeParser> FrameworkAttributeParsers { get; set; } = new List<IFrameworkAttributeParser>();

    /// <summary>
    /// Collection of raw text processors that will be applied before parsing
    /// </summary>
    public List<IRawTextProcessor> RawTextProcessors { get; set; } = new List<IRawTextProcessor>();

    /// <summary>
    /// Collection of model processors that will be applied after parsing
    /// </summary>
    public List<IModelProcessor> ModelProcessors { get; set; } = new List<IModelProcessor>();
}
