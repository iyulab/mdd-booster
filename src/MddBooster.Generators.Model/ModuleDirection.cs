using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// The dependency rule of a Model target split into modules: a module's declarations name only declarations of its
/// own module or of the modules below it. A lower module's types are compiled without the upper ones, so a model, view
/// or enum that names one above it would not compile there — and a layer that knows the layers built on it is not a
/// layer. What a declaration names: a <c>@reference</c> target (an <c>::aspect</c>'s base included — its key is one), a
/// field's enum, a <c>@lookup</c> hop, a <c>@rollup</c> source, a <c>### Relations</c> entry naming a reverse end, and a
/// view's relations.
/// </summary>
/// <remarks>
/// One relationship has an end the model does not write: a one-to-one key gives its target a navigation back to the
/// dependent, whether or not the target declares one. When the dependent is above the target that navigation would put
/// an upper type into the lower module, so it is not generated (<see cref="WithoutUpwardInverses"/>) — the relationship
/// is configured from the dependent's end, which is the end that declared it. Declaring that end on the lower model
/// (a <c>### Relations</c> entry) names the upper model, and is refused like any other upward name.
/// </remarks>
public static class ModuleDirection
{
    public static void Validate(
        ModelModules modules,
        IReadOnlyList<ResolvedModel> models,
        IReadOnlyList<EnumNode> enums,
        IReadOnlyList<ResolvedView> readViews)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var byName = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var enumByName = enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var problems = new List<string>();

        string Where(int module) => $"modules[{module}]";
        void Check(int from, string subject, string what, int to, string target)
        {
            if (to <= from) return;
            problems.Add($"{subject}({Where(from)}) 가 {what} {target}({Where(to)}) 을 가리킵니다 — 아래 모듈은 위 모듈을 모릅니다. "
                + $"그 선언을 {Where(to)} 이상의 모듈로 옮기거나, 관계를 위 모듈 쪽에서 선언하세요.");
        }

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var at = modules.IndexOf(model);
            foreach (var field in model.Fields)
            {
                var subject = $"{model.Name}.{field.Name}";
                if (LookupPath.ReferenceTarget(field) is { } target && byName.TryGetValue(target, out var t))
                    Check(at, subject, "참조", modules.IndexOf(t), $"모델 {t.Name}");
                if (enumByName.TryGetValue(field.Type ?? "", out var e))
                    Check(at, subject, "형", modules.IndexOf(e), $"enum {e.Name}");
                if (field.Lookup is { } lookup)
                {
                    foreach (var hop in LookupPath.Walk(model, lookup.Path, models).Hops)
                        Check(at, subject, "lookup 경로로", modules.IndexOf(hop.Target), $"모델 {hop.Target.Name}");
                }
                if (field.Rollup is { } rollup && byName.TryGetValue(rollup.Target, out var source))
                    Check(at, subject, "rollup 원천으로", modules.IndexOf(source), $"모델 {source.Name}");
            }
        }

        // `### Relations` entries on a model that name another model's key: a reverse collection, or a one-to-one's
        // reverse name. The entry is the model's own text, so its target is a name the model's module must know.
        var oneToOne = OneToOneRelationships.Describe(models);
        foreach (var r in oneToOne)
        {
            var target = byName[r.TargetModel];
            if (RelationDeclarations.ReverseName(target.Source, r.DependentModel, r.ForeignKeyField) is null) continue;
            Check(modules.IndexOf(target), $"{r.TargetModel} 의 ### Relations", "역방향 끝으로", modules.IndexOf(byName[r.DependentModel]), $"모델 {r.DependentModel}");
        }
        var manyToOne = ManyToOneNavigations.Describe(models, oneToOne);
        foreach (var c in ReverseCollections.Describe(models, oneToOne, manyToOne))
            Check(modules.IndexOf(byName[c.PrincipalModel]), $"{c.PrincipalModel}.{c.Name}(역방향 컬렉션)", "원소로", modules.IndexOf(byName[c.DependentModel]),
                $"모델 {c.DependentModel}");

        foreach (var view in readViews.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            var at = modules.IndexOf(view);
            foreach (var relation in view.Joins.Select(j => j.Target).Prepend(view.From))
            {
                if (relation.Model is { } m) Check(at, $"뷰 {view.Name}", "읽기 대상으로", modules.IndexOf(m), $"모델 {m.Name}");
                if (relation.View is { } v) Check(at, $"뷰 {view.Name}", "읽기 대상으로", modules.IndexOf(v), $"뷰 {v.Name}");
            }
            foreach (var column in view.Columns)
            {
                if (column is ViewRollupColumn rollup)
                    Check(at, $"뷰 {view.Name}.{column.Name}", "rollup 원천으로", modules.IndexOf(rollup.Target), $"모델 {rollup.Target.Name}");
                if (enumByName.TryGetValue(column.Field.Type ?? "", out var e))
                    Check(at, $"뷰 {view.Name}.{column.Name}", "형", modules.IndexOf(e), $"enum {e.Name}");
            }
        }

        if (problems.Count > 0)
            throw new GeneratorRefusalException(GeneratorRefusalKind.Model,
                "Model 타깃의 modules — 위 모듈을 가리키는 선언:\n  " + string.Join("\n  ", problems.Distinct(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The one-to-one relationships with the inverse navigation dropped where it would point up: a dependent in a module
    /// above its target's (see the type's remarks).
    /// </summary>
    public static IReadOnlyList<OneToOneRelationships.Relationship> WithoutUpwardInverses(
        IReadOnlyList<OneToOneRelationships.Relationship> oneToOne, ModelModules modules, IReadOnlyList<ResolvedModel> models)
    {
        var byName = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        return oneToOne
            .Select(r => modules.IndexOf(byName[r.DependentModel]) > modules.IndexOf(byName[r.TargetModel]) ? r with { HasInverse = false } : r)
            .ToList();
    }
}
