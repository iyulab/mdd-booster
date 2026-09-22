using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>::aspect</c> 가 <b>실제 빌드</b>에서 통과하고, 그 규칙들이 실제로 발화한다.
/// </summary>
/// <remarks>
/// <para>
/// 분석기 단위 테스트는 규칙이 <em>존재</em>함을 증명하지만 <em>닿는다</em>는 것은 증명하지
/// 못한다 — 규칙을 세운 사이클에서 <c>MDD016</c> 이 아직 aspect 를 통째로 막고 있었고, 그
/// 상태로도 단위 테스트는 전부 초록이었다. 문을 여는 것과 규칙이 사용자에게 닿는 것은 다른
/// 사실이라 따로 잰다.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class AspectBuildTests
{
    private static string Scaffold(string model, out string root, out string sqlDir)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-aspect-build-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        sqlDir = Path.Combine(root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(sqlDir);
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

    private const string Conventional = """
# Namespace: test.aspect

## Asset
- id: identifier @pk @generated
- name: string(100)

## AssetMaintenanceProfile ::aspect(Asset)
- level: integer?
""";

    /// <summary>
    /// 문이 열렸다: 약속대로 쓴 aspect 는 빌드가 통과하고 테이블이 실제로 나간다.
    /// </summary>
    [Fact]
    public void A_conventional_aspect_builds_and_emits_its_table()
    {
        var mddDir = Scaffold(Conventional, out var root, out var sqlDir);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.DoesNotContain("MDD016", stderr.Text);

            var table = Directory
                .EnumerateFiles(sqlDir, "AssetMaintenanceProfile.sql", SearchOption.AllDirectories)
                .Single();
            Assert.Contains(
                "[AssetId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES [dbo].[Asset]([Id]) ON DELETE CASCADE",
                File.ReadAllText(table));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// negative control for the one above — <c>::subtype</c> is still refused, and refused by
    /// the same check that used to refuse both.
    /// </summary>
    [Fact]
    public void A_subtype_still_stops_the_build()
    {
        var mddDir = Scaffold(Conventional.Replace("::aspect(Asset)", "::subtype(Asset)"),
            out var root, out _);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(3, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD016", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 이름 약속 위반은 경고 — 빌드는 통과하되 그 사실이 실제로 보인다.
    /// </summary>
    [Fact]
    public void A_badly_named_aspect_builds_but_says_so()
    {
        var mddDir = Scaffold(
            Conventional.Replace("AssetMaintenanceProfile", "Contractor"), out var root, out _);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD019", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 작성자가 키를 선언하면 빌드가 선다.
    /// </summary>
    [Fact]
    public void An_aspect_that_declares_its_own_key_stops_the_build()
    {
        var mddDir = Scaffold(
            Conventional.Replace("- level: integer?", "- asset_id: identifier @pk\n- level: integer?"),
            out var root, out _);

        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(3, new BuildCommand().Run(mddDir));
            Assert.Contains("MDD021", stderr.Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
