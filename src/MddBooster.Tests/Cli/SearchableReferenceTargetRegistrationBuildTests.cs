using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A <c>@searchable</c> reference whose target set no Api target registers: the runtime's <c>$search</c>
/// skips a navigation to an unregistered type, so the declaration would match nothing — and the build
/// says so (<c>MDD035</c>) rather than leaving it to an empty search result.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class SearchableReferenceTargetRegistrationBuildTests
{
    private const string Base = """
# Namespace: test.split

## Category

- id: identifier @pk @generated
- name: string(50) @searchable
""";

    private const string Module = """
# Namespace: test.split
# Prefix: ops

## OpsAsset

- id: identifier @pk @generated
- title: string(100)
- category_id: identifier? @reference(Category) @searchable
""";

    private (int Exit, string Stderr) Build(string targetsJson)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-searchable-target-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "base.m3l.md"), Base);
        File.WriteAllText(Path.Combine(mddDir, "ops.m3l.md"), Module);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{\n  \"sources\": [\"./base.m3l.md\", \"./ops.m3l.md\"],\n  \"targets\": [" + targetsJson + "]\n}");
        try
        {
            using var err = new ConsoleErrorCapture(this);
            var exit = new BuildCommand().Run(mddDir);
            return (exit, err.Text);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void A_target_no_Api_target_registers_is_reported()
    {
        var (exit, stderr) = Build(
            """{ "type": "Api", "projectPath": "../module", "namespace": "T.Module", "includeOwners": ["ops"] }""");

        Assert.Equal(0, exit);
        Assert.Contains("[MDD035]", stderr);
        Assert.Contains("'OpsAsset.category_id'", stderr);
    }

    [Fact]
    public void A_target_another_Api_target_registers_is_not_reported()
    {
        var (exit, stderr) = Build(
            """{ "type": "Api", "projectPath": "../base", "namespace": "T.Base", "includeOwners": [""] }, """
            + """{ "type": "Api", "projectPath": "../module", "namespace": "T.Module", "includeOwners": ["ops"] }""");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD035", stderr);
    }
}
