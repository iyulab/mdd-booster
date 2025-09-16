using M3LParser.Helpers;

namespace M3LParser.Models;

public class M3LField
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsNullable { get; set; }
    public string Length { get; set; }
    public string Description { get; set; }
    public string DefaultValue { get; set; }
    public List<string> Attributes { get; set; } = new List<string>();
    public List<string> FrameworkAttributes { get; set; } = new List<string>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    public bool IsPrimaryKey => Attributes.Any(a => a.StartsWith("@primary"));
    public bool IsUnique => Attributes.Any(a => a.StartsWith("@unique"));
    public bool IsRequired => !IsNullable;
    public bool IsReference => Attributes.Any(a => RegexHelper.IsReferenceAttribute(a));
    public string ReferenceTarget => GetReferenceTarget();
    public string CascadeBehavior => GetCascadeBehavior();

    private string GetReferenceTarget()
    {
        var attribute = Attributes.FirstOrDefault(a => RegexHelper.IsReferenceAttribute(a));
        if (attribute == null) return null;

        return RegexHelper.ExtractReferenceParameter(attribute);
    }

    private string GetCascadeBehavior()
    {
        if (!IsReference) return null;
        var allAttributeText = string.Join(" ", Attributes);
        var cascadeBehavior = RegexHelper.ExtractCascadeBehavior(allAttributeText);
        return cascadeBehavior;
    }
}