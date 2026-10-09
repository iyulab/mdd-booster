using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql.Postgres;

/// <summary>
/// How an M3L expression reaches PostgreSQL: the one declared for <c>postgresql</c> as written, the portable one translated
/// (M3L §10.7). Unlike SQL Server — whose generated views passed expressions through before the language defined them —
/// this dialect never emitted an expression, so there is nothing to keep building: an expression it cannot translate stops
/// the build (<c>MDD037</c>), and so does one with no PostgreSQL expression at all (<c>MDD036</c>).
/// </summary>
internal static class PostgresExpressions
{
    /// <summary>
    /// The PostgreSQL text for <paramref name="selected"/>. Callers render only after <see cref="Violations"/> came back
    /// empty, so an untranslatable portable expression here is a programming error, not a model one.
    /// </summary>
    internal static string Render(SelectedExpression selected, Func<string, string> column)
    {
        if (selected.IsRaw) return selected.Text;
        var result = PortableExpression.Translate(selected.Text, SqlDialect.PostgreSql, column);
        return result.Sql ?? throw new InvalidOperationException(
            $"Untranslatable expression reached the PostgreSQL renderer: {selected.Text}");
    }

    /// <summary>
    /// Every expression the PostgreSQL output would need and cannot have — <c>MDD036</c> (none for this platform) and
    /// <c>MDD037</c> (a portable-context expression outside the subset). Field references are not checked here; the
    /// translation only needs to know whether every construct has a PostgreSQL form.
    /// </summary>
    internal static List<string> Violations(GeneratorContext context)
    {
        var violations = new List<string>();
        void Check(SelectedExpression? selected, bool declared, string where, IEnumerable<PlatformExpression> raw)
        {
            if (selected is null)
            {
                if (declared)
                    violations.Add($"MDD036: {where} — PostgreSQL 용 식이 없다(선언된 플랫폼: {string.Join(", ", raw.Select(r => r.Platform ?? "(없음)"))}). " +
                                   "이식 가능한 식을 두거나 platform: \"postgresql\" 로 선언하라.");
                return;
            }
            if (selected.IsRaw) return;
            var result = PortableExpression.Translate(selected.Text, SqlDialect.PostgreSql, c => c);
            if (result.Sql is null)
                violations.Add($"MDD037: {where} — \"{selected.Text}\" 은(는) 이식 가능한 식이 아니다({string.Join(", ", result.Unsupported)}). " +
                               "부분집합(M3L 명세 §10.7)으로 다시 쓰거나 @computed_raw(…, platform: \"postgresql\") / where_raw 로 선언하라.");
        }

        foreach (var model in context.Models)
        {
            foreach (var field in model.Fields.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                if (field.Computed is { } computed)
                    Check(ExpressionSelection.Select(computed, SqlDialect.PostgreSql), declared: true,
                        $"'{model.Name}.{field.Name}' @computed", computed.Raw);
                if (field.Rollup is { } rollup)
                    Check(ExpressionSelection.Select(rollup.Where, rollup.WhereRaw, SqlDialect.PostgreSql),
                        declared: rollup.WhereRaw.Count > 0, $"'{model.Name}.{field.Name}' @rollup where", rollup.WhereRaw);
            }
        }
        foreach (var view in context.Views)
        {
            Check(view.WhereFor(SqlDialect.PostgreSql), declared: view.WhereRaw.Count > 0, $"view '{view.Name}' where", view.WhereRaw);
            foreach (var c in view.Columns.OfType<ViewComputedColumn>())
                Check(ExpressionSelection.Select(c.Computed, SqlDialect.PostgreSql), declared: true,
                    $"view '{view.Name}.{c.Field.Name}' @computed", c.Computed.Raw);
        }
        return violations;
    }
}
