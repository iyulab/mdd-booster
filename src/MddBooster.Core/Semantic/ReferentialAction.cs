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
/// Two sources: the key <c>::aspect</c> synthesizes cascades (the aspect row exists only with its
/// base row), and a <c>@reference</c> may carry a symbol after its argument list (M3L §8.4.1):
/// <c>!</c> NO ACTION, <c>?</c> SET NULL, <c>!!</c> RESTRICT. A reference without a symbol
/// declares nothing and gets the database default.
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

        return Ast.FieldAttributes.Find(field, "reference")?.Cascade switch
        {
            "!" => ReferentialAction.NoAction,
            "?" => ReferentialAction.SetNull,
            "!!" => ReferentialAction.Restrict,
            _ => null,
        };
    }
}
