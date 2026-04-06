using MddBooster.Generators.Model;
using Xunit;

namespace MddBooster.Tests.Generators.Model;

public class CSharpTypeMapperTests
{
    [Theory]
    [InlineData("identifier", "Guid")]
    [InlineData("boolean", "bool")]
    [InlineData("integer", "int")]
    [InlineData("long", "long")]
    [InlineData("short", "short")]
    [InlineData("byte", "byte")]
    [InlineData("float", "float")]
    [InlineData("double", "double")]
    [InlineData("decimal", "decimal")]
    [InlineData("string", "string")]
    [InlineData("text", "string")]
    [InlineData("date", "DateOnly")]
    [InlineData("time", "TimeOnly")]
    [InlineData("timestamp", "DateTimeOffset")]
    [InlineData("datetime", "DateTimeOffset")]
    [InlineData("json", "string")]
    [InlineData("binary", "byte[]")]
    public void Map_primitive_types(string m3l, string expected)
    {
        Assert.Equal(expected, CSharpTypeMapper.Map(m3l));
    }

    [Theory]
    [InlineData("phone", "global::Iyu.Core.ValueObjects.PhoneNumber")]
    [InlineData("email", "global::Iyu.Core.ValueObjects.EmailAddress")]
    [InlineData("url", "global::Iyu.Core.ValueObjects.WebUrl")]
    public void Map_value_objects_fully_qualified(string m3l, string expected)
    {
        Assert.Equal(expected, CSharpTypeMapper.Map(m3l));
    }

    [Fact]
    public void Map_unknown_throws()
    {
        Assert.Throws<NotSupportedException>(() => CSharpTypeMapper.Map("unknown"));
    }

    [Fact]
    public void Map_empty_throws()
    {
        Assert.Throws<ArgumentException>(() => CSharpTypeMapper.Map(""));
    }

    [Theory]
    [InlineData("identifier", true)]
    [InlineData("integer", true)]
    [InlineData("decimal", true)]
    [InlineData("phone", true)]
    [InlineData("email", true)]
    [InlineData("url", true)]
    [InlineData("string", false)]
    [InlineData("text", false)]
    [InlineData("binary", false)]
    [InlineData("json", false)]
    public void IsValueType(string m3l, bool expected)
    {
        Assert.Equal(expected, CSharpTypeMapper.IsValueType(m3l));
    }

    [Theory]
    [InlineData("string", " = string.Empty;")]
    [InlineData("text", " = string.Empty;")]
    [InlineData("json", " = string.Empty;")]
    [InlineData("binary", " = Array.Empty<byte>();")]
    [InlineData("integer", "")]
    [InlineData("phone", "")]
    [InlineData("identifier", "")]
    [InlineData("boolean", "")]
    public void DefaultInitializer(string m3l, string expected)
    {
        Assert.Equal(expected, CSharpTypeMapper.DefaultInitializer(m3l));
    }
}
