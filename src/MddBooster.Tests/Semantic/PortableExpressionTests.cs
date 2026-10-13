using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// The portable expression subset (M3L §10.7) translated into each dialect — the meaning is the language's, the
/// spelling is this generator's — and what cannot be translated is named instead of being passed through.
/// </summary>
public class PortableExpressionTests
{
    private static string Sql(string expression, SqlDialect dialect, bool booleanResult = false)
    {
        var result = PortableExpression.Translate(expression, dialect, c => $"<{c}>", booleanResult);
        Assert.True(result.Unsupported.Count == 0, string.Join("; ", result.Unsupported));
        return result.Sql!;
    }

    private static IReadOnlyList<string> Unsupported(string expression, SqlDialect dialect = SqlDialect.SqlServer)
    {
        var result = PortableExpression.Translate(expression, dialect, c => c);
        Assert.Null(result.Sql);
        return result.Unsupported;
    }

    [Theory]
    [InlineData("quantity * unit_price", "<quantity> * <unit_price>", "<quantity> * <unit_price>")]
    [InlineData("is_active = true", "<is_active> = 1", "<is_active> = TRUE")]
    [InlineData("status != 'cancelled'", "<status> <> 'cancelled'", "<status> <> 'cancelled'")]
    [InlineData("concat(a, ' ', b)", "CONCAT(<a>, ' ', <b>)", "concat(<a>, ' ', <b>)")]
    [InlineData("coalesce(nickname, 'x')", "COALESCE(<nickname>, 'x')", "coalesce(<nickname>, 'x')")]
    [InlineData("NULLIF(n, 0)", "NULLIF(<n>, 0)", "nullif(<n>, 0)")]
    [InlineData("length(name)", "(LEN(CONCAT(<name>, '.')) - 1)", "length(<name>)")]
    [InlineData("substring(code, 2, 3)", "SUBSTRING(<code>, 2, 3)", "substring(<code> from 2 for 3)")]
    [InlineData("round(price, 2)", "ROUND(<price>, 2)", "round((<price>)::numeric, 2)")]
    [InlineData("lower(replace(title, ' ', '-'))", "LOWER(REPLACE(<title>, ' ', '-'))", "lower(replace(<title>, ' ', '-'))")]
    [InlineData("today()", "CAST(SYSDATETIME() AS date)", "CURRENT_DATE")]
    [InlineData("now()", "SYSDATETIMEOFFSET()", "now()")]
    [InlineData("generate_uuid()", "NEWID()", "gen_random_uuid()")]
    [InlineData("year(d)", "YEAR(<d>)", "EXTRACT(YEAR FROM <d>)::int")]
    [InlineData("date_add(now(), -30, 'day')", "DATEADD(DAY, -30, SYSDATETIMEOFFSET())", "(now() + (-30) * INTERVAL '1 day')")]
    [InlineData("date_diff('day', s, e)", "DATEDIFF(DAY, <s>, <e>)", "((<e>)::date - (<s>)::date)")]
    [InlineData("date_diff('year', s, e)", "DATEDIFF(YEAR, <s>, <e>)", "(EXTRACT(YEAR FROM <e>) - EXTRACT(YEAR FROM <s>))::int")]
    [InlineData("status IN ('a', 'b') AND NOT (x IS NULL)", "<status> IN ('a', 'b') AND NOT (<x> IS NULL)", "<status> IN ('a', 'b') AND NOT (<x> IS NULL)")]
    [InlineData("CASE WHEN t > 1000 THEN 'Gold' ELSE 'Bronze' END", "CASE WHEN <t> > 1000 THEN 'Gold' ELSE 'Bronze' END", "CASE WHEN <t> > 1000 THEN 'Gold' ELSE 'Bronze' END")]
    [InlineData("Order.status = 'paid' AND $parent.region = Order.region", "<Order.status> = 'paid' AND <$parent.region> = <Order.region>", "<Order.status> = 'paid' AND <$parent.region> = <Order.region>")]
    [InlineData("'it''s' = label", "'it''s' = <label>", "'it''s' = <label>")]
    public void The_subset_translates_into_each_dialect(string expression, string sqlServer, string postgres)
    {
        Assert.Equal(sqlServer, Sql(expression, SqlDialect.SqlServer));
        Assert.Equal(postgres, Sql(expression, SqlDialect.PostgreSql));
    }

