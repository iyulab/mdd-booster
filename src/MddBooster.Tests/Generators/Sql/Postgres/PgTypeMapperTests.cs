using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql.Postgres;

public class PgTypeMapperTests
{
    [Theory]
    [InlineData("identifier", null, "uuid")]
    [InlineData("boolean", null, "boolean")]
    [InlineData("integer", null, "integer")]
    [InlineData("long", null, "bigint")]
    [InlineData("short", null, "smallint")]
    [InlineData("byte", null, "smallint")]          // PG에 1바이트 정수 없음 — smallint 승격
    [InlineData("float", null, "real")]
    [InlineData("double", null, "double precision")]
    [InlineData("string", null, "text")]
    [InlineData("text", null, "text")]
    [InlineData("date", null, "date")]
    [InlineData("time", null, "time")]
    [InlineData("timestamp", null, "timestamptz")]
    [InlineData("datetime", null, "timestamptz")]
    [InlineData("phone", null, "varchar(30)")]
    [InlineData("email", null, "varchar(200)")]
    [InlineData("url", null, "varchar(500)")]
    [InlineData("json", null, "jsonb")]
    [InlineData("binary", null, "bytea")]
    [InlineData("string", "30", "varchar(30)")]
    public void Map_SimpleTypes(string m3lType, string? param, string expected)
    {
        var actual = PgTypeMapper.Map(m3lType, param is null ? null : new List<string> { param });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Map_DecimalWithPrecisionAndScale_EmitsNumeric()
    {
        Assert.Equal("numeric(12,2)", PgTypeMapper.Map("decimal", new List<string> { "12", "2" }));
    }

    [Fact]
    public void Map_DecimalWithoutParams_EmitsNumericDefault()
    {
        Assert.Equal("numeric(18,2)", PgTypeMapper.Map("decimal", null));
    }

    [Fact]
    public void Map_BinaryWithLength_EmitsByteaWithoutLength()
    {
        // PG bytea는 길이 상한이 없다 — 길이 인자는 DDL에선 소실되며, 이는 문서화된 완화다
        Assert.Equal("bytea", PgTypeMapper.Map("binary", new List<string> { "128" }));
    }

    [Fact]
    public void Map_UnknownType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => PgTypeMapper.Map("alien", null));
    }

    [Fact]
    public void MapFieldType_EnumType_EmitsVarcharSizedByConvention()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "enum-default.m3l.md"));
        var enumLookup = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        // Status(draft, published) — 최장 9자, 하한 20 → varchar(20)
        Assert.Equal("varchar(20)", PgTypeMapper.MapFieldType("Status", null, enumLookup));
    }
}
