using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Classifies each resolved model by which SQL view layer(s) it needs.
/// <list type="bullet">
/// <item><description>
/// Any model with a <c>deleted_at</c> stored field needs a
/// <c>{Name}UdView</c> (UndeletedView) that filters <c>WHERE [DeletedAt] IS NULL</c>.
/// </description></item>
/// <item><description>
/// Any model with at least one <see cref="FieldKind.Lookup"/>, <see cref="FieldKind.Rollup"/>,
/// or <see cref="FieldKind.Computed"/> field needs a <c>{Name}FullView</c>.
/// When UdView exists, FullView is derived from UdView; otherwise from the base table.
/// </description></item>
/// <item><description>
/// <c>{Name}ExtView</c> is user-maintained (lives in <c>dbo/Views/</c>, not <c>dbo/Views_gen/</c>)
/// and is NOT generated here. It is detected by file scan in the consumers
/// (<see cref="MddBooster.Generators.Model.ModelGenerator"/>).
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

        var needsUd = model.Fields.Any(f =>
            f.Kind == FieldKind.Stored &&
            string.Equals(f.Name, "deleted_at", StringComparison.Ordinal));

        var needsFull = lookups.Count > 0 || rollups.Count > 0 || computeds.Count > 0;

        return new ViewPlan(
            Model: model,
            NeedsFullView: needsFull,
            NeedsUdView: needsUd,
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
/// whether to emit a UdView, a FullView, and exposes the classified
/// field groups so the renderers don't re-walk the model.
/// </summary>
public sealed record ViewPlan(
    ResolvedModel Model,
    bool NeedsFullView,
    bool NeedsUdView,
    IReadOnlyList<FieldNode> Lookups,
    IReadOnlyList<FieldNode> Rollups,
    IReadOnlyList<FieldNode> Computeds)
{
    public bool NeedsAnyView => NeedsFullView || NeedsUdView;
}
