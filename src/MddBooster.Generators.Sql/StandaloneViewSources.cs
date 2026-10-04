using MddBooster.Core.Naming;

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
    /// <summary>Every <c>Relation.column</c> the view reads: projected columns, join terms, group keys.</summary>
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

    /// <summary>
    /// The views the PostgreSQL dialect does not generate, each with its reason: a view with an
    /// expression (<c>where</c>, <c>@computed</c> — the language does not define their dialect, and
    /// this dialect does not translate one), a view that reads a model through a view this dialect
    /// does not render (<see cref="Postgres.PostgresSqlGenerator.UnsupportedModels"/>), and a view
    /// that reads one of those views. The T-SQL dialect generates them all.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PostgresSkipped(
        IReadOnlyList<ResolvedView> views,
        IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(models);

        var planner = new ViewPlanner();
        var plans = models.Select(m => planner.Plan(m)).ToList();
        var derived = DerivedFieldsByModel(plans);
        var viewsNotRendered = Postgres.PostgresSqlGenerator.UnsupportedModels(plans, derived, warn: null);

        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var view in views) // dependency order: a view's sources were judged before it
        {
            if (view.HasExpressions)
            {
                skipped[view.Name] = "식(where·@computed)이 있다 — 언어가 식의 방언을 정하지 않았고 이 방언은 식을 번역하지 않는다";
                continue;
            }
            var relations = new[] { view.From }.Concat(view.Joins.Select(j => j.Target));
            foreach (var relation in relations)
            {
                if (relation.Kind == ViewRelationKind.View && skipped.ContainsKey(relation.Name))
                {
                    skipped[view.Name] = $"읽는 뷰 '{relation.Name}' 을(를) 이 방언에서 생성하지 않는다";
                    break;
                }
                if (relation.Kind == ViewRelationKind.Model
                    && viewsNotRendered.Contains(relation.Name)
                    && SourceOf(view, relation, derived) is StandaloneViewSourceKind.FullView or StandaloneViewSourceKind.UdView)
                {
                    skipped[view.Name] = $"모델 '{relation.Name}' 을(를) 그 모델의 뷰로 읽는데, 이 방언이 그 뷰를 생성하지 않는다(@computed·@indexed rollup)";
                    break;
                }
            }
        }
        return skipped;
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
