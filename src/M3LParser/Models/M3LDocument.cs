namespace M3LParser.Models;

public class M3LDocument
{
    public string Namespace { get; set; }
    public List<M3LModel> Models { get; set; } = new List<M3LModel>();
    public List<M3LEnum> Enums { get; set; } = new List<M3LEnum>();
    public List<M3LInterface> Interfaces { get; set; } = new List<M3LInterface>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
