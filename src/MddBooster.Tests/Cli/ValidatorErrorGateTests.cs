using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// m3l 검증기의 «에러»는 빌드를 세운다 — 의미 분석 에러와 같은 종료 코드 3.
/// <para>
/// 표면화만 하고 진행하면, 정의되지 않은 타입 이름 같은 위반이 생성물에서 조용히
/// 다른 무엇(모델 참조)으로 바뀌어 나온다. 경고는 여전히 표면화만 한다.
/// </para>
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ValidatorErrorGateTests
{
    private static string Scaffold(string model, out string root, out string sqlDir)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-validator-gate-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        sqlDir = Path.Combine(root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(sqlDir);
        // The Sql target patches a .sqlproj; seeding one keeps a passing build possible.
        File.WriteAllText(Path.Combine(sqlDir, "Test.sqlproj"), """
<Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
  <PropertyGroup>
    <Name>Test</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./tables.m3l.md"],
  "targets": [ { "type": "Sql", "projectPath": "../db", "schema": "dbo" } ]
}
""");
        return mddDir;
    }

    // M3L-E010 on purpose: an error only the m3l validator knows about. An undefined type
    // would not do - this generator's own semantic analysis also rejects it, so the build
    // would stop with or without this gate and the test would prove nothing about it.
    [Fact]
    public void A_validator_error_stops_the_build_before_anything_is_generated()
    {
        var mddDir = Scaffold("""
# Namespace: test.gate

## Person
- id: identifier @pk

## Post
- id: identifier @pk
- author_id: identifier

### Relations
- >author
  - target: Person
  - from: author_id
""", out var root, out var sqlDir);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(3, new BuildCommand().Run(mddDir));

            Assert.Contains("[m3l] 에러 1건:", stderr.Text);
            Assert.Contains("[M3L-E010]", stderr.Text);
            Assert.Empty(Directory.EnumerateFiles(sqlDir, "*.sql", SearchOption.AllDirectories));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Every_numeric_width_builds_without_a_validator_diagnostic()
    {
        var mddDir = Scaffold("""
# Namespace: test.gate

## Reading
- id: identifier @pk
- level: byte
- count: short
- total: integer
- serial: long
- ratio: float
- value: double
""", out var root, out _);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.DoesNotContain("[m3l]", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
