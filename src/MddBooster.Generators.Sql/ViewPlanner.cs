using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Classifies each resolved model by which SQL view layer it needs. The
/// rules mirror design spec section 3.1 and Plan 5.1:
/// <list type="bullet">
/// <item><description>
/// Any model with at least one <see cref="FieldKind.Lookup"/> field needs a
/// <c>{Name}_full</c> view (base table LEFT JOINed onto every lookup target).
/// </description></item>
/// <item><description>
/// Any model with at least one <see cref="FieldKind.Rollup"/> or
/// <see cref="FieldKind.Computed"/> field needs a <c>{Name}_ext</c> view
/// layered on top of the full view.
/// </description></item>
/// <item><description>
/// Models with neither remain table-only — the Model generator still emits
/// an <c>XxxExt</c> read class backed directly by the base table.
/// </description></item>
/// </list>
/// This classification is a pure function of the AST; it has no side effects
/// and can be computed eagerly at the start of every build.
/// </summary>
public sealed class ViewPlanner
{
    public ViewPlan Plan(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lookups = model.Fields.Where(f => f.Kind == FieldKind.Lookup).ToList();
        var rollups = model.Fields.Where(f => f.Kind == FieldKind.Rollup).ToList();
        var computeds = model.Fields.Where(f => f.Kind == FieldKind.Computed).ToList();

        var needsFull = lookups.Count > 0;
        // Rollup aggregates and computed expressions can be materialized in the
        // _ext view even without a lookup layer, but the common case pairs
        // them — the ExtViewRenderer (Cycle 29/30) will add a no-op FROM on
        // the base table when no lookups exist.
        var needsExt = rollups.Count > 0 || computeds.Count > 0;

        return new ViewPlan(
            Model: model,
            NeedsFullView: needsFull,
            NeedsExtView: needsExt,
            Lookups: lookups,
            Rollups: rollups,
            Computeds: computeds);
    }

    public IReadOnlyList<ViewPlan> PlanAll(IEnumerable<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return models.Select(Plan).ToList();
    }
}

/// <summary>
/// Result of <see cref="ViewPlanner.Plan"/>. Tells downstream renderers
/// whether to emit a full view, an ext view, and exposes the classified
/// field groups so the renderers don't re-walk the model.
/// </summary>
public sealed record ViewPlan(
    ResolvedModel Model,
    bool NeedsFullView,
    bool NeedsExtView,
    IReadOnlyList<FieldNode> Lookups,
    IReadOnlyList<FieldNode> Rollups,
    IReadOnlyList<FieldNode> Computeds)
{
    public bool NeedsAnyView => NeedsFullView || NeedsExtView;
}
