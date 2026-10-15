using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The collection navigations a model set's read types carry — one per reverse entry a model declares for an ordinary
/// reference key pointing at it (<c>- &lt;comments</c> with <c>- target: Comment.post_id</c>, or
/// <c>@relation(comments, &lt;- Comment.post_id)</c>), on the referenced model's read type, holding the dependents'
/// read types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared, never implied.</b> A key gives its own model a navigation to the target; it does not give the target a
/// collection, because a collection opens a fan-out on the data API that the target's author did not ask for. The
/// reverse entry is that request, written on the target — the side that gains the member.
/// </para>
/// <para>
/// <b>Which entries.</b> A named reverse entry whose key is a stored, exposed <c>@reference</c> to this model and is
/// neither <c>@unique</c> nor <c>@pk</c> — those make a one-to-one, whose reverse side is a single navigation
/// (<see cref="OneToOneRelationships"/>). An entry that contradicts the keys is the parser's error and never reaches
/// here; one without its key is the analyzer's (<c>MDD050</c>).
/// </para>
/// <para>
/// <b>A collision is reported, not resolved</b> — the same rule as the forward side
/// (<see cref="ManyToOneNavigations"/>): a field, a one-to-one navigation or a many-to-one navigation of the read type
/// keeps its name, and the collection is left out with <see cref="Collection.CollidesWith"/> naming what it met.
/// </para>
/// </remarks>
public static class ReverseCollections
{
    /// <param name="PrincipalModel">The model declaring the entry — the read type that gains the collection.</param>
    /// <param name="DependentModel">The model holding the key.</param>
    /// <param name="ForeignKeyField">The key field's name, as written in the model.</param>
    /// <param name="Name">The collection's name on the principal's read type.</param>
    /// <param name="ForwardName">
    /// The key's many-to-one navigation on the dependent's read type, when one is generated — the collection's inverse.
    /// </param>
    /// <param name="CollidesWith">
    /// When set, the principal's read type already has a member named <paramref name="Name"/>; the value names it. The
    /// collection is not emitted.
    /// </param>
    public sealed record Collection(
        string PrincipalModel,
        string DependentModel,
        string ForeignKeyField,
        string Name,
        string? ForwardName,
        string? CollidesWith);

    /// <summary>Every declared reverse collection in <paramref name="models"/>, in a stable order.</summary>
    public static IReadOnlyList<Collection> Describe(
        IReadOnlyList<ResolvedModel> models,
        IReadOnlyList<OneToOneRelationships.Relationship> oneToOne,
        IReadOnlyList<ManyToOneNavigations.Navigation> manyToOne)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(oneToOne);
        ArgumentNullException.ThrowIfNull(manyToOne);

        var byName = models.GroupBy(m => m.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var found = new List<Collection>();

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in model.Fields.Where(f => !EntitySurface.IsFieldInternal(f)))
                members.TryAdd(NameCasing.ToPascalCase(field.Name), field.Name);
            foreach (var r in oneToOne.Where(r => r.ReverseNameConflict is null))
            {
                if (r.DependentModel == model.Name) members.TryAdd(r.ForwardName, $"1:1 {r.ForwardName} → {r.TargetModel}");
                if (r.TargetModel == model.Name) members.TryAdd(r.ReverseName, $"1:1 {r.ReverseName} ← {r.DependentModel}");
            }
            foreach (var n in manyToOne.Where(n => n.CollidesWith is null && n.DependentModel == model.Name))
                members.TryAdd(n.Name, $"{n.ForeignKeyField} 의 navigation");

            foreach (var entry in RelationDeclarations.Of(model.Source))
            {
                if (entry is not { Direction: "from", Name: { } declared, Target: { } dependent, Key: { } keyName }) continue;
                if (!byName.TryGetValue(dependent, out var dep)) continue;
                var key = dep.Fields.FirstOrDefault(f => f.Name == keyName);
                if (!IsCollectionKey(key, model.Name)) continue;

                var name = NameCasing.ToPascalCase(declared);
                members.TryGetValue(name, out var clash);
                if (clash is null) members.Add(name, $"{dependent}.{keyName} 의 컬렉션");

                var forward = manyToOne.FirstOrDefault(n => n.CollidesWith is null
                    && n.DependentModel == dependent && n.ForeignKeyField == keyName);
                found.Add(new Collection(model.Name, dependent, keyName, name, forward?.Name, clash));
            }
        }

        return found;
    }

    /// <summary>
    /// Whether <paramref name="key"/> is a key a reverse entry on <paramref name="principal"/> turns into a collection:
    /// a stored, exposed <c>@reference(principal)</c> that is not unique — the many side of a many-to-one.
    /// </summary>
    public static bool IsCollectionKey(FieldNode? key, string principal)
        => key is not null
           && key.Kind == FieldKind.Stored
           && FieldAttributes.FirstArg(key, "reference") == principal
           && !FieldAttributes.Has(key, "unique")
           && !FieldAttributes.Has(key, "pk")
           && !EntitySurface.IsFieldInternal(key);
}
