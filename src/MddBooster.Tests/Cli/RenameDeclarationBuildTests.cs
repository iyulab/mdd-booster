using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A rename the model declares reaches a Sql target's <c>preScriptsPath</c> as a script, is refused when that path
/// lies inside the project a schema tool reads, and is reported (<c>MDD051</c>, promotable) when a Sql target has no
/// such path — the build never silently lets a rename read as a removal plus an addition.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class RenameDeclarationBuildTests
{
    private const string Model = """
# Namespace: test.rename

## CustomerOrder @renamed_from(SalesOrder)

- id: identifier @pk @generated
- total: decimal(12,2) @renamed_from(amount)
""";

    private (int Exit, string Stderr, string[] PreFiles) Build(string sqlTarget, string extraRoot = "")
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-rename-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{\n  \"sources\": [\"./tables.m3l.md\"],\n" + extraRoot + "  \"targets\": [" + sqlTarget + "]\n}");
        try
        {
            using var err = new ConsoleErrorCapture(this);
            var exit = new BuildCommand().Run(mddDir);
            var pre = Path.Combine(root, "pre");
            return (exit, err.Text, Directory.Exists(pre)
                ? Directory.GetFiles(pre).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!
                : []);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void A_declared_rename_is_written_to_the_pre_scripts_directory()
    {
        var (exit, stderr, files) = Build(
            """{ "type": "Sql", "dialect": "postgres", "projectPath": "../db", "preScriptsPath": "../pre" }""");
        Assert.True(exit == 0, stderr);
        Assert.Equal(
            ["V0001__rename_table_SalesOrder_to_CustomerOrder.sql", "V0002__rename_column_CustomerOrder_amount_to_total.sql"],
            files);
        Assert.DoesNotContain("MDD051", stderr);
    }

    [Fact]
    public void A_pre_scripts_directory_inside_the_project_is_a_configuration_error()
    {
        var (exit, stderr, _) = Build(
            """{ "type": "Sql", "dialect": "postgres", "projectPath": "../db", "preScriptsPath": "../db/pre" }""");
        Assert.Equal(4, exit);
        Assert.Contains("preScriptsPath", stderr);
    }

    [Fact]
    public void A_sql_target_without_a_pre_scripts_directory_says_the_renames_are_not_acted_on()
    {
        var (exit, stderr, files) = Build("""{ "type": "Sql", "dialect": "postgres", "projectPath": "../db" }""");
        Assert.Equal(0, exit);
        Assert.Contains("MDD051", stderr);
        Assert.Empty(files);
    }

    [Fact]
    public void Mdd051_can_be_promoted_to_an_error()
    {
        var (exit, stderr, _) = Build(
            """{ "type": "Sql", "dialect": "postgres", "projectPath": "../db" }""",
            extraRoot: "  \"warningsAsErrors\": [\"MDD051\"],\n");
        Assert.NotEqual(0, exit);
        Assert.Contains("MDD051", stderr);
    }
}
