using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Api;

namespace MddBooster.Tests.Generators.Api;

/// <summary>
/// 생성기는 엔티티셋 목록을 이미 내고 있었지만 권한 차원이 없어, 소비앱이 같은 목록을 손으로
/// 한 벌 더 유지했다. 그 사본에서 한 줄이 빠지면 빌드·타입·테스트·응답 어디에도 안 나타나고
/// 그 엔드포인트는 인증만 통과한 아무나 읽는다. 여기서 고정하는 것은 그 사본을 없애는 계약이다.
/// </summary>
public class PermissionsRendererTests
{
    private static IReadOnlyList<ResolvedModel> LoadInline(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-perm-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll();
        }
        finally { File.Delete(tmp); }
    }

    private const string Model = """
        ## Order
        - id: identifier @pk @generated
        - code: string(30) @not_null

        ## ServiceClient @internal
        - id: identifier @pk @generated
        - secret: string(80) @not_null
        """;

    [Fact]
    public void Emits_a_constant_per_entity_set_and_verb()
    {
        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");

        Assert.Contains("public const string OrdersRead = \"orders.read\";", result);
        Assert.Contains("public const string OrdersWrite = \"orders.write\";", result);
    }

    [Fact]
    public void Emits_the_entity_set_to_read_write_default_map()
    {
        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");

        Assert.Contains("[\"Orders\"] = (OrdersRead, OrdersWrite),", result);
    }

    /// <summary>
    /// `@internal` 엔티티는 데이터 API 표면이 없다 — 등록 파일이 `AddEntityPair` 를 내지 않는
    /// 그 엔티티에 권한 키를 내면 존재하지 않는 엔드포인트를 이름 짓는 것이 된다.
    /// </summary>
    [Fact]
    public void Skips_internal_entities_the_same_way_the_registration_file_does()
    {
        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");

        Assert.DoesNotContain("ServiceClient", result);
    }

    [Theory]
    [InlineData("{entitySetLower}.{verb}", "orders.read")]
    [InlineData("{entitySet}.{Verb}", "Orders.Read")]
    [InlineData("perm:{entitySetLower}:{verb}", "perm:orders:read")]
    public void The_template_is_input_not_a_convention_this_generator_picks(string template, string expected)
    {
        Assert.Equal(expected, PermissionKeyTemplate.Expand(template, "Orders", "read"));
    }

    /// <summary>
    /// 오타난 자리표시자를 그대로 통과시키면 어떤 정책과도 매칭되지 않는 리터럴 키가 조용히
    /// 나간다 — 빌드를 세우는 것만이 「동작 중」으로 오인될 수 없는 결과다.
    /// </summary>
    [Fact]
    public void An_unknown_placeholder_is_reported_rather_than_passed_through()
    {
        Assert.Equal(["entityset"], PermissionKeyTemplate.Validate("{entityset}.{verb}"));
        Assert.Empty(PermissionKeyTemplate.Validate("{entitySetLower}.{verb}"));
    }

    /// <summary>
    /// 런타임의 짝은 `RestrictPolicy(setName, readPolicy, writePolicy)` 다. delete 를 세 번째
    /// 동사로 내면 이 키를 소비하는 쪽이 하지 않는 구분을 발명하게 된다.
    /// </summary>
    [Fact]
    public void Only_read_and_write_are_emitted_matching_the_runtimes_policy_pair()
    {
        Assert.Equal(["read", "write"], PermissionsRenderer.Verbs);

        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");
        Assert.DoesNotContain("delete", result);
    }
}
