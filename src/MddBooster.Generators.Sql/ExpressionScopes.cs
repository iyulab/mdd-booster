using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// What a field reference in an expression names: the field, or why the expression's context has no such name.
/// </summary>
internal sealed record Resolved(FieldNode? Field, string? Error)
{
    internal static Resolved To(FieldNode field) => new(field, null);
    internal static Resolved Not(string why) => new(null, why);
}

/// <summary>
/// The names each expression context can read (M3L §10.7.1) — asked before either dialect writes a file, so a name the
/// context does not have stops the build instead of reaching the database as a column that is not there, or as one that
/// is there twice.
/// </summary>
internal static class ExpressionScopes
{
    private const string ParentPrefix = "$parent.";

    /// <summary>
    /// A model's <c>@computed</c> reads the model's own fields, by name. A computed field is evaluated after the ones
    /// declared before it (one layer each), so it reads those and not itself or a later one.
    /// </summary>
    internal static Func<string, Resolved> ModelComputed(ResolvedModel model, FieldNode computed) => name =>
    {
        if (name.Contains('.') || name.StartsWith('$'))
            return Resolved.Not($"'{name}' (a computed field reads its own model's fields, by name)");
        var index = IndexOf(model, name);
        if (index < 0) return Resolved.Not($"'{name}' is not a field of {model.Name}");
        var field = model.Fields[index];
        if (EntitySurface.IsFieldInternal(field)) return Internal(name);
        if (field.Computed is not null && index >= IndexOf(model, computed.Name))
            return Resolved.Not($"'{name}' is computed after '{computed.Name}' (a computed field reads the ones before it)");
        return Resolved.To(field);
    };

    /// <summary>
    /// A rollup's <c>where:</c> reads the rolled-up rows' fields — by name, or qualified by the rolled-up model's name — and
    /// <c>$parent.field</c> reads the row the rollup is computed for.
    /// </summary>
    /// <param name="parent">The row the rollup is computed for, or <c>null</c> when it is not a model.</param>
    internal static Func<string, Resolved> RollupWhere(ResolvedModel target, ResolvedModel? parent) => name =>
    {
        if (name.StartsWith(ParentPrefix, StringComparison.Ordinal))
        {
            var field = name[ParentPrefix.Length..];
            if (parent is null) return Resolved.Not($"'{name}' (the row the rollup is computed for is not a model)");
            return Field(parent, field) is { } f ? Resolved.To(f) : Resolved.Not($"'{field}' is not a field of {parent.Name}");
        }
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var qualifier = name[..dot];
            if (qualifier == "b")
                return Resolved.Not($"'{name}' names the generator's alias for the parent row (write $parent.{name[(dot + 1)..]})");
            if (qualifier != target.Name)
                return Resolved.Not($"'{name}' (a rollup's filter reads {target.Name}'s fields and $parent.field)");
            name = name[(dot + 1)..];
        }
        return Field(target, name) is { } t ? Resolved.To(t) : Resolved.Not($"'{name}' is not a field of {target.Name}");
    };

    /// <summary>
    /// A view's <c>where</c> filters the joined rows: <c>Relation.field</c> reads one of the view's relations, a bare name
    /// the view's <c>from</c>.
    /// </summary>
    internal static Func<string, Resolved> ViewWhere(ResolvedView view) => name =>
    {
        if (name.StartsWith('$')) return Resolved.Not($"'{name}' (a view's filter reads its relations' fields)");
        var (relation, column) = Split(view, name);
        if (relation is null) return Resolved.Not($"'{name[..name.IndexOf('.', StringComparison.Ordinal)]}' is not a relation of view {view.Name}");
        if (!relation.HasColumn(column)) return Resolved.Not($"'{column}' is not a column of {relation.Name}");
        var field = ColumnField(relation, column);
        return field is not null && relation.Kind == ViewRelationKind.Model && EntitySurface.IsFieldInternal(field)
            ? Internal(column)
            : new Resolved(field, null);
    };

    /// <summary>
    /// A view's <c>@computed</c> is evaluated over the view's own row: a bare name is one of the view's columns — a
    /// projected or rolled-up one, or a computed one declared before it — and <c>Relation.field</c> reads the column that
    /// projects it.
    /// </summary>
    internal static Func<string, Resolved> ViewComputed(ResolvedView view, ViewComputedColumn computed) => name =>
    {
        if (name.StartsWith('$')) return Resolved.Not($"'{name}' (a view's computed column reads the view's columns)");
        if (name.Contains('.'))
        {
            return ProjectionOf(view, name) is { } projecting
                ? Resolved.To(projecting.Field)
                : Resolved.Not($"'{name}' is not projected by view {view.Name} (a computed column reads the view's own columns — project it with @from)");
        }
        var columns = view.Columns.ToList();
        var index = columns.FindIndex(c => c.Name == name);
        if (index < 0) return Resolved.Not($"'{name}' is not a column of view {view.Name}");
        if (columns[index] is ViewComputedColumn && index >= columns.IndexOf(computed))
            return Resolved.Not($"'{name}' is computed after '{computed.Name}' (a computed column reads the ones before it)");
        return Resolved.To(columns[index].Field);
    };

    /// <summary>A field kept off the read surface: the views a model is read through do not carry it.</summary>
    private static Resolved Internal(string name)
        => Resolved.Not($"'{name}' is @internal (kept off the read surface the views are built from)");

    /// <summary>The view column that projects <c>Relation.field</c>, if the view projects it.</summary>
    internal static ViewFromColumn? ProjectionOf(ResolvedView view, string qualifiedName)
    {
        var dot = qualifiedName.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0) return null;
        var (relation, column) = (qualifiedName[..dot], qualifiedName[(dot + 1)..]);
        return view.Columns.OfType<ViewFromColumn>()
            .FirstOrDefault(c => c.Source.Relation.Name == relation && c.Source.Column == column);
    }

    private static (ViewRelation? Relation, string Column) Split(ResolvedView view, string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0) return (view.From, name);
        var relation = new[] { view.From }.Concat(view.Joins.Select(j => j.Target)).FirstOrDefault(r => r.Name == name[..dot]);
        return (relation, name[(dot + 1)..]);
    }

    private static FieldNode? ColumnField(ViewRelation relation, string column) => relation.Kind == ViewRelationKind.Model
        ? Field(relation.Model!, column)
        : relation.View!.Columns.FirstOrDefault(c => c.Name == column)?.Field;

    private static FieldNode? Field(ResolvedModel model, string name) => model.Fields.FirstOrDefault(f => f.Name == name);

    private static int IndexOf(ResolvedModel model, string name)
    {
        for (var i = 0; i < model.Fields.Count; i++)
            if (model.Fields[i].Name == name) return i;
        return -1;
    }
}
