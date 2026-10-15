using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;

namespace MddBooster.Core.Semantic;

/// <summary>
/// What a model's <c>### Relations</c> section (and its <c>@relation(…)</c> directives) says about the relationships
/// its <c>@reference</c> keys define — the one home for reading it, so every target names a navigation the same way.
/// </summary>
/// <remarks>
/// <para>
/// <c>@reference</c> defines a relationship; the section supplements it (specification §3.2.6). What this generator
/// takes from it is the <b>names</b>: a <c>to</c> entry (<c>- &gt;author</c> with <c>from: author_id</c>, or
/// <c>@relation(author, -&gt; Person, from: author_id)</c>) names the key's forward navigation, and a <c>from</c> entry on
/// the target (<c>- &lt;profile</c> with <c>target: Profile.owner_id</c>, or <c>@relation(profile, &lt;- Profile.owner_id)</c>)
/// names the reverse navigation of a one-to-one. A name is what the derivation from the key cannot always supply:
/// <c>owner</c> has no <c>_id</c> to strip — the derived name would be the key property's own — and a self-referencing
/// one-to-one has no reverse name to derive at all. The generator does not invent one; it reads the declared one.
/// </para>
/// <para>
/// An entry also carries an <c>on_delete</c> (§3.2.5), read with the key's own declarations by
/// <see cref="OnDeleteRule"/>. Everything else it can say — <c>load</c>, <c>order_by</c>, <c>on_update</c>, a reverse
/// collection, a many-to-many — no target generates, and <see cref="Unread"/> names it.
/// </para>
/// </remarks>
public static class RelationDeclarations
{
    /// <summary>One entry, normalized from either spelling.</summary>
    /// <param name="Direction"><c>to</c>, <c>from</c> or <c>many-to-many</c>.</param>
    /// <param name="Name">The entry's own name — the navigation it names — or <see langword="null"/>.</param>
    /// <param name="Target">The model the arrow names (for <c>from</c>: the model holding the key).</param>
    /// <param name="Key">The key field: on this model for <c>to</c>, on <see cref="Target"/> for <c>from</c>.</param>
    /// <param name="Keys">The entry's other keys (<c>on_delete</c>, <c>load</c>, …), lower-case.</param>
    /// <param name="InFields">Written among the fields rather than under <c>### Relations</c> (still a relationship — §3.2.4).</param>
    public sealed record Entry(
        string Direction,
        string? Name,
        string? Target,
        string? Key,
        IReadOnlyDictionary<string, string> Keys,
        string Raw,
        SourceLocation? Loc,
        bool InFields = false);

    /// <summary>Every entry of <paramref name="model"/>'s section, in source order.</summary>
    public static IReadOnlyList<Entry> Of(ModelNode model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var result = new List<Entry>();
        foreach (var element in model.Sections?.Relations ?? [])
            if (Read(element) is { } entry) result.Add(entry);
        return result;
    }

    /// <summary>
    /// The forward navigation name a <c>to</c> entry declares for <paramref name="key"/> on <paramref name="owner"/>
    /// (PascalCase), or <see langword="null"/> when none does.
    /// </summary>
    public static string? ForwardName(ModelNode owner, FieldNode key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Of(owner).FirstOrDefault(e => e.Direction == "to" && e.Name is not null && e.Key == key.Name) is { } e
            ? NameCasing.ToPascalCase(e.Name!)
            : null;
    }

    /// <summary>
    /// The reverse navigation name a <c>from</c> entry on <paramref name="target"/> declares for the key
    /// <paramref name="dependent"/>.<paramref name="key"/> (PascalCase), or <see langword="null"/>.
    /// </summary>
    public static string? ReverseName(ModelNode target, string dependent, string key)
        => Of(target).FirstOrDefault(e => e.Direction == "from" && e.Name is not null
                                          && e.Target == dependent && e.Key == key) is { } e
            ? NameCasing.ToPascalCase(e.Name!)
            : null;

    /// <summary>The <c>on_delete</c> a <c>to</c> entry declares for <paramref name="key"/>, or <see langword="null"/>.</summary>
    public static string? OnDelete(ModelNode owner, FieldNode key)
        => Of(owner).Where(e => e.Direction == "to" && e.Key == key.Name)
            .Select(e => e.Keys.GetValueOrDefault("on_delete"))
            .FirstOrDefault(v => v is not null);

