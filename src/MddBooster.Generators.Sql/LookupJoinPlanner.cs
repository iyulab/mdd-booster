using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>One <c>LEFT JOIN</c> a FullView needs for its lookups.</summary>
/// <param name="Alias">The join's alias — <c>j_{fk}</c> for a first hop, <c>j_{fk}__{fk}</c> below it.</param>
/// <param name="ParentAlias">The alias the join's key is read from: <c>b</c> at the first hop.</param>
/// <param name="FkField">The key's m3l field name on the parent, un-cased.</param>
/// <param name="Target">The referenced model's name.</param>
/// <param name="UsesFullView">Whether the join reads the target's FullView rather than its table.</param>
/// <param name="Depth">1 for a first hop — joins are emitted in depth order so a parent precedes its children.</param>
internal sealed record LookupJoin(
    string Alias, string ParentAlias, string FkField, string Target, bool UsesFullView, int Depth);

/// <summary>A lookup field's projected column: <c>{JoinAlias}.{Column}</c> as <see cref="Field"/>.</summary>
internal sealed record LookupColumn(FieldNode Field, string JoinAlias, string Column);

/// <summary>
/// Plans the joins a FullView's lookups need, for both dialects: one join per path prefix, so
/// lookups that share a prefix share its joins.
/// </summary>
/// <remarks>
/// <para>
/// A one-hop lookup (<c>customer_id.name</c>) joins the target's table, or its FullView when the
/// column read is itself derived there — and when any lookup through that key needs the FullView,
/// every lookup through it reads from the FullView, since a join has one source. That is the rule
/// this type inherited unchanged; a model with only one-hop lookups renders exactly as before.
/// </para>
/// <para>
/// A multi-hop lookup (<c>order_id.customer_id.name</c>) chains a join per hop. The hops before the
/// last join the target's <b>table</b>: they only need the next key, which is a stored column, so
/// they add no dependency on another view — that is what lets a multi-hop path reach a column a
/// one-hop lookup could only reach through a FullView that closes a cycle. The last hop follows the
/// one-hop rule. When the same prefix is also the end of a lookup that needs the FullView, the
/// intermediate join reuses it — the dependency already exists — unless the key it must read is
/// field-internal and so absent from that FullView, in which case it gets its own table join.
/// </para>
/// </remarks>
internal static class LookupJoinPlanner
{
    public static (IReadOnlyList<LookupJoin> Joins, IReadOnlyList<LookupColumn> Columns) Plan(
        ResolvedModel owner,
        IEnumerable<FieldNode> lookups,
        Func<string, ResolvedModel?> findModel,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? derivedFieldsByModel)
    {
        var chains = lookups.Select(lookup => (lookup, chain: Resolve(owner, lookup, findModel))).ToList();

        // Terminal prefixes first: a join's source is one choice for every lookup ending there.
        var terminalUsesFullView = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (_, chain) in chains)
        {
            var key = Prefix(chain.Hops, chain.Hops.Count);
            var needs = FullViewRenderer.IsDerivedColumn(
                derivedFieldsByModel, chain.Hops[^1].Target, NameCasing.ToPascalCase(chain.Column));
            terminalUsesFullView[key] = terminalUsesFullView.TryGetValue(key, out var prior) ? prior || needs : needs;
        }

        var joins = new Dictionary<(string Prefix, bool FullView), LookupJoin>();
        var columns = new List<LookupColumn>();
        foreach (var (lookup, chain) in chains)
        {
            var parentAlias = "b";
            for (var depth = 1; depth <= chain.Hops.Count; depth++)
            {
                var hop = chain.Hops[depth - 1];
                var prefix = Prefix(chain.Hops, depth);
                var isLast = depth == chain.Hops.Count;
                var fullView = terminalUsesFullView.TryGetValue(prefix, out var tv) && tv;

                if (!isLast && fullView && chain.Hops[depth].FkInternal)
                    fullView = false;

                var alias = "j_" + prefix.Replace(".", "__", StringComparison.Ordinal);
                if (!fullView && terminalUsesFullView.TryGetValue(prefix, out var other) && other)
                    alias += "_base";

                if (!joins.TryGetValue((prefix, fullView), out var join))
                {
                    join = new LookupJoin(alias, parentAlias, hop.FkField, hop.Target, fullView, depth);
                    joins[(prefix, fullView)] = join;
                }
                parentAlias = join.Alias;
            }
            columns.Add(new LookupColumn(lookup, parentAlias, chain.Column));
        }

