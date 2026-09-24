using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Finds the delete actions SQL Server refuses to create (error 1785) before any table is written.
/// <para>
/// The actions one delete starts must form a tree: from the deleted table, each <c>ON DELETE
/// CASCADE</c> deletes rows that start actions of their own, each <c>ON DELETE SET NULL</c> updates
/// rows and ends there — and no table may be reached twice, nor the table the delete started on.
/// A <c>@reference(X)?</c> self-reference is the shortest refused shape; two <c>?</c> keys to one
/// table, and a <c>?</c> beside the cascading key an <c>::aspect</c> synthesizes, are the others a
/// model meets. A model that is valid M3L and deploys on PostgreSQL can still be refused here — at
/// deployment, one constraint in, with the tables before it already created. The generator
/// reports it instead.
/// </para>
/// </summary>
public static class CascadePathDetector
{
    /// <summary>
    /// One key whose delete action acts on its own model's rows: deleting a <see cref="Principal"/>
    /// row changes or removes the <see cref="Dependent"/> rows that reference it.
    /// </summary>
    public sealed record CascadeEdge(string Principal, string Dependent, string Field, ReferentialAction Action)
    {
        /// <summary>Reads as <c>Order.referrer_id (SET NULL)</c>.</summary>
        public override string ToString() =>
            $"{Dependent}.{Field} ({(Action == ReferentialAction.Cascade ? "CASCADE" : "SET NULL")})";
    }

    /// <summary>
    /// What SQL Server would refuse. <see cref="Second"/> is <see langword="null"/> for a cycle —
    /// <see cref="First"/> then leads from a table back to itself; otherwise the two paths start at
    /// the same table and end at the same, different table.
    /// </summary>
    public sealed record CascadeConflict(IReadOnlyList<CascadeEdge> First, IReadOnlyList<CascadeEdge>? Second)
    {
        public string Describe()
        {
            static string Path(IReadOnlyList<CascadeEdge> edges) =>
                edges[0].Principal + string.Concat(edges.Select(e => $" -> {e.Dependent} via {e}"));

            return Second is null
                ? $"deleting a {First[0].Principal} row comes back to {First[0].Principal}: {Path(First)}"
                : $"deleting a {First[0].Principal} row reaches {First[^1].Dependent} twice: {Path(First)}; and {Path(Second)}";
        }
    }

    /// <summary>The first conflict found, or <see langword="null"/> when SQL Server accepts every key.</summary>
    public static CascadeConflict? Detect(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        var edges = new Dictionary<string, List<CascadeEdge>>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            foreach (var field in BaseColumns.StoredFields(model))
            {
                var target = MddBooster.Core.Ast.FieldAttributes.FirstArg(field, "reference");
                if (string.IsNullOrEmpty(target)) continue;
                var action = OnDeleteRule.For(model, field);
                if (action is not (ReferentialAction.Cascade or ReferentialAction.SetNull)) continue;

                if (!edges.TryGetValue(target!, out var list)) edges[target!] = list = [];
                list.Add(new CascadeEdge(target!, model.Name, field.Name, action.Value));
            }
        }

        // Every path of actions from each table: the first path to reach a table is kept, and a
        // second one — or one that reaches the table it started from — is the conflict. Where no
        // conflict exists the reachable part is a tree, so each walk visits it once.
        foreach (var start in edges.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var firstPath = new Dictionary<string, List<CascadeEdge>>(StringComparer.Ordinal);
            var conflict = Walk(start, start, [], edges, firstPath);
            if (conflict is not null) return conflict;
        }
        return null;
    }

    private static CascadeConflict? Walk(
        string start,
        string table,
        List<CascadeEdge> path,
        IReadOnlyDictionary<string, List<CascadeEdge>> edges,
        Dictionary<string, List<CascadeEdge>> firstPath)
    {
        if (!edges.TryGetValue(table, out var outgoing)) return null;

        foreach (var edge in outgoing)
        {
            var next = new List<CascadeEdge>(path) { edge };
            // Back to a table already on this path: a cycle, reported from where it closes — it
            // need not pass through the table the walk started from.
            if (edge.Dependent == start) return new CascadeConflict(next, null);
            var closes = next.FindIndex(e => e.Principal == edge.Dependent);
            if (closes > 0) return new CascadeConflict(next[closes..], null);
            if (firstPath.TryGetValue(edge.Dependent, out var earlier)) return new CascadeConflict(earlier, next);
            firstPath[edge.Dependent] = next;

            // SET NULL updates the referencing row and ends there; only a deleted row starts
            // actions of its own.
            if (edge.Action != ReferentialAction.Cascade) continue;
            var deeper = Walk(start, edge.Dependent, next, edges, firstPath);
            if (deeper is not null) return deeper;
        }
        return null;
    }
}
