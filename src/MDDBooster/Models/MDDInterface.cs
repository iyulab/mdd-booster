using M3LParser.Models;

namespace MDDBooster.Models;

public class MDDInterface
{
    public M3LInterface BaseInterface { get; set; }
    public string RawText { get; set; }
    public Dictionary<string, object> ExtendedMetadata { get; set; } = new Dictionary<string, object>();
    public List<MDDField> Fields { get; set; } = new List<MDDField>();
}
