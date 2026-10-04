using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The many-to-one navigations a model set's read types carry — one per ordinary
/// <c>@reference(Target)</c> key, pointing from the dependent's read type at the target's read type.
/// </summary>
/// <remarks>
/// <para>
/// A reference already gives the <em>write</em> entity a navigation (it is how EF orders inserts). The
/// read type is what the data API serves, and without a navigation there a reference cannot be
/// traversed by a client at all — a standard query client has no way to read a referenced row's
/// display values in the same request. This is the forward half only: the target gains nothing.
/// A collection on the target side opens a different surface (unbounded fan-out under
/// <c>$expand</c>) and is not implied by declaring a key.
/// </para>
/// <para>
/// <b>Which keys.</b> The ones the write entity already navigates
/// (<see cref="IsNavigableKey"/>), minus those that make the relationship one-to-one —
/// <c>@unique</c> or <c>@pk</c> — which <see cref="OneToOneRelationships"/> describes, with a navigation
/// on both ends. A key left off the read type (<c>@internal</c>) has nothing to hang a navigation on,
/// and a target outside the model set is already reported by the analyzer. A self-reference is kept:
/// a forward navigation needs no reverse name, which is the only thing that excluded it from the
/// one-to-one case.
/// </para>
/// <para>
/// <b>Names are derived, so they can collide, and a collision is reported rather than resolved.</b>
/// The name comes from the key the way the write side derives it (<c>owner_id</c> → <c>Owner</c>), and
/// the read type may already have a member by that name — a lookup field declared as <c>owner</c>, or
/// a one-to-one navigation. The declared member wins: it was written by the model's author, the
/// navigation was not. The navigation is left out and <see cref="Navigation.CollidesWith"/> says with
/// what, so the analyzer can warn (<c>MDD027</c>) instead of the generated class failing to compile.
/// </para>
/// </remarks>
public static class ManyToOneNavigations
{
    /// <param name="DependentModel">The model declaring the key.</param>
    /// <param name="TargetModel">The model it points at.</param>
    /// <param name="ForeignKeyField">The key field's name, as written in the model.</param>
    /// <param name="Name">The navigation's name on the dependent's read type.</param>
    /// <param name="IsOptional">Whether the key is nullable — the navigation follows it.</param>
    /// <param name="CollidesWith">
    /// When set, the read type already has a member with <paramref name="Name"/>; the value names it
    /// (a field as written in the model, or a one-to-one navigation). The navigation is not emitted.
    /// </param>
    public sealed record Navigation(
        string DependentModel,
        string TargetModel,
        string ForeignKeyField,
        string Name,
        bool IsOptional,
        string? CollidesWith);

    /// <summary>
    /// Whether a field is a reference key that carries a navigation: a <c>@reference</c> named by the
    /// <c>xxx_id</c> convention. The write entity's navigation follows the same rule — this is its one
    /// home, so the two sides cannot disagree about which keys are navigable.
    /// </summary>
    public static bool IsNavigableKey(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return !string.IsNullOrEmpty(FieldAttributes.FirstArg(field, "reference"))
            && field.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every many-to-one navigation in <paramref name="models"/>, in a stable order (dependent model,
    /// then field), with collisions against the read type's other members marked.
    /// </summary>
    /// <param name="oneToOne">
    /// The set's one-to-one relationships — their navigations are members of the same read types, so
    /// a name collision with one of them is as real as one with a field.
    /// </param>
    public static IReadOnlyList<Navigation> Describe(
        IReadOnlyList<ResolvedModel> models, IReadOnlyList<OneToOneRelationships.Relationship> oneToOne)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(oneToOne);

        var known = new HashSet<string>(models.Select(m => m.Name), StringComparer.Ordinal);
        var found = new List<Navigation>();

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            // Members the read type has besides these navigations: its fields, and the one-to-one
            // navigations on either end. A relationship whose reverse name conflicts emits nothing
            // and stops the build, so it occupies no name here.
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in model.Fields.Where(f => !EntitySurface.IsFieldInternal(f)))
                members.TryAdd(NameCasing.ToPascalCase(field.Name), field.Name);
            foreach (var r in oneToOne.Where(r => r.ReverseNameConflict is null))
            {
                if (r.DependentModel == model.Name)
                    members.TryAdd(r.ForwardName, $"1:1 {r.ForwardName} → {r.TargetModel}");
                if (r.TargetModel == model.Name)
                    members.TryAdd(r.ReverseName, $"1:1 {r.ReverseName} ← {r.DependentModel}");
            }

            foreach (var field in model.Fields)
            {
                if (field.Kind != FieldKind.Stored || !IsNavigableKey(field)) continue;
                if (FieldAttributes.Has(field, "unique") || FieldAttributes.Has(field, "pk")) continue;
                if (EntitySurface.IsFieldInternal(field)) continue;

                var target = FieldAttributes.FirstArg(field, "reference")!;
                if (!known.Contains(target)) continue;

                var name = OneToOneRelationships.ForwardNameFrom(field.Name);
                members.TryGetValue(name, out var clash);
                if (clash is null) members.Add(name, $"{field.Name} 의 navigation");

                found.Add(new Navigation(
                    DependentModel: model.Name,
                    TargetModel: target,
                    ForeignKeyField: field.Name,
                    Name: name,
                    IsOptional: field.Nullable,
                    CollidesWith: clash));
            }
        }

        return found;
    }
}
