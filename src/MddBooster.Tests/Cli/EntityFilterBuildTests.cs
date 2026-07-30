using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>includeEntities</c>/<c>excludeEntities</c> 의 엔드투엔드 계약 — `BuildCommand` 수준에서
/// 검증한다. 단위 테스트(<c>EntitySurfaceFilterTests</c>)가 술어를 고정하고, 여기서는
/// **빌드가 실제로 실패하는지**와 **산출물이 실제로 좁혀지는지**를 본다.
/// </summary>
public class EntityFilterBuildTests
{
    private const string Canon = """
# Namespace: test.filter

## Order

- id: identifier @pk @generated
- code: string(30) @not_null "코드"

---

## OrderItem

- id: identifier @pk @generated
- name: string(30) @not_null "품목명"

---

## ProductionWork

- id: identifier @pk @generated
- title: string(30) @not_null "작업명"

---

## ServiceClient @internal

- id: identifier @pk @generated
- secret: string(80) @not_null "시크릿"
""";

    /// <summary>Writes canon + mdd.json into a throwaway dir and returns its path.</summary>
    private static string Scaffold(string targetsJson, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-filter-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "api"));
        Directory.CreateDirectory(Path.Combine(root, "ts"));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Canon);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            $"{{\n  \"sources\": [\"./tables.m3l.md\"],\n  \"targets\": [{targetsJson}]\n}}");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void Include_narrows_the_generated_api_surface()
    {
        var mddDir = Scaffold(
            """{ "type": "Api", "projectPath": "../api", "namespace": "T.Server", "includeEntities": ["ProductionWork"] }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("ProductionWorkExt, ProductionWork", reg);
            Assert.DoesNotContain("OrderExt, Order", reg);
            Assert.DoesNotContain("OrderItem", reg);

            // 컨트롤러도 같은 범위를 따라야 한다 — 등록되지 않은 셋에 컨트롤러만 남으면 죽은 라우트다.
            var ctrl = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "Controllers_gen.cs"));
            Assert.Contains("ProductionWorksController", ctrl);
            Assert.DoesNotContain("OrdersController", ctrl);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Exclude_narrows_the_generated_api_surface()
    {
        var mddDir = Scaffold(
            """{ "type": "Api", "projectPath": "../api", "namespace": "T.Server", "excludeEntities": ["Order", "OrderItem"] }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("ProductionWorkExt, ProductionWork", reg);
            Assert.DoesNotContain("OrderExt, Order", reg);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void No_filter_keeps_current_behaviour()
    {
        var mddDir = Scaffold(
            """{ "type": "Api", "projectPath": "../api", "namespace": "T.Server" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("OrderExt, Order", reg);
            Assert.Contains("ProductionWorkExt, ProductionWork", reg);
            // @internal 은 필터와 무관하게 계속 제외된다.
            Assert.DoesNotContain("ServiceClient", reg);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void TypeScript_target_narrows_entity_derived_outputs_but_not_enums()
    {
        var mddDir = Scaffold(
            """{ "type": "TypeScript", "outputPath": "../ts", "excludeEntities": ["OrderItem"] }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var entities = File.ReadAllText(Path.Combine(root, "ts", "entities_gen.ts"));
            Assert.Contains("Order", entities);
            Assert.DoesNotContain("interface OrderItem", entities);

            var schema = File.ReadAllText(Path.Combine(root, "ts", "field_schema_gen.ts"));
            Assert.DoesNotContain("OrderItem", schema);

            // enums_gen.ts / enum_labels_gen.ts 는 생성되며 필터 대상이 아니다.
            Assert.True(File.Exists(Path.Combine(root, "ts", "enums_gen.ts")));
            Assert.True(File.Exists(Path.Combine(root, "ts", "enum_labels_gen.ts")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void EntitySetName_union_excludes_internal_entities()
    {
        // 이 목록은 OData entity set 이름의 미러이고 소비자가 이걸로 OData URL 을 만든다.
        // @internal 엔티티는 AddEntityPair 가 등록하지 않으므로 목록에 남으면
        // **타입세이프하게 404 경로를 광고**하는 셈이 된다.
        var mddDir = Scaffold(
            """{ "type": "TypeScript", "outputPath": "../ts" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var names = File.ReadAllText(Path.Combine(root, "ts", "entity_names_gen.ts"));
            Assert.Contains("'Orders'", names);
            Assert.DoesNotContain("ServiceClient", names);

            // 반면 인터페이스/폼은 @internal 을 존중하지 않는다 — 타입은 데이터 API 전용이 아니고
            // 전용 엔드포인트로 관리되는 인프라 엔티티에도 유용하다.
            var entities = File.ReadAllText(Path.Combine(root, "ts", "entities_gen.ts"));
            Assert.Contains("ServiceClient", entities);
        }
        finally { Cleanup(root); }
    }

    [Theory]
    // 둘 다 지정
    [InlineData("""{ "type": "Api", "projectPath": "../api", "namespace": "T.Server", "includeEntities": ["Order"], "excludeEntities": ["OrderItem"] }""")]
    // 미지 엔티티명
    [InlineData("""{ "type": "Api", "projectPath": "../api", "namespace": "T.Server", "includeEntities": ["Ordr"] }""")]
    // include 에 @internal
    [InlineData("""{ "type": "Api", "projectPath": "../api", "namespace": "T.Server", "includeEntities": ["ServiceClient"] }""")]
    // Sql 타깃에 필터 (FK/상속 무결성)
    [InlineData("""{ "type": "Sql", "projectPath": "../api", "excludeEntities": ["Order"] }""")]
    // Model 타깃에 필터
    [InlineData("""{ "type": "Model", "projectPath": "../api", "namespace": "T.E", "dbContextName": "TDb", "excludeEntities": ["Order"] }""")]
    public void Invalid_filter_config_fails_the_build(string targetJson)
    {
        var mddDir = Scaffold(targetJson, out var root);
        try
        {
            // 조용히 무시하거나 부분 적용하지 않는다 — 명시적 실패.
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }
}
