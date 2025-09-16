using M3LParser.Models;

namespace MDDBooster.Models;

public class MDDField
{
    public M3LField BaseField { get; set; }
    public string RawText { get; set; }
    public List<FrameworkAttribute> FrameworkAttributes { get; set; } = new List<FrameworkAttribute>();
    public Dictionary<string, object> ExtendedMetadata { get; set; } = new Dictionary<string, object>();
}
