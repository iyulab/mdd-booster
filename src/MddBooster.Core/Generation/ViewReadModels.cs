using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// Which standalone <c>::view</c> declarations are served as read models — an entity mapped to the view, a read-only
/// set, a controller — and which are only SQL.
/// </summary>
/// <remarks>
/// <para>
/// A set is addressed by key, so a view becomes a read model only when it has one: an output column named
/// <c>id</c>, typed <c>identifier</c>, projected from a column of one of its relations (<c>@from(Order.id)</c>). The
/// author chooses it, and it is theirs to keep unique — a view that joins a parent to its children repeats the
/// parent's key, and should project the child's.
/// </para>
/// <para>
/// A view with no such column is a legitimate view (an aggregate has no row identity to offer) and is still
/// generated as SQL; it simply has nothing a client could address a row by. <c>@internal</c> keeps a keyed view off
/// the read surface the same way it does a model.
/// </para>
/// </remarks>
public static class ViewReadModels
{
    /// <summary>The output column a view's rows are addressed by, or <c>null</c> when it has none.</summary>
    public static ViewFromColumn? KeyOf(ResolvedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Columns.OfType<ViewFromColumn>().FirstOrDefault(c =>
            string.Equals(c.Name, "id", StringComparison.Ordinal)
            && string.Equals(c.Field.Type, "identifier", StringComparison.Ordinal));
    }

    /// <summary>Whether the view carries <c>@internal</c>.</summary>
    public static bool IsInternal(ResolvedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (view.Source.Attributes ?? [])
            .Any(a => string.Equals(a.Name, "internal", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the view is served as a read model: it has a key and is not kept off the surface.</summary>
    public static bool IsReadModel(ResolvedView view) => KeyOf(view) is not null && !IsInternal(view);

    /// <summary>
    /// The read models whose generated names are already taken — reported, not resolved.
    /// </summary>
    /// <remarks>
    /// A read model derives a class (the view's name) and a set (its plural). A model derives a write class, a read
    /// class (<c>…Ext</c>) and a set for each; an enum derives a type. Two declarations may differ as written and
    /// still meet in what is generated — a view <c>ProductExt</c> beside a model <c>Product</c>, a view
    /// <c>Products</c> beside it — and the generated code would then declare one name twice.
    /// </remarks>
    public static IReadOnlyList<(ResolvedView View, string Reason)> NameClashes(
        IReadOnlyList<ResolvedView> readViews, IEnumerable<string> modelNames, IEnumerable<string> enumNames)
    {
        ArgumentNullException.ThrowIfNull(readViews);
        ArgumentNullException.ThrowIfNull(modelNames);
        ArgumentNullException.ThrowIfNull(enumNames);

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var sets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in modelNames)
        {
            var entity = NameCasing.ToPascalCase(model);
            var set = Pluralizer.Pluralize(entity);
            types.TryAdd(entity, $"모델 '{model}' 의 엔티티");
            types.TryAdd(entity + "Ext", $"모델 '{model}' 의 읽기 타입");
            sets.TryAdd(set, $"모델 '{model}' 의 셋");
            sets.TryAdd(set + "Ext", $"모델 '{model}' 의 읽기 셋");
        }
        foreach (var name in enumNames)
            types.TryAdd(NameCasing.ToPascalCase(name), $"enum '{name}'");

        var clashes = new List<(ResolvedView, string)>();
        foreach (var view in readViews.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            var entity = NameCasing.ToPascalCase(view.Name);
            var set = Pluralizer.Pluralize(entity);
            if (types.TryGetValue(entity, out var typeOwner))
                clashes.Add((view, $"클래스 '{entity}' 는 {typeOwner}와(과) 같은 이름이다"));
            else if (sets.TryGetValue(set, out var setOwner))
                clashes.Add((view, $"셋 '{set}' 는 {setOwner}와(과) 같은 이름이다"));
            else
            {
                types.Add(entity, $"뷰 '{view.Name}' 의 읽기 모델");
                sets.Add(set, $"뷰 '{view.Name}' 의 셋");
            }
        }
        return clashes;
    }
}