    /// <summary>
    /// An entry that does not describe a relationship the model's keys define — the specification calls each of these
    /// an error (§3.2.6). Generating from one would name a navigation for a key that is not there, or for a different
    /// target than the key's.
    /// </summary>
    public static IEnumerable<(Entry Entry, string Problem)> Problems(
        ResolvedModel model, IReadOnlyDictionary<string, ResolvedModel> models)
    {
        foreach (var e in Of(model.Source))
        {
            if (e.Direction == "to")
            {
                if (e.Key is null) continue; // names no key: metadata only (reported as unread)
                var key = model.Fields.FirstOrDefault(f => f.Name == e.Key);
                var referenced = key is null ? null : FieldAttributes.FirstArg(key, "reference");
                if (referenced is null)
                    yield return (e, $"`from: {e.Key}` 은(는) 이 모델의 `@reference` 키가 아니다");
                else if (e.Target is not null && e.Target != referenced)
                    yield return (e, $"대상이 '{e.Target}' 인데 `{e.Key}` 의 `@reference` 는 '{referenced}' 를 가리킨다");
            }
            else if (e.Direction == "from" && e.Name is not null)
            {
                if (e.Target is null || e.Key is null)
                {
                    yield return (e, "역방향 항목은 키를 가진 모델과 그 키를 함께 적는다 — `target: Model.key_field`");
                    continue;
                }
                var key = models.TryGetValue(e.Target, out var dependent)
                    ? dependent.Fields.FirstOrDefault(f => f.Name == e.Key)
                    : null;
                if (key is null || FieldAttributes.FirstArg(key, "reference") != model.Name)
                    yield return (e, $"'{e.Target}.{e.Key}' 은(는) 이 모델('{model.Name}')을 가리키는 `@reference` 키가 아니다");
            }
        }
    }

    /// <summary>
    /// What no target generates from the section — every entry's unread part, as a line for the unconsumed-elements
    /// report. A <c>from</c> entry naming a one-to-one's reverse side is read; one naming the many side of a
    /// many-to-one (a collection) is not.
    /// </summary>
    public static IEnumerable<string> Unread(ModelNode model, IReadOnlyDictionary<string, ModelNode> models)
    {
        foreach (var e in Of(model))
        {
            var reads = e.Direction switch
            {
                "to" => e.Name is not null && e.Key is not null,
                "from" => e.Name is not null && e.Target is not null && e.Key is not null
                          && models.TryGetValue(e.Target, out var dep)
                          && dep.Fields.FirstOrDefault(f => f.Name == e.Key) is { } k
                          && (FieldAttributes.Has(k, "unique") || FieldAttributes.Has(k, "pk")),
                _ => false,
            };
            var where = e.InFields ? "관계 표기" : "### Relations";
            if (!reads)
            {
                var why = e switch
                {
                    { Direction: "many-to-many" } => "다대다는 생성하지 않는다",
                    { Name: null } => "navigation 이름이 없다",
                    { Direction: "to", Key: null } => "어느 키의 navigation 인지 `from: 키` 로 말하지 않는다",
                    { Direction: "from", Target: null } or { Direction: "from", Key: null }
                        => "키를 가진 모델과 그 키를 말하지 않는다 — `target: Model.key_field`",
                    { Direction: "from" } => "다대일의 역방향 컬렉션은 생성하지 않는다",
                    _ => "읽는 타깃이 없다",
                };
                yield return $"{model.Name}: {where} '{e.Raw}' ({why})";
            }
            var unreadKeys = e.Keys.Keys.Where(k => k != "on_delete").OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (reads && unreadKeys.Count > 0)
                yield return $"{model.Name}: {where} '{e.Raw}' 의 {string.Join(" · ", unreadKeys)}";
        }
    }

    // ---- reading ----

    /// <summary>The keys an entry carries that are its structure, not something it says about the relationship.</summary>
    private static readonly HashSet<string> Structural =
        new(StringComparer.Ordinal) { "direction", "name", "target", "from", "raw", "loc", "declaredIn", "type", "args", "cardinality" };

    private static Entry? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var raw = Str(element, "raw") ?? element.ToString();
        var loc = element.TryGetProperty("loc", out var l) ? ReadLoc(l) : null;

        // The directive spelling, `- @relation(name, <- Model.key)` / `- @relation(name, -> Target, from: key)`, arrives
        // with its arguments as written.
        if (Str(element, "type") == "directive")
        {
            if (!element.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array) return null;
            var parts = args.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString()! : a.ToString()).ToList();
            if (parts.Count < 2) return null;
            var arrow = parts[1].Trim();
            var from = parts.Skip(2).Select(p => p.Trim())
                .FirstOrDefault(p => p.StartsWith("from:", StringComparison.Ordinal))?["from:".Length..].Trim();
            if (arrow.StartsWith("<-", StringComparison.Ordinal))
            {
                var (t, k) = SplitQualified(arrow[2..].Trim());
                return new Entry("from", parts[0].Trim(), t, k ?? from, Empty, raw, loc);
            }
            if (arrow.StartsWith("->", StringComparison.Ordinal))
                return new Entry("to", parts[0].Trim(), arrow[2..].Trim(), from, Empty, raw, loc);
            return null;
        }

        var direction = Str(element, "direction");
        if (direction is null) return null;
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
            if (!Structural.Contains(p.Name))
                keys[p.Name.ToLowerInvariant()] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.ToString();

        var target = Str(element, "target");
        var key = Str(element, "from");
        var inFields = Str(element, "declaredIn") == "fields";
        // `- <comments` with `target: Comment.post_id` names the key inside the target.
        if (direction == "from" && target is not null && key is null)
            (target, key) = SplitQualified(target);
        return new Entry(direction, Str(element, "name"), target, key, keys, raw, loc, inFields);
    }

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static (string Model, string? Key) SplitQualified(string text)
    {
        var dot = text.IndexOf('.');
        return dot < 0 ? (text, null) : (text[..dot], text[(dot + 1)..]);
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static SourceLocation? ReadLoc(JsonElement l)
    {
        try { return l.Deserialize<SourceLocation>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return null; }
    }
}
