using MddBooster.Cli;
using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A target the file system will not let the build write to is reported as that — which target,
/// what stopped it, and which targets this build had already rewritten — instead of the bare .NET
/// message the top-level catch used to print.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class OutputWriteFailureTests
{
    private const string Model = """
# Namespace: test.output

## Item

- id: identifier @pk @generated
- name: string(20)
- created_at: timestamp = now()
- updated_at: timestamp = now()
""";

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        File.WriteAllText(Path.Combine(root, "mdd", "tables.m3l.md"), Model);
        return root;
    }

    private (int Exit, string Stderr) Build(string root, string configJson)
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), configJson);
        using var err = new ConsoleErrorCapture(this);
        var exit = new BuildCommand().Run(Path.Combine(root, "mdd"));
        return (exit, err.Text);
    }

    /// <summary>Through the real entry point — a generator's validation refusal surfaces at its boundary.</summary>
    private (int Exit, string Stderr) BuildViaProgram(string root, string configJson)
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), configJson);
        using var err = new ConsoleErrorCapture(this);
        var exit = Program.Main(["build", Path.Combine(root, "mdd")]);
        return (exit, err.Text);
    }

    private static void Cleanup(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private const string TsThenModel = """
        { "sources": ["./tables.m3l.md"], "targets": [
          { "type": "TypeScript", "outputPath": "../ts" },
          { "type": "Model", "projectPath": "../model", "namespace": "N", "dbContextName": "Db" } ] }
        """;

    [Fact]
    public void A_read_only_output_file_names_the_target_and_the_targets_already_rewritten()
    {
        var root = NewRoot();
        try
        {
            Assert.Equal(0, Build(root, TsThenModel).Exit);
            var locked = Directory.EnumerateFiles(Path.Combine(root, "model"), "Item.cs", SearchOption.AllDirectories).Single();
            File.SetAttributes(locked, FileAttributes.ReadOnly);

            var (exit, stderr) = Build(root, TsThenModel);

            Assert.Equal(1, exit);
            Assert.Contains("[model] 출력을 쓰지 못했습니다", stderr);
            Assert.Contains("../model", stderr);
            Assert.Contains("읽기 전용", stderr);
            Assert.Contains("이미 새로 쓴 타깃: ts(../ts)", stderr);
            Assert.Contains("원문", stderr);   // the file path lives in the runtime's own message
            Assert.DoesNotContain("error: Access", stderr);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_missing_sqlproj_stops_the_build_before_any_target_writes()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = BuildViaProgram(root, """
                { "sources": ["./tables.m3l.md"], "targets": [
                  { "type": "TypeScript", "outputPath": "../ts" },
                  { "type": "Sql", "projectPath": "../db" } ] }
                """);

            Assert.NotEqual(0, exit);
            Assert.Contains(".sqlproj", stderr);
            Assert.Contains("emitSqlProj", stderr);
            Assert.False(Directory.Exists(Path.Combine(root, "ts")), "the TypeScript target wrote before the Sql target was refused");
            Assert.False(Directory.Exists(Path.Combine(root, "db")), "the Sql target created its output before being refused");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Without_emitSqlProj_no_sqlproj_is_needed()
    {
        var root = NewRoot();
        try
        {
            var (exit, _) = Build(root, """
                { "sources": ["./tables.m3l.md"], "targets": [ { "type": "Sql", "projectPath": "../db", "emitSqlProj": false } ] }
                """);
            Assert.Equal(0, exit);
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), "다른 프로그램이")]   // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021), "다른 프로그램이")]   // ERROR_LOCK_VIOLATION
    [InlineData(unchecked((int)0x80070070), "공간이")]            // ERROR_DISK_FULL
    [InlineData(0, "파일 시스템이 쓰기를 거절했습니다")]
    public void An_io_failure_is_described_by_its_cause(int hresult, string expected)
    {
        var text = OutputWriteFailure.Describe(new IOException("runtime words", hresult));
        Assert.Contains(expected, text);
        Assert.Contains("원문: runtime words", text);
    }

    [Fact]
    public void An_access_failure_is_described_as_permission_or_read_only()
    {
        var text = OutputWriteFailure.Describe(new UnauthorizedAccessException("runtime words"));
        Assert.Contains("읽기 전용", text);
        Assert.Contains("원문: runtime words", text);
    }
}
