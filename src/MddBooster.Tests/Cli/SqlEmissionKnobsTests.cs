using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Validates the independent Sql-target emission knobs (emitSqlProj / emitRefreshScript).
/// Default (both true) behavior is covered by <see cref="ViewPipelineE2ETests"/>; these tests
/// pin the non-default combinations and backward compatibility.
/// </summary>
public class SqlEmissionKnobsTests
{
    // Product (soft-delete + lookup) → ProductUdView + ProductFullView, exercising the
    // Views_gen patch and the RefreshViews script.
    private const string Model =
        "# Namespace: X\n\n" +
        "## Product\n" +
        "- id: identifier @pk @generated\n" +
        "- cat_id: identifier @reference(Category) @not_null\n" +
        "- price: decimal(12,2) @not_null\n" +
        "- deleted_at: timestamp\n" +
        "- cat_name: string @lookup(cat_id.name)\n\n" +
        "## Category\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n";

    private static (string mddDir, string dbDir) Scaffold(string tag)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-knobs-{tag}-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "X.Database");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);
        return (mddDir, dbDir);
    }

    [Fact]
    public void EmitSqlProj_false_builds_without_any_sqlproj_and_still_generates_sql()
    {
        var (mddDir, dbDir) = Scaffold("nosqlproj");
        // Note: no .sqlproj is created at all — the point of emitSqlProj:false is that a
        // consumer who has retired the .sqlproj can still run `mdd build`.
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo", "emitSqlProj": false }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            // Desired-state SQL is still emitted for Schemorph to consume.
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "Product.sql")));
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Views_gen", "ProductFullView.sql")));

            // emitRefreshScript defaults to true → RefreshViews.sql is still emitted.
            Assert.True(File.Exists(
                Path.Combine(dbDir, "dbo", "Scripts_gen", "Script.PostDeployment.RefreshViews.sql")));

            // No .sqlproj was created or resurrected.
            Assert.Empty(Directory.GetFiles(dbDir, "*.sqlproj"));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dbDir)!)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EmitRefreshScript_false_skips_script_and_leaves_no_dangling_sqlproj_reference()
    {
        var (mddDir, dbDir) = Scaffold("norefresh");
        File.WriteAllText(Path.Combine(dbDir, "X.sqlproj"),
            "<Project Sdk=\"Microsoft.Build.Sql/0.2.5-preview\"><PropertyGroup><Name>X</Name></PropertyGroup></Project>");

        // emitSqlProj stays true (patch runs); emitRefreshScript false — the Phase-3 pre-seed combo.
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo", "emitRefreshScript": false }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            // RefreshViews.sql must not be emitted.
            Assert.False(File.Exists(
                Path.Combine(dbDir, "dbo", "Scripts_gen", "Script.PostDeployment.RefreshViews.sql")));

            var sqlProj = File.ReadAllText(Path.Combine(dbDir, "X.sqlproj"));
            // Tables/Views are still patched...
            Assert.Contains("dbo\\Tables_gen\\Product.sql", sqlProj);
            Assert.Contains("dbo\\Views_gen\\ProductFullView.sql", sqlProj);
            // ...but the Scripts_gen reference must NOT dangle in the .sqlproj.
            Assert.DoesNotContain("Scripts_gen", sqlProj);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dbDir)!)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EmitEnumCheckConstraints_true_emits_table_level_check()
    {
        var (mddDir, dbDir) = Scaffold("enumcheck");
        // enum 컬럼을 가진 전용 모델 — 기본(off) 동작은 BuildCommandFullFixtureTests가
        // DoesNotContain("CHECK")로 고정하고 있으므로, 여기서는 opt-in 경로만 검증한다.
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"),
            "# Namespace: X\n\n" +
            "## DeviceStatus ::enum\n" +
            "- active: \"활성\"\n" +
            "- retired: \"퇴역\"\n\n" +
            "## Device\n" +
            "- id: identifier @pk @generated\n" +
            "- status: DeviceStatus @not_null\n");
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo", "emitSqlProj": false, "emitEnumCheckConstraints": true }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var deviceSql = File.ReadAllText(Path.Combine(dbDir, "dbo", "Tables_gen", "Device.sql"));
            Assert.Contains(
                "CONSTRAINT [CK_Device_Status] CHECK ([Status] IN (N'active', N'retired'))",
                deviceSql);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dbDir)!)!, recursive: true); } catch { }
        }
    }
}
