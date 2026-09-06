using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Decides which foreign-key columns need an index that the model did not declare.
/// </summary>
/// <remarks>
/// Neither engine indexes a foreign key on its own, so a join or a delete that checks
/// referencing rows scans the child table. The gap is the same on both, which is why the
/// decision lives here rather than in either renderer.
/// <para>
/// One case is not quite dialect-neutral. A nullable column declared unique becomes a
/// filtered unique index on SQL Server (<c>WHERE [col] IS NOT NULL</c>), and the engine
/// can only use a filtered index where the query predicate implies the filter — so it
/// does not serve a general join the way PostgreSQL's plain unique index does. This
/// planner treats both as covered. The shape is narrow (a nullable unique foreign key
/// is an optional one-to-one) and the correction would be to make the planner
/// dialect-aware, which costs the single-decision property that keeps the two renderers
/// from drifting. Stated rather than silently assumed away.
/// </para>
/// <para>
/// Opt-in. Turning it on adds one index per un-covered foreign key, and on a model of
/// any size that is a lot of indexes to pay for on every write; the reader can be worth
/// it and can not, and that is not a judgment a generator should make silently for an
/// existing schema.
/// </para>
/// </remarks>
public static class ForeignKeyIndexPlanner
{
    /// <summary>
    /// Foreign-key fields whose column is not already indexed by something the model
    /// declares. Order follows the model's stored fields so output is stable.
    /// </summary>
    public static IReadOnlyList<FieldNode> Plan(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var fields = BaseColumns.StoredFields(model).ToList();
        var covered = LeadingColumnsOfDeclaredIndexes(model, fields);

        return fields
            .Where(f => !string.IsNullOrEmpty(
                MddBooster.Core.Ast.FieldAttributes.FirstArg(f, "reference")))
            .Where(f => !covered.Contains(f.Name))
            .ToList();
    }

    /// <summary>
    /// Column names an existing index already leads with. Leading position is what
    /// matters: a composite index on <c>(a, b)</c> serves a lookup on <c>a</c> alone,
    /// so adding a second index on <c>a</c> would only add write cost.
    /// </summary>
    private static HashSet<string> LeadingColumnsOfDeclaredIndexes(
        ResolvedModel model, IReadOnlyList<FieldNode> storedFields)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in storedFields)
        {
            if (MddBooster.Core.Ast.FieldAttributes.Has(f, "pk")
                || MddBooster.Core.Ast.FieldAttributes.Has(f, "unique")
                || MddBooster.Core.Ast.FieldAttributes.Has(f, "index"))
            {
                covered.Add(f.Name);
            }
        }

        foreach (var entry in SectionIndexParser.Parse(model))
        {
            covered.Add(entry.Columns[0]);
        }
        return covered;
    }
}
