using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql.Postgres;

/// <summary>
/// How an M3L expression reaches PostgreSQL: the one declared for <c>postgresql</c> as written, the portable one translated
/// (M3L §10.7). What the dialect cannot have stops the build before this is asked (<see cref="ExpressionRefusals"/>).
/// </summary>
internal static class PostgresExpressions
{
    /// <summary>
    /// The PostgreSQL text for <paramref name="selected"/>. Callers render only after
    /// <see cref="ExpressionRefusals.Violations"/> came back empty, so an untranslatable portable expression here is a
    /// programming error, not a model one.
    /// </summary>
    internal static string Render(SelectedExpression selected, Func<string, string> column)
    {
        if (selected.IsRaw) return selected.Text;
        var result = PortableExpression.Translate(selected.Text, SqlDialect.PostgreSql, column);
        return result.Sql ?? throw new InvalidOperationException(
            $"Untranslatable expression reached the PostgreSQL renderer: {selected.Text}");
    }

    /// <summary>
    /// The PostgreSQL text for <paramref name="selected"/> as the value of <paramref name="field"/>: a portable expression
    /// is cast to the type the field declares (<see cref="ComputedValues"/>). A platform expression is the author's, as
    /// written.
    /// </summary>
    internal static string RenderValue(SelectedExpression selected, Func<string, string> column, FieldNode field)
    {
        var sql = Render(selected, column);
        if (selected.IsRaw || !ComputedValues.IsCast(field)) return sql;
        var type = PgTypeMapper.Map(field.Type!, FieldAttributes.StringArgs(field.Params));
        // PostgreSQL's cast to an integer rounds; SQL Server's truncates toward zero — truncate first, so both agree.
        return ComputedValues.IsIntegral(field)
            ? $"CAST(trunc(({sql})::numeric) AS {type})"
            : $"CAST({sql} AS {type})";
    }
}
