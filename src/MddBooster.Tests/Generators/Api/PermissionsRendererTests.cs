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
    /// 런타임은 read·write·delete 세 정책을 구분해 받는다. 셋 다 상수로 낸다 — 소비앱이
    /// 「고칠 수 있는 사람」과 「지울 수 있는 사람」을 가르는 곳에서 그 키를 손으로 적지 않도록.
    /// </summary>
    [Fact]
    public void Read_write_and_delete_constants_are_all_emitted()
    {
        Assert.Equal(["read", "write", "delete"], PermissionsRenderer.Verbs);

        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");
        Assert.Contains("OrdersRead = \"orders.read\"", result);
        Assert.Contains("OrdersWrite = \"orders.write\"", result);
        Assert.Contains("OrdersDelete = \"orders.delete\"", result);
    }

    /// <summary>
    /// 🔴 기본 매핑에는 delete 를 «싣지 않는다». 런타임의 기본값은 「삭제는 write 정책이
    /// 지배한다」이고, 어느 셋이 그 둘을 가르는지는 소비앱 결정이다 — 매핑에 delete 를 넣으면
    /// 생성기가 **모든 셋에 대해 그 결정을 대신 내리는** 것이 되고, 그건 키 «형식»을 입력으로
    /// 둔 것과 같은 경계를 반대편에서 넘는 것이다. 상수는 예외를 얹을 자리에 쓰라고 낸다.
    /// </summary>
    [Fact]
    public void The_default_map_carries_read_and_write_only()
    {
        var result = PermissionsRenderer.Render(LoadInline(Model), "T.Server", "{entitySetLower}.{verb}");

        Assert.Contains("IReadOnlyDictionary<string, (string Read, string Write)> Default", result);
        Assert.Contains("[\"Orders\"] = (OrdersRead, OrdersWrite),", result);
        Assert.DoesNotContain("OrdersDelete),", result);
    }
}
