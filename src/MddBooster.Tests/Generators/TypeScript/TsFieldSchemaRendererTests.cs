using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

public class TsFieldSchemaRendererTests
{
    private static IReadOnlyList<ResolvedModel> LoadFixture(string name)
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
        return new InterfaceResolver(ast).ResolveAll();
    }

    private static IReadOnlyList<ResolvedModel> LoadInline(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-tsfs-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll();
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// 2026-07-22 회귀 — `@primary` 별칭 PK가 elide되지 않아 필드 스키마에 Id가 노출됨.
    /// </summary>
    [Fact]
    public void Primary_alias_pk_field_is_excluded_from_schema()
    {
        var models = LoadFixture("primary-alias.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.DoesNotContain("Id:", result);
        Assert.Contains("Name:", result);
    }

    [Fact]
    public void Emits_required_true_and_maxLength_for_non_nullable_string_field()
    {
        var models = LoadFixture("order-with-enum.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // order_number: string(30) @not_null @unique "주문번호" — non-nullable → required: true, maxLength: 30, label: '주문번호'
        Assert.Contains("OrderNumber: { required: true, maxLength: 30, label: '주문번호' },", result);
    }

    [Fact]
    public void Omits_nullable_field_with_no_other_constraints()
    {
        // A truly bare nullable field: no label, no group, no structural constraints → must not appear
        var ast = new M3lAst
        {
            Models =
            [
                new ModelNode
                {
                    Name = "SimpleOrder",
                    Type = ModelType.Model,
                    Fields =
                    [
                        new FieldNode
                        {
                            Name = "order_number",
                            Type = "string",
                            Params = [System.Text.Json.JsonDocument.Parse("30").RootElement],
                            Nullable = false,
                            Kind = FieldKind.Stored,
                            Description = "주문번호",
                            Attributes = []
                        },
                        new FieldNode
                        {
                            Name = "notes",
                            Type = "text",
                            Nullable = true,
                            Kind = FieldKind.Stored,
                            Description = null,  // no label
                            Attributes = []      // no group
                        }
                    ]
                }
            ]
        };
        var models = new InterfaceResolver(ast).ResolveAll();
        var result = TsFieldSchemaRenderer.RenderAll(models);

        // notes has no label, no group → must not appear
        Assert.DoesNotContain("Notes:", result);
    }

    [Fact]
    public void Emits_min_from_attribute()
    {
        var models = LoadFixture("item-with-constraints.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // qty: integer @min(1) — non-nullable → required: true, min: 1
        Assert.Contains("Qty: { required: true, min: 1 },", result);
    }

    [Fact]
    public void Emits_min_and_max_from_attributes()
    {
        var models = LoadFixture("item-with-constraints.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // rate: decimal(5,2) = 0 @min(0) @max(100) — non-nullable → required: true, min: 0, max: 100
        Assert.Contains("Rate: { required: true, min: 0, max: 100 },", result);
    }

    [Fact]
    public void Explicit_label_attribute_overrides_the_description()
    {
        var models = LoadInline(
            "## Sample\n" +
            "- id: identifier @pk @generated\n" +
            "- password_hash: string(64) @not_null @label(\"Password\") \"Salted hash of the user's password\"\n");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("PasswordHash: { required: true, maxLength: 64, label: 'Password' },", result);
    }

    [Fact]
    public void Explicit_label_attribute_alone_is_enough_to_include_an_otherwise_bare_nullable_field()
    {
        // Same shape as Omits_nullable_field_with_no_other_constraints, but with @label —
        // that must be enough on its own, without needing a description or any other constraint.
        var models = LoadInline(
            "## Sample\n" +
            "- id: identifier @pk @generated\n" +
            "- notes: text? @label(\"Notes\")\n");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("Notes: { label: 'Notes' },", result);
    }

    [Fact]
    public void Wraps_result_in_as_const_satisfies()
    {
        var models = LoadFixture("order-with-enum.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("} as const satisfies Record<string, Record<string, FieldConstraints>>", result);
    }

    [Fact]
    public void Contains_auto_generated_header()
    {
        var models = LoadFixture("order-with-enum.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.StartsWith("// <auto-generated> mdd-booster; DO NOT EDIT.</auto-generated>", result);
    }

    [Fact]
    public void Emits_label_for_field_with_label_string()
    {
        // order-with-group.m3l.md: name: string(50) @not_null @group("기본") "품목명"
        var models = LoadFixture("order-with-group.m3l.md");
        var result = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("label: '품목명'", result);
    }

    [Fact]
    public void Emits_group_for_field_with_group_attribute()
    {
        // order-with-group.m3l.md: @group("기본")
        var models = LoadFixture("order-with-group.m3l.md");
        var result = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("group: '기본'", result);
    }

    [Fact]
    public void Emits_entry_for_nullable_field_with_label_only()
    {
        // note: text? @group("기타") "메모" — nullable, no structural constraints, but has label+group
        var models = LoadFixture("order-with-group.m3l.md");
        var result = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("Note:", result);
        Assert.Contains("label: '메모'", result);
        Assert.Contains("group: '기타'", result);
    }

    [Fact]
    public void Skips_entity_with_no_constrained_fields()
    {
        // order-with-enum.m3l.md의 Order 엔티티에는 nullable fields (priority?, notes?)가 있음
        // priority는 enum 타입 nullable → required 아님, maxLength 없음 → 스키마에 들어가야 하지만
        // notes: text? → 제약 없음 → 이 필드 자체는 생략되어야 함
        // 하지만 엔티티 블록 생략은 모든 stored 필드에 제약이 없을 때만 발생
        // 순수하게 모든 필드가 nullable text인 가상 엔티티로 테스트
        var ast = new M3lAst
        {
            Models =
            [
                new ModelNode
                {
                    Name = "NullableOnly",
                    Type = ModelType.Model,
                    Fields =
                    [
                        new FieldNode
                        {
                            Name = "note",
                            Type = "text",
                            Nullable = true,
                            Kind = FieldKind.Stored,
                            Attributes = []
                        }
                    ]
                }
            ]
        };
        var models = new InterfaceResolver(ast).ResolveAll();

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.DoesNotContain("NullableOnly:", result);
    }
}