    /// <summary>SQL Server's select list takes no predicate: a boolean field's comparison becomes a CASE over bit.</summary>
    [Fact]
    public void A_boolean_predicate_is_a_case_on_sql_server_only()
    {
        Assert.Equal("CASE WHEN <age> >= 18 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Sql("age >= 18", SqlDialect.SqlServer, booleanResult: true));
        Assert.Equal("<age> >= 18", Sql("age >= 18", SqlDialect.PostgreSql, booleanResult: true));
        Assert.Equal("CAST(1 AS bit)", Sql("true", SqlDialect.SqlServer, booleanResult: true));
        // A CASE that yields the value is not itself a predicate, whatever its conditions compare.
        Assert.Equal("CASE WHEN <a> > 1 THEN 1 ELSE 0 END",
            Sql("CASE WHEN a > 1 THEN true ELSE false END", SqlDialect.SqlServer, booleanResult: true));
        Assert.Equal("<flag>", Sql("flag", SqlDialect.SqlServer, booleanResult: true));
    }

    [Fact]
    public void What_is_outside_the_subset_is_named_not_passed_through()
    {
        Assert.Equal(["function DATEDIFF()", "function GETDATE()"], Unsupported("DATEDIFF(DAY, a, GETDATE())"));
        Assert.Equal(["bracketed identifier [Name]", "function ISNULL()"], Unsupported("ISNULL([Name], 'x')").Order(StringComparer.Ordinal));
        Assert.Equal(["+ joining text (write concat(…))"], Unsupported("first_name + ' ' + last_name"));
        Assert.Equal(["|| (write concat(…))"], Unsupported("a || b", SqlDialect.PostgreSql));
        Assert.Contains("date_add() unit must be one of 'year', 'month', 'day', 'hour', 'minute', 'second'",
            Unsupported("date_add(d, 1, unit_column)"));
        Assert.Contains("substring() takes 3 argument(s), not 2", Unsupported("substring(a, 1)"));
    }
}

/// <summary>Which expression a dialect uses: the one declared for its platform, otherwise the portable one.</summary>
public class ExpressionSelectionTests
{
    private static readonly List<PlatformExpression> Raw =
    [
        new() { Platform = "sqlserver", Expression = "YEAR(d)" },
        new() { Platform = "PostgreSQL", Expression = "EXTRACT(YEAR FROM d)::int" },
    ];

    [Fact]
    public void The_platform_expression_wins_and_is_marked_raw()
    {
        Assert.Equal(new SelectedExpression("YEAR(d)", IsRaw: true), ExpressionSelection.Select("year(d)", Raw, SqlDialect.SqlServer));
        Assert.Equal(new SelectedExpression("EXTRACT(YEAR FROM d)::int", IsRaw: true), ExpressionSelection.Select("year(d)", Raw, SqlDialect.PostgreSql));
    }

    [Fact]
    public void Without_one_for_the_platform_the_portable_expression_is_used_and_without_either_nothing()
    {
        var sqlServerOnly = Raw.Take(1).ToList();
        Assert.Equal(new SelectedExpression("year(d)", IsRaw: false), ExpressionSelection.Select("year(d)", sqlServerOnly, SqlDialect.PostgreSql));
        Assert.Null(ExpressionSelection.Select(null, sqlServerOnly, SqlDialect.PostgreSql));
        Assert.Null(ExpressionSelection.Select("  ", null, SqlDialect.SqlServer));
    }
}
