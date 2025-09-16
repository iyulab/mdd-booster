namespace M3LParser.Models;

public class M3LModel
{
    public string Name { get; set; }
    public string Label { get; set; }
    public string Description { get; set; }
    public List<string> Inherits { get; set; } = new List<string>();
    public List<M3LField> Fields { get; set; } = new List<M3LField>();
    public List<M3LRelation> Relations { get; set; } = new List<M3LRelation>();
    public List<M3LIndex> Indexes { get; set; } = new List<M3LIndex>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    public List<string> Attributes { get; set; } = new List<string>();
    public bool IsAbstract => Attributes.Contains("@abstract");
    public bool IsDefault => Attributes.Contains("@default");
}
