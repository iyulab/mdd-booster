namespace MDDBooster.Processors;

public interface IRawTextProcessor
{
    /// <summary>
    /// Processes raw text before it's passed to the M3L parser
    /// </summary>
    string Process(string rawText);
}
