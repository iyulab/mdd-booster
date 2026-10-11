using System.Text.Json;

namespace MddBooster.Core.Semantic;

/// <summary>
/// One index declared in a model's <c>### Indexes</c> section, as written.
/// </summary>
/// <param name="Columns">
/// Column names in declaration order, in the casing the model used. Leading position is
/// meaningful: a composite index on <c>(a, b)</c> also serves a lookup on <c>a</c>.
/// </param>
/// <param name="IsUnique">Whether the entry declared a uniqueness constraint.</param>
/// <param name="Nulls">
/// The declared NULL-handling policy for a unique entry (currently only
/// <c>"not_distinct"</c> — SQL:2023 <c>UNIQUE NULLS NOT DISTINCT</c>), or <see langword="null"/>
/// when the model didn't declare one. Absence means the pre-existing default: NULL is treated as
/// distinct, so a row carrying it is excluded from uniqueness enforcement (SQL Server's own
/// native behavior for a nullable unique column). Meaningless when <see cref="IsUnique"/> is
/// <see langword="false"/> — a plain index has no uniqueness to attach a NULL policy to.
/// </param>
/// <param name="Name">
/// The database name the model gives the index — an <c>### Indexes</c> item's name (specification §3.3.3, in either
/// item form), or a directive's <c>name:</c> — or <see langword="null"/> to have the generators name it from the
/// model and its columns. Not the text in parentheses after an item name: that is a description.
/// </param>
/// <param name="FullText">Whether the model asked for a full-text index (<c>@index(…, fulltext: true)</c>).</param>
public sealed record SectionIndexEntry(
    IReadOnlyList<string> Columns,
    bool IsUnique,
    string? Nulls = null,
    string? Name = null,
    bool FullText = false);

/// <summary>
/// Reads a model's <c>### Indexes</c> section into column lists.
/// </summary>
/// <remarks>
/// Both SQL dialect renderers, the foreign-key index planner, and the Model (EF) target's
/// <c>DbContextRenderer</c> need these entries, and each needs something different from
/// them — every column or only the leading one, the source casing or a converted one. What
/// none of them differ on is how an entry is <em>read</em>, so that part lives here: an
/// entry shape the upstream AST changes is then one edit, not four, and the four cannot
/// silently disagree about which declarations exist. It lives in <c>MddBooster.Core</c>
/// rather than the Sql project precisely because it is not a Sql-only concern — the Model
/// target has no reason to reference dialect-rendering code to read the same AST shape.
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
    /// <see cref="Unread"/> names the ones that were items, so the loss is not silent.
    /// </summary>
    public static IReadOnlyList<SectionIndexEntry> Parse(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var parsed = new List<SectionIndexEntry>();
        foreach (var entry in model.Source.Sections?.Indexes ?? [])
        {
            if (Read(entry) is { } index) parsed.Add(index);
        }
        return parsed;
    }

    /// <summary>
    /// The names of <c>### Indexes</c> items that yield no index — an item whose <c>fields:</c> is missing or empty,
    /// or whose directive names no column. <see cref="Parse"/> leaves them out, and <c>### Indexes</c> is the one
    /// section the unconsumed-element accounting does not cover (it is consumed), so this is the only report of them.
    /// </summary>
    public static IReadOnlyList<string> Unread(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var unread = new List<string>();
        foreach (var entry in model.Source.Sections?.Indexes ?? [])
        {
            if (entry.ValueKind != JsonValueKind.Object || Read(entry) is not null) continue;
            if (entry.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                unread.Add(name.GetString()!);
            }
        }
        return unread;
    }

    /// <summary>
    /// One entry as an index, or <see langword="null"/> when it is not one. The upstream AST carries three forms: a
    /// directive (<c>type: "directive"</c>) and an item with a directive (<c>type: "indexed"</c>) list their columns in
    /// <c>args</c>; an item written with sub-items (§3.3.3) carries them as <c>fields</c>, with no <c>type</c>. In all
    /// three, <c>name</c> is the index's name and <c>unique</c>/<c>nulls</c>/<c>fulltext</c> mean the same.
    /// </summary>
    private static SectionIndexEntry? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;

        JsonElement columnsEl;
        if (entry.TryGetProperty("type", out var typeProp))
        {
            // Anything that is not an index directive can share the section; skipping on
            // type keeps a stray entry from becoming an index named after nothing.
            var type = typeProp.GetString();
            if (type != "directive" && type != "indexed") return null;
            if (!entry.TryGetProperty("args", out columnsEl)) return null;
        }
        else if (!entry.TryGetProperty("fields", out columnsEl))
        {
            return null;
        }

        var columns = Columns(columnsEl);
        if (columns.Count == 0) return null;

        var isUnique = entry.TryGetProperty("unique", out var uniqueProp)
                       && uniqueProp.ValueKind == JsonValueKind.True;
        var nulls = entry.TryGetProperty("nulls", out var nullsProp)
                    && nullsProp.ValueKind == JsonValueKind.String
            ? nullsProp.GetString()
            : null;
        var name = entry.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(nameProp.GetString())
            ? nameProp.GetString()
            : null;
        var fullText = entry.TryGetProperty("fulltext", out var fullTextProp)
                       && fullTextProp.ValueKind == JsonValueKind.True;
        return new SectionIndexEntry(columns, isUnique, nulls, name, fullText);
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
