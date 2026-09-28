using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>Program.Main</c>'s top-level catch used to print the raw .NET stack trace unconditionally,
/// burying the useful first line under ~10 lines of framework frames. These tests go through the
/// real CLI entry point (not <c>BuildCommand.Run</c> directly) so a regression in <c>Program.cs</c>
/// itself — not just in a generator — is caught. A malformed <c>mdd.json</c> no longer reaches that
/// catch at all: it is a configuration error, reported with the file, line and member, exit code 4.
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
    public void Malformed_targets_shape_is_a_configuration_error_with_no_stack_trace()
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
            Assert.Equal(4, exitCode);
            Assert.Equal("", FirstStackFrame(stderr.Text));
            Assert.Contains("mdd.json", stderr.Text);
            Assert.Contains("항목 targets[0]", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void Wrong_scalar_type_for_sources_is_a_configuration_error_with_no_stack_trace()
    {
        // A *different* JSON schema mismatch must go through the same path, not just the
        // targets-shape case.
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
            Assert.Equal(4, exitCode);
            Assert.Equal("", FirstStackFrame(stderr.Text));
            Assert.Contains("항목 sources", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void Missing_sqlproj_prints_clean_message_no_stack_trace()
    {
        // SqlGenerator.FindSqlProj's message was always good, but Program.Main used to bury it under
        // a stack trace. It is now a configuration refusal: exit code 4, under the target's name.
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
            Assert.Equal(4, exitCode);
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
        // A failure that still reaches the top-level catch: a directory with no mdd.json. (A missing
        // .sqlproj used to serve here; it is now a configuration refusal with its own exit code.)
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent);

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
