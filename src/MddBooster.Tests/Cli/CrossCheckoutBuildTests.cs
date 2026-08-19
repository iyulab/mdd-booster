using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// End-to-end coverage for the cross-checkout warning (<see cref="MddBooster.Core.Generation.ConfiguredPathResolver"/>)
/// through an actual <see cref="BuildCommand"/> run — the unit tests in
/// <c>MddBooster.Tests.Generation</c> pin the resolver's own contract, this
/// pins that every generator actually wires it in.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class CrossCheckoutBuildTests
{
    private const string Model =
        "# Namespace: X\n\n" +
        "## Product\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n";

    // Model targets additionally require the Timestampable contract (created_at/updated_at).
    private const string ModelWithTimestamps =
        "# Namespace: X\n\n" +
        "## Product\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n" +
        "- created_at: timestamp @not_null\n" +
        "- updated_at: timestamp @not_null\n";

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void Sql_target_with_absolute_projectPath_in_another_checkout_warns()
    {
        // Simulates the reported layout: `mdd.json` lives in checkout A (e.g. a
        // linked git worktree) but its Sql target's `projectPath` is pinned to
        // an absolute location that actually lives in checkout B (e.g. the main
        // checkout) — a config that was presumably correct when first written,
        // and silently stays wrong once a second checkout enters the picture.
        var checkoutA = Path.Combine(Path.GetTempPath(), $"mdd-xchk-A-{Guid.NewGuid():N}");
        var checkoutB = Path.Combine(Path.GetTempPath(), $"mdd-xchk-B-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(checkoutA, ".git"));
            Directory.CreateDirectory(Path.Combine(checkoutB, ".git"));

            var mddDir = Path.Combine(checkoutA, "mdd");
            Directory.CreateDirectory(mddDir);
            File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);

            var dbDir = Path.Combine(checkoutB, "src", "X.Database");
            Directory.CreateDirectory(dbDir);

            var dbDirJson = dbDir.Replace("\\", "\\\\");
            File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "{{dbDirJson}}", "schema": "dbo", "emitSqlProj": false }
  ]
}
""");

            using var stderr = new ConsoleErrorCapture(this);
            int exit;
            try
            {
                exit = new BuildCommand().Run(mddDir);
            }
            finally
            {
                var text = stderr.Text;
                Assert.Contains("다른 작업트리", text);
                Assert.Contains("projectPath", text);
            }

            Assert.Equal(0, exit);   // 경고이지 오류가 아니다 — 산출물은 그대로 쓰인다
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "Product.sql")));
        }
        finally
        {
            Cleanup(checkoutA);
            Cleanup(checkoutB);
        }
    }

    [Fact]
    public void Sql_target_with_relative_projectPath_in_the_same_checkout_stays_silent()
    {
        var checkout = Path.Combine(Path.GetTempPath(), $"mdd-xchk-same-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(checkout, ".git"));
            var mddDir = Path.Combine(checkout, "mdd");
            Directory.CreateDirectory(mddDir);
            File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);
            File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo", "emitSqlProj": false }
  ]
}
""");

            using var stderr = new ConsoleErrorCapture(this);
            int exit;
            try
            {
                exit = new BuildCommand().Run(mddDir);
            }
            finally
            {
                Assert.DoesNotContain("다른 작업트리", stderr.Text);
            }

            Assert.Equal(0, exit);
        }
        finally { Cleanup(checkout); }
    }

    [Fact]
    public void Model_target_with_absolute_sqlProjectPath_in_another_checkout_warns()
    {
        // sqlProjectPath is read-only (it scans the sibling Sql project for
        // hand-authored *ExtView.sql files) rather than written to, but pointing
        // it at the wrong checkout is the same silent-cross-checkout risk: the
        // scan would read that *other* checkout's custom views and could change
        // which models get ExtBacking.Ext, unnoticed.
        var checkoutA = Path.Combine(Path.GetTempPath(), $"mdd-xchk-mA-{Guid.NewGuid():N}");
        var checkoutB = Path.Combine(Path.GetTempPath(), $"mdd-xchk-mB-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(checkoutA, ".git"));
            Directory.CreateDirectory(Path.Combine(checkoutB, ".git"));

            var mddDir = Path.Combine(checkoutA, "mdd");
            Directory.CreateDirectory(mddDir);
            File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), ModelWithTimestamps);

            var modelDir = Path.Combine(checkoutA, "src", "X.Entities");
            Directory.CreateDirectory(modelDir);
            var otherSqlDir = Path.Combine(checkoutB, "src", "X.Database");
            Directory.CreateDirectory(otherSqlDir);

            var otherSqlDirJson = otherSqlDir.Replace("\\", "\\\\");
            File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    {
      "type": "Model",
      "projectPath": "../src/X.Entities",
      "namespace": "X.Entities",
      "dbContextName": "XDbContext",
      "sqlProjectPath": "{{otherSqlDirJson}}"
    }
  ]
}
""");

            using var stderr = new ConsoleErrorCapture(this);
            int exit;
            try
            {
                exit = new BuildCommand().Run(mddDir);
            }
            finally
            {
                var text = stderr.Text;
                Assert.Contains("다른 작업트리", text);
                Assert.Contains("sqlProjectPath", text);
            }

            Assert.Equal(0, exit);
        }
        finally
        {
            Cleanup(checkoutA);
            Cleanup(checkoutB);
        }
    }
}
