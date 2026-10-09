using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// An <c>::aspect</c> in a module file whose base lives in the base file, with the Api target split by owner.
/// The shared-key declaration belongs to the registration that registers the aspect set — even though the
/// base set is registered by the other one.
/// </summary>
/// <remarks>
/// Before, each Api target looked for the base among its own models only, so neither emitted the declaration:
/// the base target does not register the aspect, the module target does not register the base. The runtime
/// then never learnt the key was shared, and its shared-key refusals stayed off without a word from the build.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class AspectSharedKeyOwnerSplitBuildTests
{
    private const string Base = """
# Namespace: test.split

## Asset

- id: identifier @pk @generated
- name: string(50) "name"
""";

    private const string Module = """
# Namespace: test.split
# Prefix: ops

## OpsAssetProfile ::aspect(Asset)

- grade: integer?
""";

    private const string Declaration = "options.ODataModel.DeclareSharedKey(\"OpsAssetProfiles\", \"Assets\");";

    private (int Exit, string Stderr, string? BaseReg, string? ModuleReg) Build(string targetsJson)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-aspect-split-{Guid.NewGuid():N}");
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
            return (exit, err.Text, Read(root, "base"), Read(root, "module"));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }

        static string? Read(string root, string dir)
        {
            var path = Path.Combine(root, dir, "Api_gen", "ApiRegistration_gen.cs");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    [Fact]
    public void The_module_registration_declares_the_shared_key_and_says_the_base_one_comes_first()
    {
        var (exit, stderr, baseReg, moduleReg) = Build(
            """{ "type": "Api", "projectPath": "../base", "namespace": "T.Base", "includeOwners": [""] }, """
            + """{ "type": "Api", "projectPath": "../module", "namespace": "T.Module", "includeOwners": ["ops"] }""");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD033", stderr);
        Assert.DoesNotContain("DeclareSharedKey", baseReg);
        Assert.Contains(Declaration, moduleReg);
        Assert.Contains("'Assets' is registered by another generated registration; call that one first.", moduleReg);
    }

    [Fact]
    public void A_single_target_declares_it_as_before_with_no_ordering_note()
    {
        var (exit, _, baseReg, _) = Build(
            """{ "type": "Api", "projectPath": "../base", "namespace": "T.Base" }""");

        Assert.Equal(0, exit);
        Assert.Contains(Declaration, baseReg);
        Assert.DoesNotContain("call that one first", baseReg);
    }

    /// <summary>No Api target registers the base: there is nowhere to declare the key, and the build says so.</summary>
    [Fact]
    public void An_aspect_whose_base_no_target_registers_is_reported()
    {
        var (exit, stderr, _, moduleReg) = Build(
            """{ "type": "Api", "projectPath": "../module", "namespace": "T.Module", "includeOwners": ["ops"] }""");

        Assert.Equal(0, exit);
        Assert.Contains("[MDD033]", stderr);
        Assert.Contains("'OpsAssetProfile'", stderr);
        Assert.DoesNotContain("DeclareSharedKey", moduleReg);
    }
}
