using M3LParser.Models;

namespace MDDBooster.Models;

public class MDDEnum
{
    public M3LEnum BaseEnum { get; set; }
    public string RawText { get; set; }
    public Dictionary<string, object> ExtendedMetadata { get; set; } = new Dictionary<string, object>();
}
