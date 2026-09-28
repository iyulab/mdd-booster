using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A target that refuses the model or its own configuration during validation stops the build with
/// the exit code of that kind of error — 3 for the model, 4 for the configuration — under the
/// target's name, instead of the generic <c>error:</c> line and exit 1 of an unexpected failure.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class GeneratorRefusalExitTests
{
    private static string NewRoot(string model)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-refusal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        File.WriteAllText(Path.Combine(root, "mdd", "tables.m3l.md"), model);
        return root;
    }

    private (int Exit, string Stderr) Build(string root, string configJson)
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), configJson);
        using var err = new ConsoleErrorCapture(this);
        var exit = Program.Main(["build", Path.Combine(root, "mdd")]);
        return (exit, err.Text);
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void A_model_the_Model_target_refuses_is_a_model_error()
    {
        var root = NewRoot("""
            # Namespace: t

            ## Item
            - id: identifier @pk @generated
            - name: string(20)
            """);
        try
        {
            var (exit, stderr) = Build(root, """
                { "sources": ["./tables.m3l.md"], "targets": [
                  { "type": "TypeScript", "outputPath": "../ts" },
                  { "type": "Model", "projectPath": "../model", "namespace": "N", "dbContextName": "Db" } ] }
                """);

            Assert.Equal(3, exit);
            Assert.Contains("[model] 오류", stderr);
            Assert.Contains("created_at", stderr);
            Assert.DoesNotContain("error:", stderr);
            Assert.False(Directory.Exists(Path.Combine(root, "ts")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_delete_path_SQL_Server_refuses_is_a_model_error()
    {
        var root = NewRoot("""
            # Namespace: t

            ## Category
            - id: identifier @pk
            - parent_id: identifier? @reference(Category)?
            """);
        try
        {
            var (exit, stderr) = Build(root, """
                { "sources": ["./tables.m3l.md"], "targets": [ { "type": "Sql", "projectPath": "../db", "emitSqlProj": false } ] }
                """);

            Assert.Equal(3, exit);
            Assert.Contains("[sql] 오류", stderr);
            Assert.Contains("1785", stderr);
            Assert.DoesNotContain("error:", stderr);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_missing_sqlproj_is_a_configuration_error()
    {
        var root = NewRoot("""
            # Namespace: t

            ## Item
            - id: identifier @pk
            """);
        try
        {
            var (exit, stderr) = Build(root, """
                { "sources": ["./tables.m3l.md"], "targets": [ { "type": "Sql", "projectPath": "../db" } ] }
                """);

            Assert.Equal(4, exit);
            Assert.Contains("[sql] 설정 오류", stderr);
            Assert.Contains(".sqlproj", stderr);
            Assert.DoesNotContain("error:", stderr);
        }
        finally { Cleanup(root); }
    }
}