        var ordered = joins.Values
            .OrderBy(j => j.Depth)
            .ThenBy(j => j.Alias, StringComparer.Ordinal)
            .ToList();
        return (ordered, columns);
    }

    /// <summary>
    /// The models whose FullView <paramref name="owner"/>'s lookups read, each with the lookup that
    /// first needs it — the same joins <see cref="Plan"/> renders, so a dependency the cycle check
    /// counts is exactly one the view text contains.
    /// </summary>
    public static IReadOnlyList<(string Target, FieldNode Via)> FullViewDependencies(
        ResolvedModel owner,
        IEnumerable<FieldNode> lookups,
        Func<string, ResolvedModel?> findModel,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? derivedFieldsByModel)
    {
        var (joins, columns) = Plan(owner, lookups, findModel, derivedFieldsByModel);
        var byAlias = joins.ToDictionary(j => j.Alias, StringComparer.Ordinal);
        var result = new List<(string Target, FieldNode Via)>();
        foreach (var column in columns)
        {
            var join = byAlias[column.JoinAlias];
            if (join.UsesFullView && result.All(r => r.Target != join.Target))
                result.Add((join.Target, column.Field));
        }
        return result;
    }

    /// <summary>One hop as the SQL side needs it: the key's name, the model it references, and whether the key is internal.</summary>
    internal sealed record Hop(string FkField, string Target, bool FkInternal);

    /// <summary>A lookup path resolved to its hops and final column.</summary>
    internal sealed record Chain(IReadOnlyList<Hop> Hops, string Column);

    /// <summary>
    /// Resolves <paramref name="lookup"/>'s path. The first hop needs only <paramref name="owner"/>;
    /// every later hop needs the model the previous one references, from <paramref name="findModel"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The path is malformed or a hop does not resolve —
    /// the analyzer reports these as MDD003–MDD006 before generation, so reaching here is a bug.</exception>
    internal static Chain Resolve(ResolvedModel owner, FieldNode lookup, Func<string, ResolvedModel?> findModel)
    {
        var path = lookup.Lookup?.Path
            ?? throw new InvalidOperationException($"Lookup field '{owner.Name}.{lookup.Name}' has no parsed LookupDef.");
        if (!LookupPath.TrySplit(path, out var fkSegments, out var column))
            throw new InvalidOperationException($"Invalid lookup path '{path}'. Expected 'fk.column' or 'fk.fk.column'.");

        var hops = new List<Hop>(fkSegments.Count);
        var current = owner;
        for (var i = 0; i < fkSegments.Count; i++)
        {
            var fkField = current.Fields.FirstOrDefault(f => string.Equals(f.Name, fkSegments[i], StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Model '{current.Name}' has no field '{fkSegments[i]}' (referenced by lookup path '{path}').");
            var target = LookupPath.ReferenceTarget(fkField)
                ?? throw new InvalidOperationException(
                    $"Lookup FK '{current.Name}.{fkSegments[i]}' is missing an @reference(Target) attribute.");
            hops.Add(new Hop(fkField.Name, target, EntitySurface.IsFieldInternal(fkField)));

            if (i < fkSegments.Count - 1)
                current = findModel(target)
                    ?? throw new InvalidOperationException(
                        $"Lookup path '{path}' passes through model '{target}', which is not among the models being generated.");
        }
        return new Chain(hops, column);
    }

    private static string Prefix(IReadOnlyList<Hop> hops, int depth)
        => string.Join('.', hops.Take(depth).Select(h => h.FkField));
}
