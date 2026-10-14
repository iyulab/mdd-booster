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
    [InlineData("status != 'cancelled'", "<status> <> N'cancelled'", "<status> <> 'cancelled'")]
    [InlineData("concat(a, ' ', b)", "CONCAT(<a>, N' ', <b>)", "concat(<a>, ' ', <b>)")]
    [InlineData("coalesce(nickname, 'x')", "COALESCE(<nickname>, N'x')", "coalesce(<nickname>, 'x')")]
    [InlineData("NULLIF(n, 0)", "NULLIF(<n>, 0)", "nullif(<n>, 0)")]
    [InlineData("length(name)", "(LEN(CAST(<name> AS nvarchar(max)) + N'.') - 1)", "length(<name>)")]
    [InlineData("substring(code, 2, 3)", "SUBSTRING(<code>, 2, 3)", "substring(<code> from 2 for 3)")]
    [InlineData("round(price, 2)", "ROUND(<price>, 2)", "round((<price>)::numeric, 2)")]
    [InlineData("lower(replace(title, ' ', '-'))", "LOWER(REPLACE(<title>, N' ', N'-'))", "lower(replace(<title>, ' ', '-'))")]
    [InlineData("today()", "CAST(SYSDATETIME() AS date)", "CURRENT_DATE")]
    [InlineData("now()", "SYSDATETIMEOFFSET()", "now()")]
    [InlineData("generate_uuid()", "NEWID()", "gen_random_uuid()")]
    [InlineData("year(d)", "YEAR(<d>)", "EXTRACT(YEAR FROM <d>)::int")]
    [InlineData("date_add(now(), -30, 'day')", "DATEADD(DAY, -30, SYSDATETIMEOFFSET())", "(now() + (-30) * INTERVAL '1 day')")]
    [InlineData("date_diff('day', s, e)", "DATEDIFF(DAY, <s>, <e>)", "((<e>)::date - (<s>)::date)")]
    [InlineData("date_diff('year', s, e)", "DATEDIFF(YEAR, <s>, <e>)", "(EXTRACT(YEAR FROM <e>) - EXTRACT(YEAR FROM <s>))::int")]
    [InlineData("status IN ('a', 'b') AND NOT (x IS NULL)", "<status> IN (N'a', N'b') AND NOT (<x> IS NULL)", "<status> IN ('a', 'b') AND NOT (<x> IS NULL)")]
    [InlineData("CASE WHEN t > 1000 THEN 'Gold' ELSE 'Bronze' END", "CASE WHEN <t> > 1000 THEN N'Gold' ELSE N'Bronze' END", "CASE WHEN <t> > 1000 THEN 'Gold' ELSE 'Bronze' END")]
    [InlineData("Order.status = 'paid' AND $parent.region = Order.region", "<Order.status> = N'paid' AND <$parent.region> = <Order.region>", "<Order.status> = 'paid' AND <$parent.region> = <Order.region>")]
    [InlineData("'it''s' = label", "N'it''s' = <label>", "'it''s' = <label>")]
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
        // A value of a boolean field is a bit whatever produced it — 1 and 0 alone are int, which a boolean property
        // cannot be read from.
        Assert.Equal("CAST(CASE WHEN <a> > 1 THEN 1 ELSE 0 END AS bit)",
            Sql("CASE WHEN a > 1 THEN true ELSE false END", SqlDialect.SqlServer, booleanResult: true));
        Assert.Equal("CAST(<flag> AS bit)", Sql("flag", SqlDialect.SqlServer, booleanResult: true));
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

    /// <summary>
    /// Text that is not a well-formed expression is named, never emitted: each of these once reached the DDL as written,
    /// where it either broke the statement or silently changed what follows it.
    /// </summary>
    [Fact]
    public void Malformed_text_is_named_not_emitted()
    {
        // An unclosed literal would swallow the rest of the generated statement.
        Assert.Contains("unterminated string literal 'abc", Unsupported("name = 'abc"));
        // A number has one decimal point and ends where a letter would begin a name.
        Assert.Contains("malformed number 1.2.3", Unsupported("a + 1.2.3"));
        Assert.Contains("malformed number 2d", Unsupported("a + 2d"));
        // A missing ')' is not closed for the author, and an empty argument is not dropped.
        Assert.Contains("missing ')'", Unsupported("concat(a, b"));
        Assert.Contains("empty argument in concat()", Unsupported("concat(a, )"));
        Assert.Contains("empty ( )", Unsupported("a IN ()"));
    }

    /// <summary>
    /// Two operands with nothing between them are not the subset's grammar — the shape a platform word takes when it is
    /// written where an operator belongs (PostgreSQL's <c>ilike</c>, <c>interval '…'</c>), which would otherwise pass on
    /// the platform that knows it and break on the other.
    /// </summary>
    [Fact]
    public void An_operand_next_to_an_operand_is_named()
    {
        Assert.Contains("name ilike (no operator between them)", Unsupported("name ilike '%x%'", SqlDialect.PostgreSql));
        Assert.Contains("interval '30 days' (no operator between them)", Unsupported("d > now() - interval '30 days'", SqlDialect.PostgreSql));
        // Keywords join operands; true, false and null are operands themselves.
        Assert.Equal("<a> IS NOT NULL AND <b> = TRUE", Sql("a IS NOT NULL AND b = true", SqlDialect.PostgreSql));
    }

    /// <summary>
    /// The language's meaning, not one platform's: the length of null is null (SQL Server's <c>CONCAT</c> made it 0), and
    /// a string literal keeps every character (a T-SQL literal without <c>N</c> is narrowed to the database's code page,
    /// where non-Latin text becomes <c>?</c>).
    /// </summary>
    [Fact]
    public void Null_has_no_length_and_text_literals_are_unicode_on_sql_server()
    {
        Assert.Equal("(LEN(CAST(<name> AS nvarchar(max)) + N'.') - 1)", Sql("length(name)", SqlDialect.SqlServer));
        Assert.Equal("<name> = N'가나다'", Sql("name = '가나다'", SqlDialect.SqlServer));
        Assert.Equal("<name> = '가나다'", Sql("name = '가나다'", SqlDialect.PostgreSql));
    }

    /// <summary>
    /// A date has no hours: moving one by hours, minutes or seconds is refused on SQL Server and turns it into a timestamp
    /// on PostgreSQL. Known from a date field (the caller names field types) and from <c>today()</c>.
    /// </summary>
    [Fact]
    public void A_date_moves_by_whole_days_months_or_years()
    {
        const string Hint = "date_add() on a date takes 'year', 'month' or 'day'";
        var result = PortableExpression.Translate("date_add(due, 2, 'hour')", SqlDialect.PostgreSql, c => c,
            fieldType: name => name == "due" ? "date" : null);
        Assert.Null(result.Sql);
        Assert.Contains(Hint, result.Unsupported);
        Assert.Contains(Hint, Unsupported("date_add(today(), 1, 'minute')"));
        Assert.Equal("DATEADD(DAY, 2, <due>)", PortableExpression.Translate("date_add(due, 2, 'day')", SqlDialect.SqlServer,
            c => $"<{c}>", fieldType: name => name == "due" ? "date" : null).Sql);
    }

    /// <summary>Signs next to each other stay apart — glued, two minus signs open a line comment.</summary>
    [Fact]
    public void Two_signs_never_become_a_comment()
    {
        Assert.Equal("- -1", Sql("- -1", SqlDialect.SqlServer));
        Assert.Equal("<a> * - -1", Sql("a * - -1", SqlDialect.SqlServer));
        Assert.Equal("<a> - -1", Sql("a - -1", SqlDialect.PostgreSql));
    }

    /// <summary>
    /// <c>coalesce</c> and <c>concat</c> take two or more arguments (§10.7.4) — SQL Server refuses one, PostgreSQL takes it,
    /// so a single-argument call would build for one platform only.
    /// </summary>
    [Fact]
    public void Coalesce_and_concat_take_at_least_two_arguments()
    {
        Assert.Contains("concat() takes at least 2 argument(s), not 1", Unsupported("concat(a)"));
        Assert.Contains("coalesce() takes at least 2 argument(s), not 1", Unsupported("coalesce(a)", SqlDialect.PostgreSql));
    }

    /// <summary>
    /// Arithmetic is for numbers only (§10.7.2): <c>+</c> between text joins on SQL Server and fails on PostgreSQL. Text is
    /// known from a literal, from a function that returns text, and — when the caller says which fields hold text — from a
    /// field.
    /// </summary>
    [Fact]
    public void Plus_between_text_is_named_wherever_the_text_comes_from()
    {
        const string Hint = "+ joining text (write concat(…))";
        Assert.Contains(Hint, Unsupported("lower(a) + upper(b)"));
        var result = PortableExpression.Translate("first_name + last_name", SqlDialect.SqlServer, c => c,
            fieldType: name => name is "first_name" or "last_name" ? "string" : null);
        Assert.Null(result.Sql);
        Assert.Contains(Hint, result.Unsupported);
        // Numbers still add.
        Assert.Equal("<qty> + <extra>", PortableExpression.Translate("qty + extra", SqlDialect.SqlServer, c => $"<{c}>",
            fieldType: name => name is "qty" or "extra" ? "integer" : null).Sql);
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
