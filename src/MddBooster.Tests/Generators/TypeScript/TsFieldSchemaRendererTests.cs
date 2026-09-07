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

    // ── 읽기(파생) 필드 ─────────────────────────────────────────────────────────
    // entities_gen.ts 는 lookup/rollup/computed 를 타입에 싣고 C# Ext 클래스도
    // [Display(Name=…)] 를 내는데, 이 렌더러만 FieldKind.Stored 로 좁혀 놓아
    // 목록 표가 그리는 바로 그 필드들의 라벨 원천이 없었다.

    [Fact]
    public void Derived_lookup_field_is_emitted_with_its_label_and_read_only_marker()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // customer_name: string @lookup(customer_id.name) "고객명"
        Assert.Contains("CustomerName: { label: '고객명', derived: 'lookup', readOnly: true },", result);
    }

    [Fact]
    public void Derived_rollup_and_computed_fields_carry_their_own_kind()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("ItemCount: { derived: 'rollup', readOnly: true },", result);
        Assert.Contains("TaxAmount: { derived: 'computed', readOnly: true },", result);
    }

    /// <summary>
    /// `required` 는 「사용자가 반드시 채워야 한다」는 쓰기 축 진술이다. 파생 필드는 애초에
    /// 쓰이지 않으므로, 원천 컬럼이 non-null 이라 nullable=false 로 해석되더라도 그것을
    /// required 로 옮겨 적으면 소비자에게 거짓말이 된다.
    /// </summary>
    [Fact]
    public void Derived_field_never_claims_required()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.DoesNotContain("CustomerName: { required", result);
        Assert.DoesNotContain("required: true, label: '고객명'", result);
    }

    /// <summary>
    /// 라벨 없는 파생 필드도 실린다 — `derived`/`readOnly` 자체가 소비자가 알아야 할
    /// 정보이고, 「이 필드가 스키마에 존재하는 것」이 이 요청의 핵심이었다.
    /// 저장 필드의 기존 HasAny 게이트(제약이 하나도 없으면 생략)는 그대로 둔다.
    /// </summary>
    [Fact]
    public void Derived_field_without_a_label_still_appears()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // customer_email: string @lookup(customer_id.email) — 라벨 없음
        Assert.Contains("CustomerEmail: { derived: 'lookup', readOnly: true },", result);
    }

    // ── @lookup 라벨 상속 ───────────────────────────────────────────────────────
    // 대상 필드가 이미 라벨을 갖고 있으면 같은 문자열을 두 번 적을 이유가 없다.
    // rollup/computed 는 상속하지 않는다 — 집계식·표현식에는 이름을 줄 「대상 필드」가 없다.

    private const string LookupInheritanceModel = """
        ## Customer
        - id: identifier @pk @generated
        - name: string(50) @not_null "고객 상호"
        - email: string(100) @not_null

        ## Order
        - id: identifier @pk @generated
        - customer_id: identifier @reference(Customer) @not_null
        - customer_name: string @lookup(customer_id.name)
        - customer_email: string @lookup(customer_id.email)
        - buyer: string @lookup(customer_id.name) "매입처"
        """;

    [Fact]
    public void Lookup_without_its_own_label_inherits_the_target_field_label()
    {
        var models = LoadInline(LookupInheritanceModel);

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("CustomerName: { label: '고객 상호', derived: 'lookup', readOnly: true },", result);
    }

    [Fact]
    public void An_explicit_label_wins_over_the_inherited_one()
    {
        var models = LoadInline(LookupInheritanceModel);

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("Buyer: { label: '매입처', derived: 'lookup', readOnly: true },", result);
    }

    /// <summary>
    /// 대상에도 라벨이 없으면 아무것도 지어내지 않는다 — PascalCase 필드명으로 채우는 폴백은
    /// 「사람이 쓴 의미 있는 텍스트」라는 label 의 뜻을 깬다(ExtractConstraints 의 기존 근거).
    /// </summary>
    [Fact]
    public void Nothing_is_invented_when_the_target_has_no_label_either()
    {
        var models = LoadInline(LookupInheritanceModel);

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("CustomerEmail: { derived: 'lookup', readOnly: true },", result);
    }

    [Fact]
    public void Rollup_and_computed_do_not_inherit_a_label()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        // item_count @rollup(OrderItem.order_id, count) — 집계에는 대상 「필드」가 없다
        Assert.Contains("ItemCount: { derived: 'rollup', readOnly: true },", result);
        // tax_amount @computed(`subtotal * 0.1`) — 표현식도 마찬가지
        Assert.Contains("TaxAmount: { derived: 'computed', readOnly: true },", result);
    }

    [Fact]
    public void FieldConstraints_type_declares_the_two_new_members()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("derived?: 'lookup' | 'rollup' | 'computed'", result);
        Assert.Contains("readOnly?: true", result);
    }

    /// <summary>
    /// 저장 필드의 방출은 한 글자도 바뀌지 않는다 — 이 변경은 순증이다.
    /// </summary>
    [Fact]
    public void Stored_field_emission_is_unchanged_by_the_derived_addition()
    {
        var models = LoadFixture("order-with-derived.m3l.md");

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("OrderNumber: { required: true, maxLength: 30 },", result);
    }
}
