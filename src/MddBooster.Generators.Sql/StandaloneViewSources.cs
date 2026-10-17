using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>Which object a standalone view reads for one of its relations.</summary>
public enum StandaloneViewSourceKind
{
    /// <summary>The model's table.</summary>
    Table,

    /// <summary>The model's soft-delete view — the table without deleted rows.</summary>
    UdView,

    /// <summary>The model's FullView — needed when the view reads one of the model's derived columns.</summary>
    FullView,

    /// <summary>Another standalone view.</summary>
    View,
}

/// <summary>
/// What a standalone view reads, decided once for both dialects — the same questions the FullView
/// renderers ask of a rollup (does it read a derived column?) and of a source (does the model
/// soft-delete?), so a view sees the rows every other read surface sees.
/// </summary>
public static class StandaloneViewSources
{
    /// <summary>
    /// Every <c>Relation.column</c> the view reads: projected columns, join terms, group keys, and the columns its filter
    /// names (a bare name is the <c>from</c>'s) — a filter on a derived column reads it from the FullView too.
    /// </summary>
    public static IEnumerable<ViewColumnRef> ReferencedColumns(ResolvedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        foreach (var column in view.Columns.OfType<ViewFromColumn>()) yield return column.Source;
        foreach (var term in view.Joins.SelectMany(j => j.On))
        {
            yield return term.Left;
            yield return term.Right;
        }
        foreach (var key in view.GroupBy) yield return key;

        var relations = new[] { view.From }.Concat(view.Joins.Select(j => j.Target)).ToList();
        var filters = new[] { view.Where }.Concat(view.WhereRaw.Select(r => r.Expression));
        foreach (var name in filters.Where(f => !string.IsNullOrWhiteSpace(f)).SelectMany(f => PortableExpression.FieldNames(f!)))
        {
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            var relation = dot > 0 ? relations.FirstOrDefault(r => r.Name == name[..dot]) : view.From;
            var column = dot > 0 ? name[(dot + 1)..] : name;
            if (relation is not null && relation.HasColumn(column)) yield return new ViewColumnRef(relation, column);
        }
    }

    /// <summary>The object read for <paramref name="relation"/>.</summary>
    public static StandaloneViewSourceKind SourceOf(
        ResolvedView view, ViewRelation relation, IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(relation);
        if (relation.Kind == ViewRelationKind.View) return StandaloneViewSourceKind.View;

        var readsDerived = ReferencedColumns(view)
            .Where(r => r.Relation.Name == relation.Name)
            .Any(r => FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, relation.Name, NameCasing.ToPascalCase(r.Column)));
        if (readsDerived) return StandaloneViewSourceKind.FullView;
        return SoftDelete.IsEnabled(relation.Model!) ? StandaloneViewSourceKind.UdView : StandaloneViewSourceKind.Table;
    }

    /// <summary>Model → its own derived (Lookup/Rollup/Computed) column names, PascalCase — the map both FullView renderers use.</summary>
    internal static Dictionary<string, IReadOnlySet<string>> DerivedFieldsByModel(IEnumerable<ViewPlan> plans) =>
        plans
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);
}
