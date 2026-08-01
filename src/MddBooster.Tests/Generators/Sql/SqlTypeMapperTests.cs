using M3L.Native;
using MddBooster.Core.Types;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class SqlTypeMapperTests
{
    [Theory]
    [InlineData("identifier", null, "UNIQUEIDENTIFIER")]
    [InlineData("boolean", null, "BIT")]
    [InlineData("integer", null, "INT")]
    [InlineData("timestamp", null, "DATETIMEOFFSET")]
    [InlineData("date", null, "DATE")]
    [InlineData("text", null, "NVARCHAR(MAX)")]
    public void Map_SimpleTypes(string m3lType, string? param, string expected)
    {
        var actual = SqlTypeMapper.Map(m3lType, parameters: param is null ? null : new List<string> { param });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Map_StringWithLength_EmitsNvarchar()
    {
        var actual = SqlTypeMapper.Map("string", new List<string> { "30" });
        Assert.Equal("NVARCHAR(30)", actual);
    }

    [Fact]
    public void Map_StringWithoutLength_EmitsNvarcharMax()
    {
        var actual = SqlTypeMapper.Map("string", parameters: null);
        Assert.Equal("NVARCHAR(MAX)", actual);
    }

    [Fact]
    public void Map_DecimalWithPrecisionAndScale_EmitsDecimal()
    {
        var actual = SqlTypeMapper.Map("decimal", new List<string> { "12", "2" });
        Assert.Equal("DECIMAL(12,2)", actual);
    }

    /// <summary>
    /// The column's width for a semantic type is not written in this mapper. It
    /// is read from the shared table, so that the entity attribute guarding
    /// writes to the column cannot be sized from a different number. Hard-coding
    /// the width here again would restore exactly the divergence the table
    /// exists to prevent, which is why this asserts the wiring rather than a
    /// literal — the literals are pinned once, in M3lPrimitivesTests.
    /// </summary>
    [Theory]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("url")]
    public void Map_SemanticType_TakesItsBoundFromTheSharedTable(string m3lType)
    {
        var expected = M3lPrimitives.ImplicitMaxLength[m3lType];

        Assert.Equal($"NVARCHAR({expected})", SqlTypeMapper.Map(m3lType, null));
    }

    [Fact]
    public void Map_UnknownType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => SqlTypeMapper.Map("alien", null));
    }
}
