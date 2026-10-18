using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The dependency rule between layers of one model set: a declaration names only declarations of its own layer or of
/// the layers below it. What a declaration names: a <c>@reference</c> target (an <c>::aspect</c>'s base included — its
/// key is one), a field's enum, a <c>@lookup</c> hop, a <c>@rollup</c> source, a <c>### Relations</c> entry naming a
/// reverse end, and a view's relations, rollup sources and enums.
/// </summary>
/// <remarks>
/// The layers are a Model target's modules, and below all of them the sources the build references but does not
/// generate (<see cref="ReferenceSet"/>). A lower layer is compiled — or published — without the upper ones, so a
/// declaration naming one above it could not be built there.
/// <para>
/// A one-to-one key also gives its target a navigation back, which the target never declares. When the target is in a
/// lower layer that navigation is not generated (<see cref="HasInverse"/>) rather than refused — nothing in
/// the lower layer's text named the upper one. Declaring that end on the lower model does, and is refused.
/// </para>
/// </remarks>
public sealed class LayerDirection(
    Func<ResolvedModel, int> layerOfModel,
    Func<EnumNode, int> layerOfEnum,
    Func<ResolvedView, int> layerOfView,
    Func<int, string> layerName)
{
    /// <summary>Every upward name in the set, one line each — empty when the set keeps to the rule.</summary>
    public IReadOnlyList<string> Violations(IReadOnlyList<ResolvedModel> models, IReadOnlyList<EnumNode> enums, IReadOnlyList<ResolvedView> views)
    {
        var byName = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var enumByName = enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var problems = new List<string>();

        void Check(int from, string subject, string what, int to, string target)
        {
            if (to <= from) return;
            problems.Add($"{subject}({layerName(from)}) 가 {what} {target}({layerName(to)}) 을 가리킵니다 — 아래 층은 위 층을 모릅니다. "
                + $"그 선언을 {layerName(to)} 이상의 층으로 옮기거나, 관계를 위 층 쪽에서 선언하세요.");
        }

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var at = layerOfModel(model);
            foreach (var field in model.Fields)
            {
                var subject = $"{model.Name}.{field.Name}";
                if (LookupPath.ReferenceTarget(field) is { } target && byName.TryGetValue(target, out var t))
                    Check(at, subject, "참조", layerOfModel(t), $"모델 {t.Name}");
                if (enumByName.TryGetValue(field.Type ?? "", out var e))
                    Check(at, subject, "형", layerOfEnum(e), $"enum {e.Name}");
                if (field.Lookup is { } lookup)
                {
                    foreach (var hop in LookupPath.Walk(model, lookup.Path, models).Hops)
                        Check(at, subject, "lookup 경로로", layerOfModel(hop.Target), $"모델 {hop.Target.Name}");
                }
                if (field.Rollup is { } rollup && byName.TryGetValue(rollup.Target, out var source))
                    Check(at, subject, "rollup 원천으로", layerOfModel(source), $"모델 {source.Name}");
            }
        }

        // `### Relations` entries on a model that name another model's key: a reverse collection, or a one-to-one's
        // reverse name. The entry is the model's own text, so its target is a name the model's layer must know.
        var oneToOne = OneToOneRelationships.Describe(models);
        foreach (var r in oneToOne)
        {
            var target = byName[r.TargetModel];
            if (RelationDeclarations.ReverseName(target.Source, r.DependentModel, r.ForeignKeyField) is null) continue;
            Check(layerOfModel(target), $"{r.TargetModel} 의 ### Relations", "역방향 끝으로", layerOfModel(byName[r.DependentModel]), $"모델 {r.DependentModel}");
        }
        var manyToOne = ManyToOneNavigations.Describe(models, oneToOne);
        foreach (var c in ReverseCollections.Describe(models, oneToOne, manyToOne))
            Check(layerOfModel(byName[c.PrincipalModel]), $"{c.PrincipalModel}.{c.Name}(역방향 컬렉션)", "원소로", layerOfModel(byName[c.DependentModel]), $"모델 {c.DependentModel}");

        foreach (var view in views.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            var at = layerOfView(view);
            foreach (var relation in view.Joins.Select(j => j.Target).Prepend(view.From))
            {
                if (relation.Model is { } m) Check(at, $"뷰 {view.Name}", "읽기 대상으로", layerOfModel(m), $"모델 {m.Name}");
                if (relation.View is { } v) Check(at, $"뷰 {view.Name}", "읽기 대상으로", layerOfView(v), $"뷰 {v.Name}");
            }
            foreach (var column in view.Columns)
            {
                if (column is ViewRollupColumn rollup)
                    Check(at, $"뷰 {view.Name}.{column.Name}", "rollup 원천으로", layerOfModel(rollup.Target), $"모델 {rollup.Target.Name}");
                if (enumByName.TryGetValue(column.Field.Type ?? "", out var e))
                    Check(at, $"뷰 {view.Name}.{column.Name}", "형", layerOfEnum(e), $"enum {e.Name}");
            }
        }

        return problems.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Whether a one-to-one's target gets the inverse navigation: not when the dependent is in a layer above the
    /// target's (see the type's remarks). Passed to <see cref="OneToOneRelationships.Describe"/>, so a dropped inverse
    /// also takes no name on its target.
    /// </summary>
    public bool HasInverse(ResolvedModel dependent, ResolvedModel target) => layerOfModel(dependent) <= layerOfModel(target);

    /// <summary>The two layers of a build with references and no modules: the references below, everything else above.</summary>
    public static LayerDirection Of(ReferenceSet references) => new(
        m => references.Contains(m) ? 0 : 1,
        e => references.Contains(e) ? 0 : 1,
        v => references.Contains(v) ? 0 : 1,
        layer => layer == 0 ? "references" : "sources");
}
