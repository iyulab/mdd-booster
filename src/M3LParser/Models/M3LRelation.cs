namespace M3LParser.Models;

public class M3LRelation
{
    public string Name { get; set; }
    public string Target { get; set; }
    public string From { get; set; }
    public string Description { get; set; }
    public bool IsToOne { get; set; }
    public string OnDelete { get; set; }
    public string OnUpdate { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
