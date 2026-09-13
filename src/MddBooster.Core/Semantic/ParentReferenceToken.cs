using System.Text.RegularExpressions;

namespace MddBooster.Core.Semantic;

/// <summary>
/// A rollup's <c>where:</c> expression runs as the correlated subquery's filter, so it can
/// read the parent row — but the SQL alias that names that row (<c>b</c>) is an internal
/// rendering detail, not a declared contract. A model file that writes the alias directly
/// (<c>where: "b.production_state = 'active'"</c>) ties itself to whatever alias mdd-booster
/// happens to emit today. <c>$parent.&lt;snake_field&gt;</c> is the documented token for the
/// same reference; each dialect renderer substitutes it for its own alias/casing before doing
/// anything else with the expression.
/// </summary>
public static class ParentReferenceToken
{
    private static readonly Regex Pattern = new(@"\$parent\.([a-z_][a-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>
    /// Replaces every <c>$parent.&lt;snake_field&gt;</c> occurrence in <paramref name="expr"/>.
    /// <paramref name="renderColumn"/> receives the referenced field name exactly as written
    /// (m3l's own snake_case convention) and returns the dialect-specific column reference.
    /// </summary>
    public static string Substitute(string expr, Func<string, string> renderColumn)
        => Pattern.Replace(expr, m => renderColumn(m.Groups[1].Value));
}
