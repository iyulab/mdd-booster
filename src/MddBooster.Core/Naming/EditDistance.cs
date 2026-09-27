namespace MddBooster.Core.Naming;

/// <summary>
/// Edit distance for «did you mean …» suggestions. One implementation for every place that
/// suggests a known name for a misspelt one — there were two private copies of it before a third
/// caller needed it.
/// </summary>
public static class EditDistance
{
    /// <summary>Levenshtein distance, compared without regard to case.</summary>
    public static int Between(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// The candidate closest to <paramref name="name"/> within <paramref name="maxDistance"/>, or
    /// <c>null</c> when none is that close. Ties go to the earlier candidate.
    /// </summary>
    public static string? Nearest(string name, IEnumerable<string> candidates, int maxDistance)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var d = Between(name, candidate);
            if (d <= maxDistance && d < bestDistance)
            {
                best = candidate;
                bestDistance = d;
            }
        }
        return best;
    }
}
