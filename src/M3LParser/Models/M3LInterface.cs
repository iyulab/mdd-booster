namespace M3LParser.Models;

public class M3LInterface
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Inherits { get; set; } = new List<string>();
    public List<M3LField> Fields { get; set; } = new List<M3LField>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
