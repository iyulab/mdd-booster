using MDDBooster.Models;

namespace MDDBooster.Processors;

public interface IModelProcessor
{
    /// <summary>
    /// Processes a parsed model after basic parsing is complete
    /// </summary>
    void Process(MDDDocument document);
}
