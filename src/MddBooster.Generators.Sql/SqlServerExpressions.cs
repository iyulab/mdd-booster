using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// How an M3L expression reaches SQL Server: the one declared for the platform as written, the portable one translated
/// (M3L §10.7) — one place for the rule the full views, rollup filters and standalone views share. What the dialect
/// cannot have stops the build before this is asked (<see cref="ExpressionRefusals"/>).
/// </summary>
internal static class SqlServerExpressions
{
    /// <summary>
    /// The SQL Server text for <paramref name="selected"/>. A platform expression is emitted as written, its field names
    /// spelled as columns; a portable expression is translated.
    /// </summary>
    /// <param name="column">Spells a field reference as a column in the query the expression sits in.</param>
    /// <param name="booleanResult">The expression is a boolean field's value (see <see cref="PortableExpression"/>).</param>
    /// <remarks>
    /// Callers render only after <see cref="ExpressionRefusals.Refuse"/> let the model through, so an untranslatable
    /// portable expression here is a programming error, not a model one.
    /// </remarks>
    internal static string Render(SelectedExpression selected, Func<string, string> column, bool booleanResult)
    {
        if (selected.IsRaw) return FullViewRenderer.NormalizeComputedExpression(selected.Text);

        var result = PortableExpression.Translate(selected.Text, SqlDialect.SqlServer, column, booleanResult);
        return result.Sql ?? throw new InvalidOperationException(
            $"Untranslatable expression reached the SQL Server renderer: {selected.Text}");
    }

    /// <summary>
    /// The SQL Server text for <paramref name="selected"/> as a condition — a rollup's or a view's filter. A boolean field
    /// standing alone as a condition is compared with 1 (SQL Server takes no bit there); a platform expression is as written.
    /// </summary>
    internal static string RenderCondition(SelectedExpression selected, Func<string, string> column)
    {
        if (selected.IsRaw) return FullViewRenderer.NormalizeComputedExpression(selected.Text);

        var result = PortableExpression.Translate(selected.Text, SqlDialect.SqlServer, column, condition: true);
        return result.Sql ?? throw new InvalidOperationException(
            $"Untranslatable expression reached the SQL Server renderer: {selected.Text}");
    }

    /// <summary>
    /// The SQL Server text for <paramref name="selected"/> as the value of <paramref name="field"/>: a portable expression
    /// is cast to the type the field declares (<see cref="ComputedValues"/>), a boolean is a bit. A platform expression is
    /// the author's, as written.
    /// </summary>
    internal static string RenderValue(SelectedExpression selected, Func<string, string> column, FieldNode field)
    {
        var sql = Render(selected, column, booleanResult: field.Type == "boolean");
        return selected.IsRaw || !ComputedValues.IsCast(field)
            ? sql
            : $"CAST({sql} AS {SqlTypeMapper.Map(field.Type!, FieldAttributes.StringArgs(field.Params))})";
    }

    /// <summary>
    /// A field reference as a column of the row the expression is evaluated on: <c>first_name</c> → <c>[FirstName]</c>; a
    /// qualified one keeps its qualifier, quoted: <c>b.first_name</c> → <c>[b].[FirstName]</c> (a rollup filter may name the base row's
    /// alias directly).
    /// </summary>
    internal static string OwnColumn(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0
            ? $"[{name[..dot]}].[{NameCasing.ToPascalCase(name[(dot + 1)..])}]"
            : $"[{NameCasing.ToPascalCase(name)}]";
    }
}
