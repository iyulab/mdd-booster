using M3L.Native;
using MddBooster.Core.Ast;

namespace MddBooster.Core.Generation;

/// <summary>
/// Where <c>@searchable</c> on a reference key lands: on the navigation that follows the key, not on
/// the key itself.
/// </summary>
/// <remarks>
/// <para>
/// A key is an identifier, and search compares text — a <c>[Searchable]</c> key matches no term. Worse,
/// the runtime reads any declaration as narrowing the type's search to the declared properties, so a
/// model that marked only its reference keys matched nothing at all. What such a declaration means is
/// «find this row by the referenced row's text», and the member that carries the referenced row is the
/// navigation. The runtime follows a <c>[Searchable]</c> navigation one step into the target's own
/// searched properties.
/// </para>
/// <para>
/// <b>Per class, by what that class has.</b> The read type's navigation exists when the key has a
/// many-to-one navigation that was not left out for a name collision, or is the forward side of a
/// one-to-one; the write type's when the key is navigable at all
/// (<see cref="ManyToOneNavigations.IsNavigableKey"/>). A class without one keeps the declaration on
/// the key, as before — dropping it would widen the type's search back to every string, the opposite
/// of what was declared — and the analyzer says the reference cannot be followed (<c>MDD034</c>).
/// </para>
/// </remarks>
public static class SearchableReferences
{
    /// <summary>Whether a field is a reference key declared <c>@searchable</c>.</summary>
    public static bool IsSearchableReference(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return FieldAttributes.Has(field, "searchable")
            && !string.IsNullOrEmpty(FieldAttributes.FirstArg(field, "reference"));
    }

    /// <summary>
    /// The keys of <paramref name="model"/> whose read type carries a navigation for them — the forward
    /// side of a one-to-one, or a many-to-one navigation that is emitted.
    /// </summary>
    public static IReadOnlySet<string> ReadNavigatedKeys(
        string model,
        IReadOnlyList<OneToOneRelationships.Relationship>? oneToOne,
        IReadOnlyList<ManyToOneNavigations.Navigation>? manyToOne)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in oneToOne ?? [])
            if (r.ReverseNameConflict is null && string.Equals(r.DependentModel, model, StringComparison.Ordinal))
                keys.Add(r.ForeignKeyField);
        foreach (var n in manyToOne ?? [])
            if (n.CollidesWith is null && string.Equals(n.DependentModel, model, StringComparison.Ordinal))
                keys.Add(n.ForeignKeyField);
        return keys;
    }
}
