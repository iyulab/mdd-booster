using M3L.Native;
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

    [Fact]
    public void Map_Phone_EmitsNvarchar30()
    {
        Assert.Equal("NVARCHAR(30)", SqlTypeMapper.Map("phone", null));
    }

    [Fact]
    public void Map_Email_EmitsNvarchar200()
    {
        Assert.Equal("NVARCHAR(200)", SqlTypeMapper.Map("email", null));
    }

    [Fact]
    public void Map_UnknownType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => SqlTypeMapper.Map("alien", null));
    }
}
