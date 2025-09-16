namespace M3LParser.Models;

public class M3LIndex
{
    public string Name { get; set; }
    public List<string> Fields { get; set; } = new List<string>();
    public bool IsUnique { get; set; }
    public string Description { get; set; }
    public bool IsFullText { get; set; }
}
