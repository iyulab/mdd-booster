using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Generation;

/// <summary>
/// 타깃별 엔티티 부분집합 필터의 계약 테스트 — 세만틱 표 각 행이 케이스 하나다.
/// 원칙: **조용한 처리 금지.** 오타·모순 설정은 전부 위반으로 표면화하고, 필터가 없으면
/// 전량 통과(완전 하위호환)한다.
/// </summary>
public class EntitySurfaceFilterTests
{
    private static ResolvedModel Model(string name, params FieldAttribute[] attrs) => new()
    {
        Name = name,
        Fields = [new FieldNode
        {
            Name = "key", Type = "string", Kind = FieldKind.Stored, Nullable = false,
            Loc = new SourceLocation { File = "t.m3l.md", Line = 1 },
        }],
        Source = new ModelNode
        {
            Name = name, Type = ModelType.Model,
            Loc = new SourceLocation { File = "t.m3l.md" }, Attributes = [.. attrs],
        },
    };

    private static FieldAttribute Attr(string name) => new() { Name = name, Args = [] };

    private static readonly IReadOnlyList<ResolvedModel> All =
    [
        Model("Order"),
        Model("OrderItem"),
        Model("ProductionWork"),
        Model("ServiceClient", Attr("internal")),
    ];

    private static EntitySurfaceFilter Make(
        IReadOnlyList<string>? include, IReadOnlyList<string>? exclude, out IReadOnlyList<string> violations)
        => EntitySurfaceFilter.Validate(include, exclude, All, "Api 타깃(../Server)", out violations);

    // ---- 필터 없음 → 전량 통과 (완전 하위호환) ----

    [Fact]
    public void No_filter_passes_everything()
    {
        var f = Make(null, null, out var v);

        Assert.Empty(v);
        Assert.True(f.IsPassAll);
        Assert.Equal(All.Count, f.Apply(All).Count);
    }

    [Fact]
    public void Empty_lists_are_treated_as_no_filter()
    {
        // mdd.json 에 "includeEntities": [] 를 적어 표면이 텅 비는 것은 거의 확실히 실수다.
        // 빈 목록은 "필터 없음"으로 읽는다 — 의도적 전체 제외는 excludeEntities 로 표현하게 한다.
        var f = Make([], [], out var v);

        Assert.Empty(v);
        Assert.True(f.IsPassAll);
    }

    // ---- include / exclude 정상 동작 ----

    [Fact]
    public void Include_keeps_only_listed_entities()
    {
        var f = Make(["ProductionWork", "OrderItem"], null, out var v);

        Assert.Empty(v);
        Assert.Equal(["OrderItem", "ProductionWork"], f.Apply(All).Select(m => m.Name).OrderBy(n => n));
    }

    [Fact]
    public void Exclude_drops_only_listed_entities()
    {
        var f = Make(null, ["Order"], out var v);

        Assert.Empty(v);
        Assert.DoesNotContain("Order", f.Apply(All).Select(m => m.Name));
        Assert.Contains("OrderItem", f.Apply(All).Select(m => m.Name));
    }

    [Fact]
    public void Filter_preserves_input_order()
    {
        var f = Make(null, ["OrderItem"], out _);

        Assert.Equal(["Order", "ProductionWork", "ServiceClient"], f.Apply(All).Select(m => m.Name));
    }

    // ---- 오류 케이스 (조용한 처리 금지) ----

    [Fact]
    public void Both_lists_together_is_a_violation()
    {
        Make(["Order"], ["OrderItem"], out var v);

        Assert.Single(v);
        Assert.Contains("함께 지정할 수 없습니다", v[0]);
    }

    [Fact]
    public void Unknown_entity_name_is_a_violation_with_a_suggestion()
    {
        // 오타로 표면이 조용히 비는 것이 이 검증의 존재 이유다.
        Make(["ProductionWrok"], null, out var v);

        Assert.Single(v);
        Assert.Contains("ProductionWrok", v[0]);
        Assert.Contains("ProductionWork", v[0]);   // did-you-mean
    }

    [Fact]
    public void Unrecognizable_name_is_still_a_violation_without_a_suggestion()
    {
        Make(["Zzzzzzzzzzzzzzz"], null, out var v);

        Assert.Single(v);
        Assert.DoesNotContain("의도했나요", v[0]);
    }

    [Fact]
    public void All_violations_are_reported_at_once()
    {
        // 한 건씩 고쳐가며 재실행하게 만들지 않는다.
        Make(["Nope1", "Nope2"], null, out var v);

        Assert.Equal(2, v.Count);
    }

    [Fact]
    public void Including_an_internal_entity_is_a_violation()
    {
        // 조용히 드롭하면 소비자는 자기가 적은 이름이 왜 표면에 없는지 알 수 없다.
        Make(["ServiceClient"], null, out var v);

        Assert.Single(v);
        Assert.Contains("@internal", v[0]);
    }

    [Fact]
    public void Excluding_an_internal_entity_is_allowed()
    {
        // 이미 데이터 API 에서 빠지는 엔티티를 TS 산출물에서도 빼는 것은 모순이 아니다.
        Make(null, ["ServiceClient"], out var v);

        Assert.Empty(v);
    }

    [Fact]
    public void A_violating_config_yields_a_pass_all_filter_that_callers_must_not_use()
    {
        // 호출부는 위반이 있으면 빌드를 실패시켜야 한다. 부분 적용된 필터를 돌려주면
        // 실패를 무시한 호출부가 조용히 잘못된 산출물을 만든다.
        var f = Make(["Order"], ["OrderItem"], out var v);

        Assert.NotEmpty(v);
        Assert.True(f.IsPassAll);
    }

    // ---- 커버리지 회계 ----

    [Fact]
    public void Coverage_report_names_what_was_dropped()
    {
        var f = Make(["Order"], null, out _);

        var report = f.DescribeCoverage(All);

        Assert.Contains("포함 1개", report);
        Assert.Contains("제외 3개", report);
        Assert.Contains("ServiceClient", report);   // 무엇이 빠졌는지 이름까지
    }

    // ---- @internal 판정 정본 ----

    [Fact]
    public void IsInternal_reads_the_model_attribute()
    {
        Assert.True(EntitySurface.IsInternal(Model("X", Attr("internal"))));
        Assert.True(EntitySurface.IsInternal(Model("X", Attr("INTERNAL"))));   // 대소문자 무관
        Assert.False(EntitySurface.IsInternal(Model("X")));
    }
}
