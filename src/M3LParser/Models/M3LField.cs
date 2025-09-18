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
    public string InlineComment { get; set; }
    public List<string> Attributes { get; set; } = new List<string>();
    public List<string> FrameworkAttributes { get; set; } = new List<string>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    public bool IsPrimaryKey => Attributes.Any(a => a.StartsWith("@primary"));
    public bool IsUnique => Attributes.Any(a => a.StartsWith("@unique"));
    public bool IsRequired => !IsNullable;
    public bool IsReference => Attributes.Any(a => RegexHelper.IsReferenceAttribute(a));
    public string ReferenceTarget => GetReferenceTarget();
    public string CascadeBehavior => GetCascadeBehavior();
    public bool IsComputed => Attributes.Any(a => a.StartsWith("@computed"));
    public string ComputedExpression => GetComputedExpression();
    public bool IsPersisted => Attributes.Any(a => a.StartsWith("@persisted"));

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
        var cascadeBehavior = RegexHelper.ExtractCascadeBehavior(allAttributeText, IsNullable);
        return cascadeBehavior;
    }

    private string GetComputedExpression()
    {
        var computedAttribute = Attributes.FirstOrDefault(a => a.StartsWith("@computed"));
        if (computedAttribute == null) return null;

        // Find the opening parenthesis after @computed
        var startIndex = computedAttribute.IndexOf("@computed");
        if (startIndex == -1) return null;

        var parenIndex = computedAttribute.IndexOf('(', startIndex);
        if (parenIndex == -1) return null;

        // Find the matching closing parenthesis
        var openParens = 0;
        var closeParenIndex = -1;
        for (int i = parenIndex; i < computedAttribute.Length; i++)
        {
            if (computedAttribute[i] == '(') openParens++;
            else if (computedAttribute[i] == ')')
            {
                openParens--;
                if (openParens == 0)
                {
                    closeParenIndex = i;
                    break;
                }
            }
        }

        if (closeParenIndex == -1) return null;

        // Extract the content between parentheses
        var content = computedAttribute.Substring(parenIndex + 1, closeParenIndex - parenIndex - 1).Trim();

        // Remove outer quotes if present
        if (content.StartsWith("\"") && content.EndsWith("\""))
        {
            content = content.Substring(1, content.Length - 2);
        }

        return content;
    }
}