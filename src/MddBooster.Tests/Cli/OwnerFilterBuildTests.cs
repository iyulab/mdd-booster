using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>includeOwners</c>/<c>excludeOwners</c> — a surface target chooses its models by the file that
/// owns them (<c># Prefix:</c>) instead of by a list of names that has to be kept in step by hand.
/// </summary>
/// <remarks>
/// The shape under test is one model split into a base file and a module file, emitted as two
/// TypeScript targets. With entity lists that takes the same names twice — once to include, once to
/// exclude — and a model added to the module file silently appears in the base target and not in
/// the module one. The last two tests are that drift, measured.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class OwnerFilterBuildTests
{
    private const string Base = """
# Namespace: test.owners

## Asset

- id: identifier @pk @generated
- name: string(50) "이름"
""";

    private const string Module = """
# Namespace: test.owners
# Prefix: fsa

## FsaInspection

- id: identifier @pk @generated
- note: string(50) "비고"
""";

    private const string ModuleGrown = Module + """

## FsaObligation

- id: identifier @pk @generated
- rule: string(50) "규칙"
""";

    private static string Scaffold(string targetsJson, out string root, string module = Module)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-owners-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "base.m3l.md"), Base);
        File.WriteAllText(Path.Combine(mddDir, "fsa.m3l.md"), module);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{\n  \"sources\": [\"./base.m3l.md\", \"./fsa.m3l.md\"],\n  \"targets\": [" + targetsJson + "]\n}");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private const string SplitTargets =
        """{ "type": "TypeScript", "outputPath": "../base", "excludeOwners": ["fsa"] }, """
        + """{ "type": "TypeScript", "outputPath": "../module", "includeOwners": ["fsa"] }""";

    private static string Entities(string root, string target)
        => File.ReadAllText(Path.Combine(root, target, "entities_gen.ts"));

    [Fact]
    public void Each_target_gets_the_models_of_its_owners()
    {
        var mddDir = Scaffold(SplitTargets, out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.Contains("interface Asset ", Entities(root, "base"));
            Assert.DoesNotContain("FsaInspection", Entities(root, "base"));
            Assert.Contains("interface FsaInspection ", Entities(root, "module"));
            Assert.DoesNotContain("interface Asset ", Entities(root, "module"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>A model added to the module file lands in the module target without a config change.</summary>
    [Fact]
    public void A_model_added_to_an_owner_follows_it_without_a_config_change()
    {
        var mddDir = Scaffold(SplitTargets, out var root, ModuleGrown);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.Contains("interface FsaObligation ", Entities(root, "module"));
            Assert.DoesNotContain("FsaObligation", Entities(root, "base"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>The empty string names the files that declare no prefix.</summary>
    [Fact]
    public void The_empty_owner_selects_the_unprefixed_files()
    {
        var mddDir = Scaffold(
            """{ "type": "TypeScript", "outputPath": "../base", "includeOwners": [""] }""", out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.Contains("interface Asset ", Entities(root, "base"));
            Assert.DoesNotContain("FsaInspection", Entities(root, "base"));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../t", "includeOwners": ["fsx"] }""")]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../t", "includeOwners": ["fsa"], "excludeOwners": [""] }""")]
    [InlineData("""{ "type": "Sql", "projectPath": "../t", "excludeOwners": ["fsa"] }""")]
    public void An_owner_filter_that_cannot_mean_what_it_says_is_a_configuration_error(string targetJson)
    {
        var mddDir = Scaffold(targetJson, out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }

    /// <summary>Owners and entity names combine as an intersection.</summary>
    [Fact]
    public void An_owner_filter_and_an_entity_filter_both_apply()
    {
        var mddDir = Scaffold(
            """{ "type": "TypeScript", "outputPath": "../module", "includeOwners": ["fsa"], "excludeEntities": ["FsaInspection"] }""",
            out var root, ModuleGrown);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.Contains("interface FsaObligation ", Entities(root, "module"));
            Assert.DoesNotContain("FsaInspection", Entities(root, "module"));
            Assert.DoesNotContain("interface Asset ", Entities(root, "module"));
        }
        finally { Cleanup(root); }
    }
}
