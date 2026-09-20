using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// Build-output lines saying which models were extended, by whom, and by how many fields —
/// composition across source files is never silent.
/// </summary>
public static class ExtensionCoverage
{
    public static IReadOnlyList<string> Describe(IReadOnlyList<ResolvedModel> models)
    {
        var lines = new List<string>();
        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var bySource = new List<(string Owner, int Fields)>();
            foreach (var block in model.Source.ExtendedBy)
            {
                var owner = string.IsNullOrEmpty(block.Prefix) ? "(standard)" : block.Prefix;
                var at = bySource.FindIndex(x => x.Owner == owner);
                if (at < 0) bySource.Add((owner, block.Fields));
                else bySource[at] = (owner, bySource[at].Fields + block.Fields);
            }
            if (bySource.Count == 0) continue;
            lines.Add($"{model.Name} ← {string.Join(", ", bySource.Select(x => $"{x.Owner}({x.Fields})"))}");
        }
        return lines;
    }
}
