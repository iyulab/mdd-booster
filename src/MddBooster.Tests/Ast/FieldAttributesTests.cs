using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Ast;

/// <summary>
/// 속성 별칭 해소 정본 (스펙 §10.8.1: `@pk`는 `@primary`의 별칭).
/// 생성기들은 속성 조회를 이 클래스로 위임하므로, 별칭 지식은 여기에만 존재한다.
/// </summary>
public class FieldAttributesTests
{
    private static FieldNode LoadPrimaryAliasIdField()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "primary-alias.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Sample");
        return model.Fields.Single(f => f.Name == "id");
    }

    [Fact]
    public void Has_pk_matches_field_declared_with_primary_alias()
    {
        var id = LoadPrimaryAliasIdField();

        Assert.True(FieldAttributes.Has(id, "pk"));
    }

    [Fact]
    public void Has_primary_matches_field_declared_with_primary()
    {
        var id = LoadPrimaryAliasIdField();

        Assert.True(FieldAttributes.Has(id, "primary"));
    }

    [Fact]
    public void Has_is_case_insensitive_and_does_not_match_unrelated_names()
    {
        var id = LoadPrimaryAliasIdField();

        Assert.True(FieldAttributes.Has(id, "PK"));
        Assert.True(FieldAttributes.Has(id, "generated"));
        Assert.False(FieldAttributes.Has(id, "unique"));
    }

    [Fact]
    public void Find_pk_returns_attribute_node_declared_as_primary()
    {
        var id = LoadPrimaryAliasIdField();

        var attr = FieldAttributes.Find(id, "pk");

        Assert.NotNull(attr);
        Assert.Equal("primary", attr!.Name);
    }

    [Fact]
    public void Find_returns_null_when_attribute_absent()
    {
        var id = LoadPrimaryAliasIdField();

        Assert.Null(FieldAttributes.Find(id, "reference"));
    }
}
