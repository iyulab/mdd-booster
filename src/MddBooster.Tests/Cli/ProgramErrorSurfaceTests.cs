using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Cycle 101 — <c>Program.Main</c>'s top-level catch used to print the raw .NET stack trace
/// unconditionally, burying the useful first line (exception message, which already carries
/// JSON path/line/byte info for schema mismatches) under ~10 lines of framework frames. These
/// tests exercise that catch through the real CLI entry point (not <c>BuildCommand.Run</c>
/// directly) so a regression in <c>Program.cs</c> itself — not just in a generator — is caught.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ProgramErrorSurfaceTests
{
    private const string FixtureContent = """
        # Namespace: test.bank

        ## BankAccount
        - id: identifier @pk @generated
        - bank_name: string(50) @not_null "은행명"
        """;

    private static string FirstStackFrame(string output) =>
        output.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith("at ")) ?? "";

    [Fact]
    public void Malformed_targets_shape_prints_clean_message_no_stack_trace()
    {
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": ["Model"]
            }
            """);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(1, exitCode);
            Assert.Equal("", FirstStackFrame(stderr.Text));
            Assert.Contains("targets", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void Wrong_scalar_type_for_sources_prints_clean_message_no_stack_trace()
    {
        // Acceptance criterion 3 (ISSUE-mdd-booster-20260827-config-loader-raw-exception.md ⑷):
        // a *different* JSON schema mismatch must go through the same clean path, not just the
        // originally-reproduced targets-shape case.
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": "./tables.m3l.md",
              "targets": []
            }
            """);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(1, exitCode);
            Assert.Equal("", FirstStackFrame(stderr.Text));
            Assert.Contains("sources", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void Missing_sqlproj_prints_clean_message_no_stack_trace()
    {
        // Widened scope from the issue's ⑸ 재발견: SqlGenerator.FindSqlProj's FileNotFoundException
        // already carries a good Korean message, but Program.Main used to bury it the same way.
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Sql", "projectPath": "../db", "schema": "dbo" }
              ]
            }
            """);
        Directory.CreateDirectory(Path.Combine(mddDir, "..", "db"));

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(1, exitCode);
            Assert.Equal("", FirstStackFrame(stderr.Text));
            Assert.Contains(".sqlproj", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void Valid_config_still_exits_zero_with_no_stderr()
    {
        var mddDir = CreateTempDir();
        var dbDir = Path.Combine(mddDir, "..", "db");
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(dbDir, "Test.sqlproj"), """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup><Name>Test</Name></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Sql", "projectPath": "../db", "schema": "dbo" }
              ]
            }
            """);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(0, exitCode);
            Assert.Equal("", stderr.Text.Trim());
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void MDD_DEBUG_env_var_restores_the_stack_trace()
    {
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": ["Model"]
            }
            """);

        Environment.SetEnvironmentVariable("MDD_DEBUG", "1");
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            var exitCode = Program.Main(["build", mddDir]);
            Assert.Equal(1, exitCode);
            Assert.NotEqual("", FirstStackFrame(stderr.Text));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDD_DEBUG", null);
            Cleanup(mddDir);
        }
    }

    private static string CreateTempDir()
    {
        var mddDir = Path.Combine(Path.GetTempPath(), $"mdd-program-{Guid.NewGuid():N}", "mdd");
        Directory.CreateDirectory(mddDir);
        return mddDir;
    }

    private static void Cleanup(string mddDir)
    {
        try { Directory.Delete(Path.GetDirectoryName(mddDir)!, recursive: true); } catch { /* best effort */ }
    }
}
