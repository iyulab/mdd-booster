using M3LParser.Models;

namespace MDDBooster.Models;

public class MDDDocument
{
    public M3LDocument BaseDocument { get; set; }
    public string RawText { get; set; }
    public Dictionary<string, object> ExtendedMetadata { get; set; } = new Dictionary<string, object>();

    // Extended collections that may contain additional information beyond what M3LParser provides
    public List<MDDModel> Models { get; set; } = new List<MDDModel>();
    public List<MDDEnum> Enums { get; set; } = new List<MDDEnum>();
    public List<MDDInterface> Interfaces { get; set; } = new List<MDDInterface>();
}
