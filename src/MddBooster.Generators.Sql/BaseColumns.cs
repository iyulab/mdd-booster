using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// Single source of truth for a model's base-table (physical) columns.
/// <para>
/// <see cref="TableRenderer"/> emits these columns; the view renderers
/// (<see cref="UdViewRenderer"/>, <see cref="FullViewRenderer"/>) must project
/// exactly the same set. Deriving both from here guarantees a view never lists
/// a column the table lacks.
/// </para>
/// <para>
/// <b>Why explicit columns, not <c>SELECT *</c></b>: a <c>SELECT *</c> view
/// freezes its column list at creation and does <i>not</i> auto-expand when the
/// base table gains a column — it must be re-created or <c>sp_refreshview</c>'d.
/// Under SSDT/sqlpackage the generated post-deployment refresh script masked
/// this, but a declarative schema tool that diffs the generated SQL <i>text</i>
/// (e.g. Schemorph) correctly sees "no change" for an unchanged <c>SELECT *</c>
/// view and leaves it silently stale. Projecting the base columns by name makes
/// an added column change the generated view text, so the declarative tool
/// detects it and re-defines the view. See the mdd-booster issue
/// "explicit-column-views-for-declarative-diff".
/// </para>
/// </summary>
internal static class BaseColumns
{
    /// <summary>Stored (physical) fields in declaration order.</summary>
    public static IReadOnlyList<FieldNode> StoredFields(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Fields.Where(f => f.Kind == FieldKind.Stored).ToList();
    }

    /// <summary>PascalCase column names in declaration order.</summary>
    public static IReadOnlyList<string> Names(ResolvedModel model) =>
        StoredFields(model).Select(f => ToPascalCase(f.Name)).ToList();

    /// <summary>
    /// Explicit projection replacing <c>alias.*</c>:
    /// <c>alias.[Col1], alias.[Col2], …</c>. When <paramref name="alias"/> is
    /// null/empty the columns are emitted unqualified (<c>[Col1], [Col2], …</c>).
    /// </summary>
    public static string Projection(ResolvedModel model, string? alias = null)
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        return string.Join(", ", Names(model).Select(c => prefix + "[" + c + "]"));
    }

    internal static string ToPascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
