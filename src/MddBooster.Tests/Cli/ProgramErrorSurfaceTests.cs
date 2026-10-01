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
    public void A_directory_without_mdd_json_is_a_configuration_error()
    {
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(4, exitCode);
            Assert.Contains("[config]", stderr.Text);
            Assert.Contains("mdd.json", stderr.Text);
            Assert.DoesNotContain("error:", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void A_source_mdd_json_names_that_does_not_exist_is_a_configuration_error_naming_every_one()
    {
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            { "sources": ["./tables.m3l.md", "./not-there.m3l.md", "./also-missing.m3l.md"], "targets": [ { "type": "TypeScript", "outputPath": "../ts" } ] }
            """);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(4, exitCode);
            Assert.Contains("[config]", stderr.Text);
            Assert.Contains("./not-there.m3l.md", stderr.Text);
            Assert.Contains("./also-missing.m3l.md", stderr.Text);
            Assert.DoesNotContain("./tables.m3l.md", stderr.Text);
            Assert.DoesNotContain("error:", stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    /// <summary>
    /// A model the parser refuses is a model error, like one the validator refuses: exit 3, one line
    /// per diagnostic — not the unexpected-failure exit 1 with every diagnostic joined into one line.
    /// </summary>
    [Fact]
    public void A_source_the_parser_refuses_is_a_model_error_with_one_line_per_diagnostic()
    {
        var mddDir = CreateTempDir();
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), """
            # Namespace: test.bank

            ## BankAccount : Missing1, Missing2
            - id: identifier @pk @generated
            """);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            { "sources": ["./tables.m3l.md"], "targets": [ { "type": "TypeScript", "outputPath": "../ts" } ] }
            """);

        using var stderr = new ConsoleErrorCapture(this);
        var exitCode = Program.Main(["build", mddDir]);

        try
        {
            Assert.Equal(3, exitCode);
            Assert.Contains("[m3l] 에러", stderr.Text);
            Assert.DoesNotContain("error:", stderr.Text);
            var diagnosticLines = stderr.Text.Split('\n').Count(l => l.TrimStart().StartsWith("[M3L-E"));
            Assert.True(diagnosticLines >= 2, stderr.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void An_untranslated_failure_is_one_line_and_exit_1()
    {
        using var stderr = new ConsoleErrorCapture(this);

        var exitCode = Program.ReportUnexpected(Thrown(new InvalidOperationException("boom")));

        Assert.Equal(1, exitCode);
        Assert.Equal("error: boom", stderr.Text.Trim());
    }

    [Fact]
    public void MDD_DEBUG_env_var_restores_the_stack_trace()
    {
        Environment.SetEnvironmentVariable("MDD_DEBUG", "1");
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            var exitCode = Program.ReportUnexpected(Thrown(new InvalidOperationException("boom")));
            Assert.Equal(1, exitCode);
            Assert.NotEqual("", FirstStackFrame(stderr.Text));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDD_DEBUG", null);
        }
    }

    /// <summary>An exception with a stack trace, as one that reached the top level has.</summary>
    private static Exception Thrown(Exception ex)
    {
        try { throw ex; }
        catch (Exception caught) { return caught; }
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
