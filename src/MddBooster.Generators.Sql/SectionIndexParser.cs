using System.Text.Json;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// One index declared in a model's <c>### Indexes</c> section, as written.
/// </summary>
/// <param name="Columns">
/// Column names in declaration order, in the casing the model used. Leading position is
/// meaningful: a composite index on <c>(a, b)</c> also serves a lookup on <c>a</c>.
/// </param>
/// <param name="IsUnique">Whether the entry declared a uniqueness constraint.</param>
public sealed record SectionIndexEntry(IReadOnlyList<string> Columns, bool IsUnique);

/// <summary>
/// Reads a model's <c>### Indexes</c> section into column lists.
/// </summary>
/// <remarks>
/// Both dialect renderers and the foreign-key index planner need these entries, and each
/// needs something different from them — every column or only the leading one, the source
/// casing or a converted one. What none of them differ on is how an entry is <em>read</em>,
/// so that part lives here: an entry shape the upstream AST changes is then one edit, not
/// three, and the three cannot silently disagree about which declarations exist.
/// <para>
/// Column names come back exactly as the model wrote them. Converting here would mean
/// picking one dialect's casing and making the other convert back.
/// </para>
/// </remarks>
public static class SectionIndexParser
{
    /// <summary>
    /// Index entries the model declares, in declaration order. Entries that do not name a
    /// column are left out — an index needs columns, and a name alone cannot be rendered.
    /// </summary>
    public static IReadOnlyList<SectionIndexEntry> Parse(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var entries = model.Source.Sections?.Indexes;
        if (entries is null || entries.Count == 0) return [];

        var parsed = new List<SectionIndexEntry>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            // Anything that is not an index directive can share the section; skipping on
            // type keeps a stray entry from becoming an index named after nothing.
            if (!entry.TryGetProperty("type", out var typeProp)) continue;
            var type = typeProp.GetString();
            if (type != "directive" && type != "indexed") continue;

            if (!entry.TryGetProperty("args", out var argsEl)) continue;

            var columns = Columns(argsEl);
            if (columns.Count == 0) continue;

            var isUnique = entry.TryGetProperty("unique", out var uniqueProp)
                           && uniqueProp.ValueKind == JsonValueKind.True;
            parsed.Add(new SectionIndexEntry(columns, isUnique));
        }
        return parsed;
    }

    /// <summary>
    /// Column names carried by an entry's <c>args</c>. The parser emits an array today, and
    /// the pinned package is the only thing that produces an AST here, so the bare-string
    /// shape a single argument once took is not reachable from any supported version.
    /// It is still read, because the failure it guards against is invisible: an index whose
    /// <c>args</c> this method cannot decode yields no columns and is dropped, and dropped
    /// indexes are the one element the unconsumed-element accounting deliberately does not
    /// warn about — <c>### Indexes</c> is excluded from it precisely because it <em>is</em>
    /// consumed. Nothing else would report the loss.
    /// </summary>
    private static List<string> Columns(JsonElement argsEl)
    {
        var columns = new List<string>();

        if (argsEl.ValueKind == JsonValueKind.String)
        {
            var single = argsEl.GetString();
            if (!string.IsNullOrWhiteSpace(single)) columns.Add(single);
            return columns;
        }

        if (argsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in argsEl.EnumerateArray())
            {
                var column = arg.ValueKind == JsonValueKind.String
                    ? arg.GetString()
                    : arg.GetRawText();
                if (!string.IsNullOrWhiteSpace(column)) columns.Add(column);
            }
        }
        return columns;
    }
}
