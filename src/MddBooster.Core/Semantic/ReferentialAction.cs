using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>What happens to a referencing row when the row it references is deleted.</summary>
public enum ReferentialAction
{
    Cascade,
    SetNull,
    NoAction,
    Restrict,
}

/// <summary>
/// The <c>ON DELETE</c> action a foreign key declares — one answer for every target that emits
/// the key.
/// <para>
/// Three sources: the key <c>::aspect</c> synthesizes cascades (the aspect row exists only with its
/// base row); a <c>@reference</c> may carry a symbol after its argument list (M3L §8.4.1):
/// <c>!</c> NO ACTION, <c>?</c> SET NULL, <c>!!</c> RESTRICT; and a key may name its action —
/// <c>- on_delete: cascade</c> in the extended field format, or <c>@on_delete(cascade)</c> (M3L §3.2,
/// §10.8: <c>cascade</c>, <c>set_null</c>, <c>restrict</c>, <c>no_action</c>). A key that declares
/// nothing gets the database default. A symbol and an <c>on_delete</c> that disagree, or an action the
/// language does not define, is <see cref="Problem"/> — the analyzer refuses the build (<c>MDD046</c>).
/// </para>
/// </summary>
public static class OnDeleteRule
{
    /// <summary>The declared action, or <see langword="null"/> when the key declares none.</summary>
    public static ReferentialAction? For(ResolvedModel owner, FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(field);

        if (owner.AspectBase is { } aspectBase && field.Name == AspectNaming.KeyFieldName(aspectBase))
            return ReferentialAction.Cascade;

        return BySymbol(field) ?? ByName(owner, field);
    }

    /// <summary>
    /// What is wrong with the key's declaration — an <c>on_delete</c> action the language does not define, or one
    /// that disagrees with the symbol after <c>@reference</c> — or <see langword="null"/>.
    /// </summary>
    public static string? Problem(ResolvedModel owner, FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(field);
        if (OnDeleteName(owner, field) is not { } name) return null;
        if (Parse(name) is not { } named)
            return $"on_delete \"{name}\" 은(는) 정의된 삭제 동작이 아니다 — cascade · set_null · restrict · no_action 중 하나.";
        if (BySymbol(field) is { } symbol && symbol != named)
            return $"@reference 의 기호는 {Spell(symbol)} 인데 on_delete 는 {Spell(named)} 다 — 하나만 쓰거나 둘을 맞춘다.";
        return null;
    }

    private static ReferentialAction? BySymbol(FieldNode field) => Ast.FieldAttributes.Find(field, "reference")?.Cascade switch
    {
        "!" => ReferentialAction.NoAction,
        "?" => ReferentialAction.SetNull,
        "!!" => ReferentialAction.Restrict,
        _ => null,
    };

    private static ReferentialAction? ByName(ResolvedModel owner, FieldNode field)
        => OnDeleteName(owner, field) is { } name ? Parse(name) : null;

    // The key's own declaration (the extended form or @on_delete), else the ### Relations entry that names the key
    // (§3.2.5 — `- on_delete: restrict` under `- >author` / `- from: author_id`).
    private static string? OnDeleteName(ResolvedModel owner, FieldNode field)
        => Ast.FieldAttributes.FirstArg(field, "on_delete") ?? RelationDeclarations.OnDelete(owner.Source, field);

    private static ReferentialAction? Parse(string name) => name.Trim().ToLowerInvariant() switch
    {
        "cascade" => ReferentialAction.Cascade,
        "set_null" => ReferentialAction.SetNull,
        "restrict" => ReferentialAction.Restrict,
        "no_action" => ReferentialAction.NoAction,
        _ => null,
    };

    private static string Spell(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.Restrict => "RESTRICT",
        _ => "NO ACTION",
    };
}
