namespace MDDBooster.Models;

public class FrameworkAttribute
{
    public string Name { get; set; }
    public List<string> Parameters { get; set; } = new List<string>();
    public string RawText { get; set; }

    public override string ToString()
    {
        return RawText;
    }
}
