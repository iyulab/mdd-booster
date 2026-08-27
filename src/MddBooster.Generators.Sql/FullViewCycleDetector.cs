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
    /// One hop of a reported cycle path. <see cref="Via"/> names the field and kind
    /// (<c>"Order.SupplyTotal rollup"</c>) that redirected the *previous* model's FullView to
    /// this one; it is <see langword="null"/> for the path's first entry, which has no incoming
    /// edge.
    /// </summary>
    public sealed record FullViewCycleStep(string Model, string? Via);

    /// <summary>
    /// Returns the cycle path (e.g. <c>Order -> OrderItem (via Order.SupplyTotal rollup) ->
    /// Order (via OrderItem.CustomerName lookup)</c>) if any model's FullView transitively
    /// references its own FullView through Lookup/Rollup redirection; <see langword="null"/>
    /// when the dependency graph is acyclic.
    /// </summary>
    public static IReadOnlyList<FullViewCycleStep>? Detect(
        IReadOnlyList<ViewPlan> plans,
        IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel)
    {
        var (edges, via) = BuildEdges(plans, derivedFieldsByModel);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var start in edges.Keys)
        {
            if (visited.Contains(start)) continue;
            var cycle = Walk(start, edges, visiting, visited, stack);
            if (cycle != null) return Attribute(cycle, via);
        }
        return null;
    }

    /// <summary>
    /// Pairs each hop after the first with the field/kind that created the edge into it, using
    /// the same (from, to) → description map <see cref="BuildEdges"/> recorded while building
    /// the graph itself — so the message can never name a field that didn't actually cause the
    /// edge it's attached to.
    /// </summary>
    private static IReadOnlyList<FullViewCycleStep> Attribute(
        IReadOnlyList<string> cycle,
        IReadOnlyDictionary<string, Dictionary<string, string>> via)
    {
        var steps = new List<FullViewCycleStep> { new(cycle[0], Via: null) };
        for (var i = 1; i < cycle.Count; i++)
        {
            var edgeVia = via.TryGetValue(cycle[i - 1], out var targets)
                && targets.TryGetValue(cycle[i], out var description)
                ? description
                : null;
            steps.Add(new FullViewCycleStep(cycle[i], edgeVia));
        }
        return steps;
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
    /// Builds the FullView dependency graph using exactly the same redirection rule
    /// <see cref="FullViewRenderer"/> applies when it actually renders a JOIN or subquery — so
    /// this graph can never disagree with what gets generated — alongside a parallel (from, to)
    /// → <c>"{Model}.{Field} lookup|rollup"</c> map recording which field created each edge, for
    /// <see cref="Attribute"/> to attach to the reported cycle path.
    /// </summary>
    private static (Dictionary<string, List<string>> Edges, Dictionary<string, Dictionary<string, string>> Via) BuildEdges(
        IReadOnlyList<ViewPlan> plans,
        IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var via = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var plan in plans)
        {
            if (!plan.NeedsFullView) continue;

            var targets = new List<string>();
            edges[plan.Model.Name] = targets;
            var targetVia = new Dictionary<string, string>(StringComparer.Ordinal);
            via[plan.Model.Name] = targetVia;

            foreach (var lookup in plan.Lookups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var path = lookup.Lookup?.Path;
                if (path is null) continue;
                var (fkField, targetColumn) = FullViewRenderer.ParsePath(path);
                var target = FullViewRenderer.ResolveReferenceTarget(plan.Model, fkField);
                var targetColumnPascal = NameCasing.ToPascalCase(targetColumn);
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, target, targetColumnPascal)
                    && !targets.Contains(target))
                {
                    targets.Add(target);
                    targetVia[target] = $"{plan.Model.Name}.{NameCasing.ToPascalCase(lookup.Name)} lookup";
                }
            }

            foreach (var rollup in plan.Rollups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var def = rollup.Rollup;
                if (def is null || string.IsNullOrEmpty(def.Field)) continue;
                var targetColumnPascal = NameCasing.ToPascalCase(def.Field);
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, def.Target, targetColumnPascal)
                    && !targets.Contains(def.Target))
                {
                    targets.Add(def.Target);
                    targetVia[def.Target] = $"{plan.Model.Name}.{NameCasing.ToPascalCase(rollup.Name)} rollup";
                }
            }
        }

        return (edges, via);
    }
}
