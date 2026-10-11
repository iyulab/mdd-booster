using System.Text;

namespace MddBooster.Generators.Model;

/// <summary>
/// Writes a model's prose as a C# XML documentation summary — the text a consumer sees on hover.
/// </summary>
internal static class XmlDocComment
{
    /// <summary>
    /// Appends <c>/// &lt;summary&gt;</c> … <c>/// &lt;/summary&gt;</c> for <paramref name="text"/>, one
    /// <c>/// </c> line per source line, or nothing when the text is blank. Every line carries the prefix —
    /// a multi-line description written as one block left the second line outside the comment, which does
    /// not compile.
    /// </summary>
    public static void AppendSummary(StringBuilder sb, string? text, string indent)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.Append(indent).AppendLine("/// <summary>");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            sb.Append(indent).Append("/// ").AppendLine(Escape(line));
        sb.Append(indent).AppendLine("/// </summary>");
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
