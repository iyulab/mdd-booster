using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A Model target split into <c>modules</c>: each module's entities, enums, views and configurations go to its own
/// project and namespace, chosen by owner (<c># Prefix:</c>), and a module names only the modules listed before it.
/// The context goes to the target's own project and serves every module.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ModelModulesBuildTests
{
    private const string Core = """
# Namespace: test.layers

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

    private const string Maint = """
# Namespace: test.layers
# Prefix: maint

## MaintWorkOrder : Timestampable
- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset)
- asset_name: string? @lookup(asset_id.name)
- title: string(80) @not_null

## MaintAssetProfile ::aspect(Asset) : Timestampable
- interval_days: integer @not_null
""";

    private const string Modules = """
    "modules": [
      { "owners": [""], "projectPath": "../core", "namespace": "Layers.Core" },
      { "owners": ["maint"], "projectPath": "../maint", "namespace": "Layers.Maint" } ]
""";

    private static string NewRoot(string core = Core, string maint = Maint)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-modules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        File.WriteAllText(Path.Combine(root, "mdd", "core.m3l.md"), core);
        File.WriteAllText(Path.Combine(root, "mdd", "maint.m3l.md"), maint);
        return root;
    }

    private (int Exit, string Stderr) Build(string root, string modules = Modules, string extraTargets = "")
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), $$"""
            { "sources": ["./core.m3l.md", "./maint.m3l.md"], "targets": [
              { "type": "Model", "projectPath": "../host", "namespace": "Layers.Host", "dbContextName": "LayersDbContext",
            {{modules}} }{{extraTargets}} ] }
            """);
        using var err = new ConsoleErrorCapture(this);
        var exit = Program.Main(["build", Path.Combine(root, "mdd")]);
        return (exit, err.Text);
    }

    private static string Read(string root, params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void Each_module_gets_its_owners_declarations_in_its_namespace()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            Assert.Contains("namespace Layers.Core;", Read(root, "core", "Entity_gen", "Asset.cs"));
            Assert.Contains("namespace Layers.Core;", Read(root, "core", "Entity_gen", "AssetExtConfiguration.cs"));
            Assert.Contains("namespace Layers.Core;", Read(root, "core", "Enum_gen", "AssetKind.cs"));
            Assert.False(File.Exists(Path.Combine(root, "core", "Entity_gen", "MaintWorkOrder.cs")));

            var workOrder = Read(root, "maint", "Entity_gen", "MaintWorkOrderExt.cs");
            Assert.Contains("namespace Layers.Maint;", workOrder);
            Assert.Contains("using Layers.Core;", workOrder);
            Assert.Contains("AssetExt Asset", workOrder);
            Assert.False(File.Exists(Path.Combine(root, "maint", "Entity_gen", "Asset.cs")));

            // The target's own project holds the context only, importing every module.
            var context = Read(root, "host", "DbContext_gen", "LayersDbContext.cs");
            Assert.Contains("namespace Layers.Host;", context);
            Assert.Contains("using Layers.Core;", context);
            Assert.Contains("using Layers.Maint;", context);
            Assert.Contains("modelBuilder.ApplyConfiguration(new MaintWorkOrderExtConfiguration());", context);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "host", "Entity_gen")));
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// A lower module's files import nothing above them — the core project compiles without the maint one.
    /// </summary>
    [Fact]
    public void A_lower_module_never_names_an_upper_one()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            foreach (var file in Directory.GetFiles(Path.Combine(root, "core"), "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("Layers.Maint", text);
                Assert.DoesNotContain("Maint", text.Replace("MaintenanceProfile", ""));
            }
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// An upper module's aspect on a lower model: the base's read type would get a navigation back to the aspect, an
    /// upper type in the lower module. That inverse is not generated; the relationship is configured from the aspect.
    /// </summary>
    [Fact]
    public void An_upper_aspect_of_a_lower_model_keeps_its_navigation_on_the_upper_side()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root);
            Assert.True(exit == 0, stderr);

            Assert.DoesNotContain("MaintAssetProfileExt", Read(root, "core", "Entity_gen", "AssetExt.cs"));
            var profile = Read(root, "maint", "Entity_gen", "MaintAssetProfileExtConfiguration.cs");
            Assert.Contains("builder.HasOne(e => e.Asset).WithOne().HasForeignKey<MaintAssetProfileExt>(e => e.Id)", profile);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Without_modules_the_inverse_is_generated_as_before()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, modules: "");
            Assert.True(exit == 0, stderr);

            Assert.Contains("MaintAssetProfileExt?", Read(root, "host", "Entity_gen", "AssetExt.cs"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>Listed the other way round, the maint models name a module listed after them.</summary>
    [Fact]
    public void Modules_listed_upper_first_are_a_model_error_naming_each_upward_name()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, """
                "modules": [
                  { "owners": ["maint"], "projectPath": "../maint", "namespace": "Layers.Maint" },
                  { "owners": [""], "projectPath": "../core", "namespace": "Layers.Core" } ]
                """);

            Assert.Equal(3, exit);
            Assert.Contains("MaintWorkOrder.asset_id(modules[0]) 가 참조 모델 Asset(modules[1])", stderr);
            Assert.Contains("MaintWorkOrder.asset_name(modules[0]) 가 lookup 경로로 모델 Asset(modules[1])", stderr);
            Assert.False(Directory.Exists(Path.Combine(root, "maint")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_lower_model_using_an_upper_enum_is_a_model_error()
    {
        var root = NewRoot(
            core: Core + "\n- grade: MaintGrade?\n",
            maint: Maint + "\n## MaintGrade ::enum\n- a: \"A\"\n- b: \"B\"\n");
        try
        {
            var (exit, stderr) = Build(root);

            Assert.Equal(3, exit);
            Assert.Contains("Asset.grade(modules[0]) 가 형 enum MaintGrade(modules[1])", stderr);
        }
        finally { Cleanup(root); }
    }

    /// <summary>A reverse collection declared on a lower model names the upper model it collects (decision 3).</summary>
    [Fact]
    public void A_reverse_collection_on_a_lower_model_over_an_upper_one_is_a_model_error()
    {
        var root = NewRoot(core: Core + "\n### Relations\n- <work_orders\n  - target: MaintWorkOrder.asset_id\n");
        try
        {
            var (exit, stderr) = Build(root);

            Assert.Equal(3, exit);
            Assert.Contains("Asset.WorkOrders(역방향 컬렉션)(modules[0]) 가 원소로 모델 MaintWorkOrder(modules[1])", stderr);
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""
        "modules": [ { "owners": [""], "projectPath": "../core", "namespace": "Layers.Core" } ]
        """, "소유자 'maint' 의 선언이 어느 모듈에도 없습니다")]
    [InlineData("""
        "modules": [
          { "owners": ["", "maint"], "projectPath": "../core", "namespace": "Layers.Core" },
          { "owners": ["maint"], "projectPath": "../maint", "namespace": "Layers.Maint" } ]
        """, "소유자 'maint' 가 modules[0] 와 modules[1] 에 함께 있습니다")]
    [InlineData("""
        "modules": [
          { "owners": [""], "projectPath": "../core", "namespace": "Layers.Core" },
          { "owners": ["maint", "mnt"], "projectPath": "../maint", "namespace": "Layers.Maint" } ]
        """, "modules[1] 의 소유자 'mnt' 는 모델에 없습니다")]
    public void A_module_list_that_does_not_cover_the_owners_exactly_is_a_configuration_error(string modules, string message)
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, modules);

            Assert.Equal(4, exit);
            Assert.Contains(message, stderr);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Modules_on_another_target_is_a_configuration_error()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, modules: "", extraTargets: """
                , { "type": "Sql", "projectPath": "../db", "emitSqlProj": false,
                    "modules": [ { "owners": [""], "projectPath": "../x", "namespace": "X" } ] }
                """);

            Assert.Equal(4, exit);
            Assert.Contains("modules 는 Model 타깃에만", stderr);
        }
        finally { Cleanup(root); }
    }
}
