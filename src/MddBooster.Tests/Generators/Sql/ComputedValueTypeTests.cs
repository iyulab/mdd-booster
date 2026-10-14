using M3L.Native;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A computed column has the type its field declares, on both platforms. The same portable expression yields different
/// types per dialect — <c>floor</c> of a decimal is a decimal on SQL Server and a numeric on PostgreSQL, <c>date_add</c> of
/// a date is a date on one and a timestamp on the other — and the read type maps the declared one, so the value is cast
/// to it. Text keeps its own type (a cast would truncate it), and a platform expression is the author's, as written.
/// </summary>
public class ComputedValueTypeTests
{
    private static FieldNode Field(string type, params string[] parameters) => new()
    {
        Name = "value",
        Type = type,
        Kind = FieldKind.Computed,
        Params = [.. parameters.Select(p => System.Text.Json.JsonDocument.Parse(p).RootElement)],
        Loc = new SourceLocation { File = "test.m3l.md", Line = 1 },
    };

    private static SelectedExpression Portable(string text) => new(text, IsRaw: false);

    [Theory]
    [InlineData("integer", "floor(price)", "CAST(FLOOR([Price]) AS INT)", "CAST(floor(price) AS integer)")]
    [InlineData("date", "date_add(due, 30, 'day')", "CAST(DATEADD(DAY, 30, [Due]) AS DATE)", "CAST((due + (30) * INTERVAL '1 day') AS date)")]
    [InlineData("long", "abs(delta)", "CAST(ABS([Delta]) AS BIGINT)", "CAST(abs(delta) AS bigint)")]
    public void A_computed_value_is_cast_to_the_declared_type(string type, string expression, string sqlServer, string postgres)
    {
        Assert.Equal(sqlServer, SqlServerExpressions.RenderValue(Portable(expression), SqlServerExpressions.OwnColumn, Field(type)));
        Assert.Equal(postgres, PostgresExpressions.RenderValue(Portable(expression), name => name, Field(type)));
    }

    [Fact]
    public void A_decimal_keeps_its_declared_precision()
    {
        Assert.Equal("CAST(ROUND([Price], 1) AS DECIMAL(10,2))",
            SqlServerExpressions.RenderValue(Portable("round(price, 1)"), SqlServerExpressions.OwnColumn, Field("decimal", "10", "2")));
        Assert.Equal("CAST(round((price)::numeric, 1) AS numeric(10,2))",
            PostgresExpressions.RenderValue(Portable("round(price, 1)"), name => name, Field("decimal", "10", "2")));
    }

    [Fact]
    public void Text_and_platform_expressions_are_not_cast()
    {
        Assert.Equal("CONCAT([A], N'-', [B])",
            SqlServerExpressions.RenderValue(Portable("concat(a, '-', b)"), SqlServerExpressions.OwnColumn, Field("string", "20")));
        Assert.Equal("FLOOR([Price])",
            SqlServerExpressions.RenderValue(new SelectedExpression("FLOOR([Price])", IsRaw: true), SqlServerExpressions.OwnColumn, Field("integer")));
        Assert.Equal("floor(price)::int",
            PostgresExpressions.RenderValue(new SelectedExpression("floor(price)::int", IsRaw: true), name => name, Field("integer")));
    }

    /// <summary>A boolean is a bit on SQL Server whatever produced it, and already boolean on PostgreSQL.</summary>
    [Fact]
    public void A_boolean_value_is_a_bit_on_sql_server()
    {
        Assert.Equal("CAST(CASE WHEN [Age] > 1 THEN 1 ELSE 0 END AS bit)",
            SqlServerExpressions.RenderValue(Portable("CASE WHEN age > 1 THEN true ELSE false END"), SqlServerExpressions.OwnColumn, Field("boolean")));
        Assert.Equal("CASE WHEN age > 1 THEN TRUE ELSE FALSE END",
            PostgresExpressions.RenderValue(Portable("CASE WHEN age > 1 THEN true ELSE false END"), name => name, Field("boolean")));
    }
}
