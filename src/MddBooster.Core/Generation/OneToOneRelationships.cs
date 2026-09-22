using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The one-to-one relationships a model set declares, named and checked once so that every target
/// that needs them reads the same answer.
/// </summary>
/// <remarks>
/// <para>
/// A one-to-one is declared without new syntax: <c>@reference(Target) @unique</c> on a field. Today
/// that produces a unique index and nothing else — the generated read types carry no navigation, so
/// nothing downstream can traverse the relationship. Deriving the names is the part with rules in it
/// (below), and the part where a model can be ambiguous, so it lives here as a unit that can be
/// tested on its own rather than inside a renderer where a naming bug and an emission bug look the
/// same.
/// </para>
/// <para>
/// <b>Naming.</b> The forward name comes from the foreign-key field the way the write side already
/// derives it: <c>owner_id</c> → <c>Owner</c>. The reverse name is the dependent model's own name
/// with the target's name removed from the front when it starts with it — <c>AssetMaintenanceProfile</c>
/// pointing at <c>Asset</c> becomes <c>MaintenanceProfile</c> — and the full model name otherwise,
/// because a name that does not start with the target's has nothing to strip and shortening it
/// further would be inventing one.
/// </para>
/// <para>
/// <b>Ambiguity is reported, never guessed.</b> Two references from the same model to the same target
/// would derive the same reverse name, and one would silently win. Both are returned with
/// <see cref="Relationship.ReverseNameConflict"/> set so the caller can refuse the build and say
/// which declarations collided — the same posture the accounting takes toward anything it cannot
/// read, and the outcome the design asks for.
/// </para>
/// </remarks>
public static class OneToOneRelationships
{
    /// <param name="DependentModel">The model declaring the foreign key.</param>
    /// <param name="TargetModel">The model it points at.</param>
    /// <param name="ForeignKeyField">The declaring field's name, as written in the model.</param>
    /// <param name="ForwardName">Navigation name on the dependent, pointing at the target.</param>
    /// <param name="ReverseName">Navigation name on the target, pointing back.</param>
    /// <param name="IsOptional">
    /// Whether the foreign key is nullable — an optional one-to-one. Carried here because the
    /// nullability of the navigation has to match the nullability of the key, and the caller would
    /// otherwise have to re-derive it from the field.
    /// </param>
    /// <param name="ReverseNameConflict">
    /// When set, another relationship in the same model set derives the same reverse name on the
    /// same target. The value is that other relationship's foreign-key field, so a diagnostic can
    /// name both sides.
    /// </param>
    public sealed record Relationship(
        string DependentModel,
        string TargetModel,
        string ForeignKeyField,
        string ForwardName,
        string ReverseName,
        bool IsOptional,
        string? ReverseNameConflict);

    /// <summary>
    /// Every <c>@reference(Target) @unique</c> field in <paramref name="models"/>, in a stable order
    /// (dependent model, then field), with names derived and reverse-name collisions marked.
    /// </summary>
    /// <remarks>
    /// A reference whose target is not in the model set is left out: the semantic analyzer already
    /// reports an unresolvable target, and repeating it here would give the same model two voices.
    /// </remarks>
    public static IReadOnlyList<Relationship> Describe(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        var known = new HashSet<string>(models.Select(m => m.Name), StringComparer.Ordinal);
        var found = new List<Relationship>();

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            foreach (var field in model.Fields)
            {
                // `@unique` is one way a model says "at most one of these per target". A primary key
                // that is also a foreign key says it more strongly — it cannot repeat at all — and
                // that is the shape `::aspect` produces. Reading only `@unique` would leave every
                // aspect without the navigation the relationship exists to provide.
                if (!FieldAttributes.Has(field, "unique") && !FieldAttributes.Has(field, "pk")) continue;

                var target = FieldAttributes.FirstArg(field, "reference");
                if (string.IsNullOrEmpty(target) || !known.Contains(target)) continue;

                // A self-reference would need a reverse name on the same type it starts from, and
                // the design does not say what that should be. Left out rather than named badly.
                if (string.Equals(target, model.Name, StringComparison.Ordinal)) continue;

                found.Add(new Relationship(
                    DependentModel: model.Name,
                    TargetModel: target,
                    ForeignKeyField: field.Name,
                    ForwardName: ForwardNameFrom(field.Name),
                    ReverseName: ReverseNameFor(model.Name, target, AspectNaming.PascalPrefix(model)),
                    IsOptional: field.Nullable,
                    ReverseNameConflict: null));
            }
        }

