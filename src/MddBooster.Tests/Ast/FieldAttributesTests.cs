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

    // --- type params (decimal(p,s) / string(n)) --------------------------------------
    //
    // TypeParams and StringMaxLength are the single source several generators read from:
    // the SQL DECIMAL(p,s) / NVARCHAR(n) shape, the EF [Column(TypeName)] attribute, the
    // generated form's step / maxlength, and FieldSchema's maxLength. Until now their guards
    // were only exercised indirectly through renderer tests, so removing a guard could leave
    // every existing test green. These pin the contract directly.

    private static FieldNode EdgeField(string name)
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "type-param-edges.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Edge");
        return model.Fields.Single(f => f.Name == name);
    }

    [Fact]
    public void TypeParams_normalizes_numeric_params_to_integer_text()
    {
        // M3L.Native serializes integer params as doubles (38 arrives as 38.0). Callers build
        // SQL type text and JSX literals from these, where "38.0" would be wrong.
        Assert.Equal(["38", "10"], FieldAttributes.TypeParams(EdgeField("deep_scale")));
    }

    [Fact]
    public void TypeParams_returns_null_when_the_type_has_no_params()
    {
        Assert.Null(FieldAttributes.TypeParams(EdgeField("unsized")));
    }

    [Fact]
    public void TypeParams_preserves_a_single_param()
    {
        // decimal(12) is distinct from decimal(12,0) at the AST level even though SQL renders
        // both as DECIMAL(12,0) — consumers must be able to tell scale-absent from scale-zero.
        Assert.Equal(["12"], FieldAttributes.TypeParams(EdgeField("one_param")));
    }

    [Fact]
    public void StringMaxLength_reads_the_declared_ceiling()
    {
        Assert.Equal(50, FieldAttributes.StringMaxLength(EdgeField("sized")));
    }

    [Fact]
    public void StringMaxLength_rejects_a_zero_ceiling()
    {
        // The parser accepts string(0). Passing it through would emit maxlength={0}, which
        // blocks ALL input on that field — a worse failure than the missing ceiling it replaces.
        Assert.Null(FieldAttributes.StringMaxLength(EdgeField("zero_len")));
    }

    [Fact]
    public void StringMaxLength_is_null_without_params()
    {
        // Bare `string` is NVARCHAR(MAX) — no ceiling exists to surface.
        Assert.Null(FieldAttributes.StringMaxLength(EdgeField("unsized")));
    }

    [Fact]
    public void StringMaxLength_is_null_for_a_non_string_type()
    {
        // decimal(12,2) also carries params; reading params without checking the type would
        // report a 12-character ceiling for a numeric column.
        Assert.Null(FieldAttributes.StringMaxLength(EdgeField("not_a_string")));
    }
}
