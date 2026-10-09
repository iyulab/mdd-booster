using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// How an M3L expression reaches SQL Server: the one declared for the platform as written, the portable one translated
/// (M3L §10.7) — one place for the rule the full views, rollup filters and standalone views share.
/// </summary>
internal static class SqlServerExpressions
{
    /// <summary>
    /// The SQL Server text for <paramref name="selected"/>. A platform expression is emitted as written, its field names
    /// spelled as columns; a portable expression is translated.
    /// </summary>
    /// <param name="column">Spells a field reference as a column in the query the expression sits in.</param>
    /// <param name="booleanResult">The expression is a boolean field's value (see <see cref="PortableExpression"/>).</param>
    /// <param name="where">Names the expression's place for a warning — <c>Model.field</c>, or a view.</param>
    internal static string Render(SelectedExpression selected, Func<string, string> column, bool booleanResult, string where)
    {
        if (selected.IsRaw) return FullViewRenderer.NormalizeComputedExpression(selected.Text);

        var result = PortableExpression.Translate(selected.Text, SqlDialect.SqlServer, column, booleanResult);
        if (result.Sql is not null) return result.Sql;

        // TODO(MDD038 → MDD037): a model whose portable-context expression was written in SQL Server syntax kept building
        // before expressions had a defined language. This release passes it through with a warning; the next minor
        // refuses it like PostgreSQL does — remove this fallback then.
        Console.Error.WriteLine(
            $"[sql] 경고 MDD038 ({where}): \"{selected.Text}\" 은(는) 이식 가능한 식이 아니다({string.Join(", ", result.Unsupported)}) — " +
            "이 판은 SQL Server 에 원문 그대로 내고, 다음 마이너부터 거절한다. 부분집합(명세 §10.7)으로 다시 쓰거나 " +
            "@computed_raw(…, platform: \"sqlserver\") / where_raw 로 플랫폼 식임을 선언하라.");
        return FullViewRenderer.NormalizeComputedExpression(selected.Text);
    }

    /// <summary>
    /// A field reference as a column of the row the expression is evaluated on: <c>first_name</c> → <c>[FirstName]</c>; a
    /// qualified one keeps its qualifier, <c>b.first_name</c> → <c>b.[FirstName]</c> (a rollup filter may name the base row's
    /// alias directly).
    /// </summary>
    internal static string OwnColumn(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0
            ? $"{name[..dot]}.[{NameCasing.ToPascalCase(name[(dot + 1)..])}]"
            : $"[{NameCasing.ToPascalCase(name)}]";
    }

    /// <summary>
    /// <c>MDD036</c> — an expression this dialect has none for: a computed field or a rollup filter declared only for other
    /// platforms. Generating it would mean inventing an expression; the build stops before anything is written.
    /// </summary>
    internal static void RefuseMissingExpressions(GeneratorContext context)
    {
        var missing = new List<string>();
        foreach (var model in context.Models)
        {
            foreach (var field in model.Fields)
            {
                if (field.Computed is { Raw.Count: > 0 } computed
                    && ExpressionSelection.Select(computed, SqlDialect.SqlServer) is null)
                    missing.Add($"'{model.Name}.{field.Name}' @computed_raw({Platforms(computed.Raw)})");
                if (field.Rollup is { WhereRaw.Count: > 0 } rollup && string.IsNullOrWhiteSpace(rollup.Where)
                    && ExpressionSelection.Select(rollup.Where, rollup.WhereRaw, SqlDialect.SqlServer) is null)
                    missing.Add($"'{model.Name}.{field.Name}' @rollup where_raw({Platforms(rollup.WhereRaw)})");
            }
        }
        foreach (var view in context.Views)
        {
            if (view.WhereRaw.Count > 0 && view.WhereFor(SqlDialect.SqlServer) is null)
                missing.Add($"view '{view.Name}' where_raw({Platforms(view.WhereRaw)})");
            foreach (var column in view.Columns.OfType<ViewComputedColumn>())
                if (ExpressionSelection.Select(column.Computed, SqlDialect.SqlServer) is null)
                    missing.Add($"view '{view.Name}.{column.Field.Name}' @computed_raw({Platforms(column.Computed.Raw)})");
        }
        if (missing.Count == 0) return;

        throw new GeneratorRefusalException(GeneratorRefusalKind.Model,
            $"MDD036: SQL Server 용 식이 없다 — {string.Join(", ", missing)}. 이식 가능한 식(@computed / where)을 두거나 " +
            "platform: \"sqlserver\" 로 선언하라.");

        static string Platforms(IEnumerable<PlatformExpression> raw)
            => string.Join(", ", raw.Select(r => r.Platform ?? "(없음)"));
    }
}
