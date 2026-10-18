using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>references</c>: model sources a build checks against but does not generate — the model of a published package
/// this build's own model builds on. Their declarations resolve (a reference, a lookup, an aspect's base) and are
/// checked, no target emits them, and the Model target imports their namespace and applies their configurations.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ReferenceSourcesBuildTests
{
    private const string Standard = """
# Namespace: std

## Timestampable ::interface
- created_at: timestamp @not_null
- updated_at: timestamp @not_null

## AssetKind ::enum
- plant: "설비"
- tool: "공구"

## Asset : Timestampable
- id: identifier @pk @generated
- name: string(50) @not_null
- kind: AssetKind @not_null
""";

    private const string Custom = """
# Namespace: custom
# Prefix: cus

## CusInspection : Timestampable
- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset)
- asset_name: string? @lookup(asset_id.name)
- kind: AssetKind?

## CusAssetNote ::aspect(Asset) : Timestampable
- note: string(200)
""";

    private static string NewRoot(string standard = Standard, string custom = Custom)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-references-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        Directory.CreateDirectory(Path.Combine(root, "standard"));
        File.WriteAllText(Path.Combine(root, "standard", "standard.m3l.md"), standard);
        File.WriteAllText(Path.Combine(root, "mdd", "custom.m3l.md"), custom);
        return root;
    }

    private const string Targets = """
        { "type": "Model", "projectPath": "../model", "namespace": "Custom.Entities", "dbContextName": "CustomDbContext" },
        { "type": "Sql", "projectPath": "../db", "emitSqlProj": false },
        { "type": "Api", "projectPath": "../api", "namespace": "Custom.Server" },
        { "type": "TypeScript", "outputPath": "../ts" }
        """;

    private const string Reference = """
        [ { "sources": ["../standard/standard.m3l.md"], "namespace": "Std.Entities" } ]
        """;

    private (int Exit, string Stderr) Build(string root, string references = Reference, string sources = """["./custom.m3l.md"]""", string targets = Targets)
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"),
            $$"""{ "sources": {{sources}}, "references": {{references}}, "targets": [ {{targets}} ] }""");
        using var err = new ConsoleErrorCapture(this);
        var exit = Program.Main(["build", Path.Combine(root, "mdd")]);
        return (exit, err.Text);
    }

    private static string Read(string root, params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));

    private static bool Exists(string root, params string[] path) => File.Exists(Path.Combine([root, .. path]));

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void No_target_emits_a_referenced_declaration()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            Assert.True(Exists(root, "model", "Entity_gen", "CusInspection.cs"));
            Assert.False(Exists(root, "model", "Entity_gen", "Asset.cs"));
            Assert.False(Exists(root, "model", "Entity_gen", "AssetConfiguration.cs"));
            Assert.False(Exists(root, "model", "Enum_gen", "AssetKind.cs"));
            Assert.True(Exists(root, "db", "dbo", "Tables_gen", "CusInspection.sql"));
            Assert.False(Exists(root, "db", "dbo", "Tables_gen", "Asset.sql"));
            Assert.DoesNotContain("Asset>", Read(root, "api", "Api_gen", "ApiRegistration_gen.cs").Replace("CusAssetNote", ""));
            Assert.DoesNotContain("interface Asset ", Read(root, "ts", "entities_gen.ts"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>The generated types name the referenced ones through the reference's namespace.</summary>
    [Fact]
    public void Generated_types_import_the_referenced_namespace_and_name_its_types()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            var inspection = Read(root, "model", "Entity_gen", "CusInspectionExt.cs");
            Assert.Contains("using Std.Entities;", inspection);
            Assert.Contains("AssetExt Asset", inspection);
            Assert.Contains("AssetKind? Kind", inspection);
            Assert.Contains("REFERENCES", Read(root, "db", "dbo", "Tables_gen", "CusInspection.sql"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// One context serves the referenced types too: their sets, and their configurations — which the referenced
    /// package carries — applied by name.
    /// </summary>
    [Fact]
    public void The_context_applies_the_referenced_configurations()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            var context = Read(root, "model", "DbContext_gen", "CustomDbContext.cs");
            Assert.Contains("using Std.Entities;", context);
            Assert.Contains("public DbSet<Asset> Assets", context);
            Assert.Contains("modelBuilder.ApplyConfiguration(new AssetExtConfiguration());", context);
            Assert.Contains("modelBuilder.ApplyConfiguration(new CusInspectionConfiguration());", context);
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// An aspect of a referenced model: the referenced read type cannot carry a navigation to this build's type, so
    /// none is generated for it and the relationship is configured from the aspect.
    /// </summary>
    [Fact]
    public void An_aspect_of_a_referenced_model_is_configured_from_the_aspect()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            Assert.Contains("builder.HasOne(e => e.Asset).WithOne().HasForeignKey<CusAssetNoteExt>(e => e.Id)",
                Read(root, "model", "Entity_gen", "CusAssetNoteExtConfiguration.cs"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_referenced_declaration_naming_a_generated_one_is_a_model_error()
    {
        var root = NewRoot(standard: Standard + "\n- note_kind: CusNoteKind?\n", custom: Custom + "\n## CusNoteKind ::enum\n- a: \"A\"\n");
        try
        {
            var (exit, stderr) = Build(root);

            Assert.Equal(3, exit);
            Assert.Contains("Asset.note_kind(references) 가 형 enum CusNoteKind(sources)", stderr);
            Assert.False(Directory.Exists(Path.Combine(root, "model")));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""[ { "sources": ["../standard/missing.m3l.md"], "namespace": "Std.Entities" } ]""", "missing.m3l.md")]
    [InlineData("""[ { "sources": ["../standard/standard.m3l.md"], "namespace": "" } ]""", "namespace 가 없습니다")]
    [InlineData("""[ { "sources": ["./custom.m3l.md"], "namespace": "X" } ]""", "sources 에도 있습니다")]
    public void A_reference_that_cannot_mean_what_it_says_is_a_configuration_error(string references, string message)
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, references);

            Assert.Equal(4, exit);
            Assert.Contains(message, stderr);
        }
        finally { Cleanup(root); }
    }

    /// <summary>With modules, the references are the layer below every module.</summary>
    [Fact]
    public void With_modules_the_references_are_below_every_module()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, targets: """
                { "type": "Model", "projectPath": "../host", "namespace": "Custom.Host", "dbContextName": "CustomDbContext",
                  "modules": [ { "owners": ["cus"], "projectPath": "../cus", "namespace": "Custom.Entities" } ] }
                """);
            Assert.True(exit == 0, stderr);

            Assert.Contains("using Std.Entities;", Read(root, "cus", "Entity_gen", "CusInspectionExt.cs"));
            Assert.False(Exists(root, "cus", "Entity_gen", "Asset.cs"));
            Assert.Contains("using Std.Entities;", Read(root, "host", "DbContext_gen", "CustomDbContext.cs"));
        }
        finally { Cleanup(root); }
    }
}
