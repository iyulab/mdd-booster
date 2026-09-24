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
# Prefix: insp

## InspVisit

- id: identifier @pk @generated
- note: string(50) "비고"
""";

    private const string ModuleGrown = Module + """

## InspSchedule

- id: identifier @pk @generated
- rule: string(50) "규칙"
""";

    private static string Scaffold(string targetsJson, out string root, string module = Module)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-owners-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "base.m3l.md"), Base);
        File.WriteAllText(Path.Combine(mddDir, "insp.m3l.md"), module);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{\n  \"sources\": [\"./base.m3l.md\", \"./insp.m3l.md\"],\n  \"targets\": [" + targetsJson + "]\n}");
        return mddDir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private const string SplitTargets =
        """{ "type": "TypeScript", "outputPath": "../base", "excludeOwners": ["insp"] }, """
        + """{ "type": "TypeScript", "outputPath": "../module", "includeOwners": ["insp"] }""";

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
            Assert.DoesNotContain("InspVisit", Entities(root, "base"));
            Assert.Contains("interface InspVisit ", Entities(root, "module"));
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

            Assert.Contains("interface InspSchedule ", Entities(root, "module"));
            Assert.DoesNotContain("InspSchedule", Entities(root, "base"));
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
            Assert.DoesNotContain("InspVisit", Entities(root, "base"));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../t", "includeOwners": ["fsx"] }""")]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../t", "includeOwners": ["insp"], "excludeOwners": [""] }""")]
    [InlineData("""{ "type": "Sql", "projectPath": "../t", "excludeOwners": ["insp"] }""")]
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
            """{ "type": "TypeScript", "outputPath": "../module", "includeOwners": ["insp"], "excludeEntities": ["InspVisit"] }""",
            out var root, ModuleGrown);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            Assert.Contains("interface InspSchedule ", Entities(root, "module"));
            Assert.DoesNotContain("InspVisit", Entities(root, "module"));
            Assert.DoesNotContain("interface Asset ", Entities(root, "module"));
        }
        finally { Cleanup(root); }
    }

    // ---- enums follow the owner axis; sharedTypesImport re-exports instead of re-declaring ----

    private const string BaseWithEnums = """
# Namespace: test.owners

## AssetKind ::enum

- pump: "펌프"
- valve: "밸브"

## AssetColor ::enum

- red: "빨강"

## Asset

- id: identifier @pk @generated
- name: string(50) "이름"
- kind: AssetKind "종류"
- color: AssetColor? "색"
""";

    private const string ModuleWithEnums = """
# Namespace: test.owners
# Prefix: insp

## InspGrade ::enum

- a: "A"

## InspVisit

- id: identifier @pk @generated
- kind: AssetKind "종류"
- grade: InspGrade "등급"
""";

    private static string ScaffoldWithEnums(string targetsJson, out string root)
    {
        var mddDir = Scaffold(targetsJson, out root, ModuleWithEnums);
        File.WriteAllText(Path.Combine(mddDir, "base.m3l.md"), BaseWithEnums);
        return mddDir;
    }

    private static string Enums(string root, string target)
        => File.ReadAllText(Path.Combine(root, target, "enums_gen.ts"));

    /// <summary>
    /// With an owner axis a target declares its owners' enums and, of the others, only those its
    /// models reference — the base no longer carries the module's vocabulary, the module no longer
    /// repeats the base's.
    /// </summary>
    [Fact]
    public void Enums_are_split_by_owner_and_a_referenced_foreign_enum_is_still_declared()
    {
        var mddDir = ScaffoldWithEnums(SplitTargets, out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var baseEnums = Enums(root, "base");
            Assert.Contains("export type AssetKind =", baseEnums);
            Assert.Contains("export type AssetColor =", baseEnums);
            Assert.DoesNotContain("InspGrade", baseEnums);

            var moduleEnums = Enums(root, "module");
            Assert.Contains("export type InspGrade =", moduleEnums);
            Assert.Contains("export type AssetKind =", moduleEnums);
            Assert.DoesNotContain("AssetColor", moduleEnums);
            Assert.Contains("export interface IyuEntity", Entities(root, "module"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// Given a shared source, the module target re-exports what it would otherwise declare a second
    /// time: <c>IyuEntity</c> and the referenced enums of other owners (with their label exports).
    /// </summary>
    [Fact]
    public void A_shared_source_turns_the_foreign_declarations_into_re_exports()
    {
        var mddDir = ScaffoldWithEnums(
            """{ "type": "TypeScript", "outputPath": "../module", "includeOwners": ["insp"], "sharedTypesImport": "@acme/base/types" }""",
            out var root);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var moduleEnums = Enums(root, "module");
            Assert.Contains("export type { AssetKind } from '@acme/base/types'", moduleEnums);
            Assert.DoesNotContain("export type AssetKind =", moduleEnums);
            Assert.Contains("export type InspGrade =", moduleEnums);

            var labels = File.ReadAllText(Path.Combine(root, "module", "enum_labels_gen.ts"));
            Assert.Contains("export { AssetKindLabels } from '@acme/base/types'", labels);

            var entities = Entities(root, "module");
            Assert.Contains("import type { IyuEntity } from '@acme/base/types'", entities);
            Assert.DoesNotContain("export interface IyuEntity", entities);
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""{ "type": "TypeScript", "outputPath": "../t", "sharedTypesImport": "@acme/base/types" }""")]
    [InlineData("""{ "type": "Api", "projectPath": "../t", "namespace": "T", "includeOwners": ["insp"], "sharedTypesImport": "@acme/base/types" }""")]
    public void A_shared_source_without_an_owner_axis_or_off_a_typescript_target_is_a_configuration_error(string targetJson)
    {
        var mddDir = ScaffoldWithEnums(targetJson, out var root);
        try
        {
            Assert.Equal(4, new BuildCommand().Run(mddDir));
        }
        finally { Cleanup(root); }
    }
}
