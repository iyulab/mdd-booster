namespace M3LParser.Models;

public class M3LEnum
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<M3LEnumValue> Values { get; set; } = new List<M3LEnumValue>();
    public List<string> Inherits { get; set; } = new List<string>();
}
