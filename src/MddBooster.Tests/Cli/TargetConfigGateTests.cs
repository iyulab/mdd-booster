using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// 복수 타깃 설정의 조용한 오출력을 막는 게이트. 두 결함을 고정한다 —
/// ① Model 타깃이 둘일 때 둘째 namespace가 조용히 무시되던 것,
/// ② 같은 종류·같은 경로 타깃이 둘일 때 나중 것이 앞선 것을 조용히 덮어쓰던 것.
/// <para>
/// 둘 다 "빌드는 성공하는데 산출물이 틀린" 계열이라 소비자가 컴파일 오류를 만난 뒤에야
/// 원인을 찾게 된다 — 그래서 빌드 시점 오류로 올렸다.
/// </para>
/// </summary>
public class TargetConfigGateTests
{
    // Model 타깃은 타임스탬프 계약을 요구한다(IyuEntity 가 CreatedAt/UpdatedAt 을 항상 매핑) —
    // 두 필드를 직접 선언해 게이트를 만족시킨다.
    private const string Canon = """
# Namespace: test.gate

## Order

- id: identifier @pk @generated
- code: string(30) @not_null "코드"
- created_at: timestamp @not_null "생성시각"
- updated_at: timestamp @not_null "수정시각"
""";

    private static string Scaffold(string targetsJson, out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-gate-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        foreach (var d in new[] { "api", "api2", "ent", "ent2", "ts" })
            Directory.CreateDirectory(Path.Combine(root, d));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Canon);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            $"{{\n  \"sources\": [\"./tables.m3l.md\"],\n  \"targets\": [{targetsJson}]\n}}");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private const string ModelA =
        """{ "type": "Model", "projectPath": "../ent", "namespace": "A.Entities", "dbContextName": "ADb" }""";
    private const string ModelB =
        """{ "type": "Model", "projectPath": "../ent2", "namespace": "B.Entities", "dbContextName": "BDb" }""";

    // ---- ① Model 타깃 복수 시 namespace 추론 ----

    [Fact]
    public void Two_model_targets_without_explicit_entitiesNamespace_fails_the_build()
    {
        // 과거 동작: FirstOrDefault(Model) 이 A.Entities 를 집고 B.Entities 를 조용히 버렸다.
        var mddDir = Scaffold(
            $$"""{{ModelA}}, {{ModelB}}, { "type": "Api", "projectPath": "../api", "namespace": "A.Server" }""",
            out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Two_model_targets_with_explicit_entitiesNamespace_succeeds_and_uses_it()
    {
        var mddDir = Scaffold(
            $$"""{{ModelA}}, {{ModelB}}, { "type": "Api", "projectPath": "../api", "namespace": "B.Server", "entitiesNamespace": "B.Entities" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            // 명시한 쪽을 쓴다 — 첫 Model 타깃이 아니다.
            Assert.Contains("using B.Entities;", reg);
            Assert.DoesNotContain("using A.Entities;", reg);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Single_model_target_still_infers_the_namespace()
    {
        // 하위호환: 기존 소비자(Model 1개)는 아무것도 명시하지 않아도 그대로 동작한다.
        var mddDir = Scaffold(
            $$"""{{ModelA}}, { "type": "Api", "projectPath": "../api", "namespace": "A.Server" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("using A.Entities;", reg);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Explicit_entitiesNamespace_overrides_inference_even_with_one_model_target()
    {
        var mddDir = Scaffold(
            $$"""{{ModelA}}, { "type": "Api", "projectPath": "../api", "namespace": "X.Server", "entitiesNamespace": "Custom.Entities" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var reg = File.ReadAllText(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("using Custom.Entities;", reg);
        }
        finally { Cleanup(root); }
    }

    // ---- ② 중복 타깃 ----

    [Theory]
    [InlineData("""{ "type": "Api", "projectPath": "../api", "namespace": "A.Server" }, { "type": "Api", "projectPath": "../api", "namespace": "B.Server" }""")]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../ts" }, { "type": "TypeScript", "outputPath": "../ts" }""")]
    public void Same_type_and_same_path_fails_the_build(string targetsJson)
    {
        var mddDir = Scaffold(targetsJson, out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Same_path_expressed_differently_is_still_detected()
    {
        // 경로를 정규화해 비교한다 — "../api" 와 "../ent/../api" 는 같은 디렉터리다.
        var mddDir = Scaffold(
            """{ "type": "Api", "projectPath": "../api", "namespace": "A.Server" }, { "type": "Api", "projectPath": "../ent/../api", "namespace": "B.Server" }""",
            out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Same_type_with_different_paths_is_allowed()
    {
        // 복수 서버 시나리오의 핵심 — 같은 종류 타깃 2개가 서로 다른 프로젝트로 나가는 것은 정상이다.
        var mddDir = Scaffold(
            $$"""{{ModelA}}, { "type": "Api", "projectPath": "../api", "namespace": "A.Server" }, { "type": "Api", "projectPath": "../api2", "namespace": "A.MesServer", "includeEntities": ["Order"] }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.True(File.Exists(Path.Combine(root, "api", "Api_gen", "ApiRegistration_gen.cs")));
            Assert.True(File.Exists(Path.Combine(root, "api2", "Api_gen", "ApiRegistration_gen.cs")));
        }
        finally { Cleanup(root); }
    }
}
