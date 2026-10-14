using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// The expressions a dialect's output would need and cannot have — one rule for both SQL targets, asked before either
/// writes a file. <c>MDD036</c>: the model declares an expression only for other platforms. <c>MDD037</c>: an expression
/// in a portable context uses something outside M3L's portable subset (§10.7), so it has no defined form in this dialect.
/// Emitting either unchanged would mean guessing the dialect the author had in mind.
/// </summary>
internal static class ExpressionRefusals
{
    /// <summary>
    /// Every refusal for <paramref name="dialect"/>, in model order. Field references are not resolved here; translation
    /// only needs to know whether every construct has a form in the dialect. Fields kept off the read surface are skipped
    /// — neither dialect's views carry them.
    /// </summary>
    internal static List<string> Violations(GeneratorContext context, SqlDialect dialect)
    {
        var product = dialect == SqlDialect.SqlServer ? "SQL Server" : "PostgreSQL";
        var platform = dialect.PlatformName();
        var violations = new List<string>();

        void Check(SelectedExpression? selected, bool declared, string where, IEnumerable<PlatformExpression> raw)
        {
            if (selected is null)
            {
                if (declared)
                    violations.Add($"MDD036: {where} — {product} 용 식이 없다(선언된 플랫폼: {string.Join(", ", raw.Select(r => r.Platform ?? "(없음)"))}). " +
                                   $"이식 가능한 식을 두거나 platform: \"{platform}\" 로 선언하라.");
                return;
            }
            if (selected.IsRaw) return;
            var result = PortableExpression.Translate(selected.Text, dialect, c => c);
            if (result.Sql is null)
                violations.Add($"MDD037: {where} — \"{selected.Text}\" 은(는) 이식 가능한 식이 아니다({string.Join(", ", result.Unsupported)}). " +
                               $"부분집합(M3L 명세 §10.7)으로 다시 쓰거나, {product} 문법 그대로 두려면 @computed_raw(…, platform: \"{platform}\") / " +
                               $"where_raw(…, platform: \"{platform}\") 로 옮겨라.");
        }

        foreach (var model in context.Models)
        {
            foreach (var field in model.Fields.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                if (field.Computed is { } computed)
                    Check(ExpressionSelection.Select(computed, dialect), declared: true,
                        $"'{model.Name}.{field.Name}' @computed", computed.Raw);
                if (field.Rollup is { } rollup)
                    Check(ExpressionSelection.Select(rollup.Where, rollup.WhereRaw, dialect),
                        declared: rollup.WhereRaw.Count > 0, $"'{model.Name}.{field.Name}' @rollup where", rollup.WhereRaw);
            }
        }
        foreach (var view in context.Views)
        {
            Check(view.WhereFor(dialect), declared: view.WhereRaw.Count > 0, $"view '{view.Name}' where", view.WhereRaw);
            foreach (var c in view.Columns.OfType<ViewComputedColumn>())
                Check(ExpressionSelection.Select(c.Computed, dialect), declared: true,
                    $"view '{view.Name}.{c.Field.Name}' @computed", c.Computed.Raw);
            // A view's rollup filters like a model's: unchecked, a non-portable one reached the renderer as an exception,
            // and one declared only for another platform left this dialect's view counting every row.
            foreach (var c in view.Columns.OfType<ViewRollupColumn>())
                Check(ExpressionSelection.Select(c.Rollup.Where, c.Rollup.WhereRaw, dialect),
                    declared: c.Rollup.WhereRaw.Count > 0, $"view '{view.Name}.{c.Field.Name}' @rollup where", c.Rollup.WhereRaw);
        }
        return violations;
    }

    /// <summary>Stops the build with every refusal at once, before anything is written.</summary>
    internal static void Refuse(GeneratorContext context, SqlDialect dialect)
    {
        var violations = Violations(context, dialect);
        if (violations.Count > 0)
            throw new GeneratorRefusalException(GeneratorRefusalKind.Model, string.Join(Environment.NewLine, violations));
    }
}