        return MarkReverseNameConflicts(found);
    }

    /// <summary>
    /// <c>owner_id</c> → <c>Owner</c>, matching how the write entity already names the navigation it
    /// emits for a foreign key, so the two sides of the same relationship do not end up spelled
    /// differently.
    /// </summary>
    public static string ForwardNameFrom(string foreignKeyField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foreignKeyField);
        var name = foreignKeyField.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
            ? foreignKeyField[..^3]
            : foreignKeyField;
        return NameCasing.ToPascalCase(name);
    }

    /// <summary>
    /// The dependent's name with the target's stripped from the front when it starts with it, and the
    /// full name otherwise. <c>AssetMaintenanceProfile</c> on <c>Asset</c> gives
    /// <c>MaintenanceProfile</c>; <c>Contractor</c> on <c>Organization</c> gives <c>Contractor</c>.
    /// </summary>
    /// <param name="pascalPrefix">
    /// The owning file's <c># Prefix:</c> in PascalCase, or empty. A prefixed file names its models
    /// <c>&lt;Prefix&gt;&lt;Target&gt;&lt;Rest&gt;</c>, and the prefix is the part that says who owns
    /// the declaration — stripping it along with the target would produce a navigation whose name no
    /// longer says that, and two owners extending the same target would then collide on a name
    /// neither of them wrote. <c>FsaAssetFacilityProfile</c> on <c>Asset</c> gives
    /// <c>FsaFacilityProfile</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This is the single home of the rule.</b> The same question is asked by the
    /// <c>::aspect</c> diagnostics, by the entity renderers, and by the EF configuration — a second
    /// implementation existed briefly and is gone, because two of them agree until the day one is
    /// changed and nothing says the other was not.
    /// </para>
    /// <para>
    /// Stripping leaves nothing when the two names are equal, and a navigation cannot be nameless —
    /// that case returns the full name too. It cannot arise from <see cref="Describe"/>, which
    /// excludes self-references, but this method is callable on its own.
    /// </para>
    /// </remarks>
    public static string ReverseNameFor(string dependentModel, string targetModel, string pascalPrefix = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependentModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModel);

        if (pascalPrefix.Length > 0)
        {
            var prefixed = pascalPrefix + targetModel;
            if (dependentModel.StartsWith(prefixed, StringComparison.Ordinal)
                && dependentModel.Length > prefixed.Length)
            {
                return pascalPrefix + dependentModel[prefixed.Length..];
            }
        }

        if (!dependentModel.StartsWith(targetModel, StringComparison.Ordinal)) return dependentModel;
        var rest = dependentModel[targetModel.Length..];
        return rest.Length == 0 ? dependentModel : rest;
    }

    private static IReadOnlyList<Relationship> MarkReverseNameConflicts(List<Relationship> found)
    {
        var result = new List<Relationship>(found.Count);
        foreach (var r in found)
        {
            var other = found.FirstOrDefault(o =>
                !ReferenceEquals(o, r)
                && string.Equals(o.TargetModel, r.TargetModel, StringComparison.Ordinal)
                && string.Equals(o.ReverseName, r.ReverseName, StringComparison.Ordinal));

            result.Add(other is null ? r : r with { ReverseNameConflict = other.ForeignKeyField });
        }
        return result;
    }

    /// <summary>
    /// Build-output lines, one per relationship, so a declared one-to-one is never invisible in a
    /// build log — the same reason <see cref="ExtensionCoverage"/> reports composition. A conflicting
    /// pair says so on its own line rather than being summarised away.
    /// </summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<Relationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(relationships);

        return relationships.Select(r => r.ReverseNameConflict is { } clash
            ? $"{r.DependentModel}.{r.ForeignKeyField} → {r.TargetModel} (1:1) — 역방향 이름 '{r.ReverseName}' 이 "
              + $"'{clash}' 와 충돌한다"
            : $"{r.DependentModel}.{r.ForeignKeyField} → {r.TargetModel} (1:1) — "
              + $"{r.DependentModel}.{r.ForwardName} / {r.TargetModel}.{r.ReverseName}"
              + (r.IsOptional ? " (선택적)" : "")).ToList();
    }
}
