using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Detects circular <c>{Model}FullView</c> references before any view is rendered.
/// <para>
/// <see cref="FullViewRenderer"/> decides, one model at a time, whether a Lookup's JOIN or a
/// Rollup's subquery must target the other model's FullView (<see cref="FullViewRenderer.
/// IsDerivedColumn"/>). Two independent per-model decisions can still add up to a cycle across
/// models — e.g. model A rolls up an aggregate field that is itself derived on model B, while B
/// chains a Lookup through a derived field on A. Each of A's and B's views renders without error
/// on its own; only together do they form <c>AFullView ⇄ BFullView</c>, a shape SQL Server refuses
/// to deploy (SQL72009). <c>mdd build</c> previously had no way to see this, so it only surfaced
/// once a declarative schema tool tried to deploy the generated views (docket #101).
/// </para>
/// </summary>
public static class FullViewCycleDetector
{
    /// <summary>
    /// Returns the model-name cycle path (e.g. <c>["Order", "OrderItem", "Order"]</c>) if any
    /// model's FullView transitively references its own FullView through Lookup/Rollup
    /// redirection; <see langword="null"/> when the dependency graph is acyclic.
    /// </summary>
    public static IReadOnlyList<string>? Detect(
        IReadOnlyList<ViewPlan> plans,
        IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel)
    {
        var edges = BuildEdges(plans, derivedFieldsByModel);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var start in edges.Keys)
        {
            if (visited.Contains(start)) continue;
            var cycle = Walk(start, edges, visiting, visited, stack);
            if (cycle != null) return cycle;
        }
        return null;
    }

    private static IReadOnlyList<string>? Walk(
        string node,
        IReadOnlyDictionary<string, List<string>> edges,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> stack)
    {
        visiting.Add(node);
        stack.Add(node);

        if (edges.TryGetValue(node, out var targets))
        {
            foreach (var target in targets)
            {
                if (visiting.Contains(target))
                {
                    var cycleStart = stack.IndexOf(target);
                    return stack.Skip(cycleStart).Append(target).ToArray();
                }
                if (!visited.Contains(target))
                {
                    var found = Walk(target, edges, visiting, visited, stack);
                    if (found != null) return found;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        visiting.Remove(node);
        visited.Add(node);
        return null;
    }

    /// <summary>
    /// model name → the *other* FullView model names it references, using exactly the same
    /// redirection rule <see cref="FullViewRenderer"/> applies when it actually renders a JOIN
    /// or subquery — so this graph can never disagree with what gets generated.
    /// </summary>
    private static Dictionary<string, List<string>> BuildEdges(
        IReadOnlyList<ViewPlan> plans,
        IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var plan in plans)
        {
            if (!plan.NeedsFullView) continue;

            var targets = new List<string>();
            edges[plan.Model.Name] = targets;

            foreach (var lookup in plan.Lookups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var path = lookup.Lookup?.Path;
                if (path is null) continue;
                var (fkField, targetColumn) = FullViewRenderer.ParsePath(path);
                var target = FullViewRenderer.ResolveReferenceTarget(plan.Model, fkField);
                var targetColumnPascal = NameCasing.ToPascalCase(targetColumn);
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, target, targetColumnPascal)
                    && !targets.Contains(target))
                    targets.Add(target);
            }

            foreach (var rollup in plan.Rollups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var def = rollup.Rollup;
                if (def is null || string.IsNullOrEmpty(def.Field)) continue;
                var targetColumnPascal = NameCasing.ToPascalCase(def.Field);
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, def.Target, targetColumnPascal)
                    && !targets.Contains(def.Target))
                    targets.Add(def.Target);
            }
        }

        return edges;
    }
}
