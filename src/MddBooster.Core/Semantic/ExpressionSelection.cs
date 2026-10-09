using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>The SQL dialects this generator emits.</summary>
public enum SqlDialect
{
    SqlServer,
    PostgreSql,
}

/// <summary>The expression a dialect uses, and whether it was declared for that platform.</summary>
/// <param name="Text">The expression text as the model declares it.</param>
/// <param name="IsRaw">
/// <c>true</c> when it is the platform expression declared for this dialect (<c>@computed_raw</c> / <c>where_raw</c>) —
/// emitted as written; <c>false</c> when it is the portable expression, which is translated.
/// </param>
public sealed record SelectedExpression(string Text, bool IsRaw);

/// <summary>
/// Which expression a dialect uses (M3L §10.7.5): the one declared for its platform when there is one, otherwise the
/// portable one. One home for the rule, so every renderer — entity metadata, full views, standalone views, rollup and
/// view filters — picks the same text.
/// </summary>
public static class ExpressionSelection
{
    /// <summary>The M3L platform name a dialect answers to (§10.7.5).</summary>
    public static string PlatformName(this SqlDialect dialect) => dialect switch
    {
        SqlDialect.SqlServer => "sqlserver",
        SqlDialect.PostgreSql => "postgresql",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
    };

    /// <summary>
    /// The expression <paramref name="dialect"/> uses, or <c>null</c> when the model declares neither a portable
    /// expression nor one for this platform.
    /// </summary>
    public static SelectedExpression? Select(string? portable, IEnumerable<PlatformExpression>? raw, SqlDialect dialect)
    {
        var platform = dialect.PlatformName();
        var mine = raw?.FirstOrDefault(r => string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase));
        if (mine is not null)
            return new SelectedExpression(mine.Expression, IsRaw: true);
        return string.IsNullOrWhiteSpace(portable) ? null : new SelectedExpression(portable, IsRaw: false);
    }

    /// <summary>The expression a computed field uses in <paramref name="dialect"/>.</summary>
    public static SelectedExpression? Select(ComputedDef? computed, SqlDialect dialect)
        => computed is null ? null : Select(computed.Expression, computed.Raw, dialect);
}
