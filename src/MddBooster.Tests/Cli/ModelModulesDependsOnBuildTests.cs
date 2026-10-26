using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>modules[].dependsOn</c> — layers that are not a line. A common layer with two products on it: each product builds on the
/// common layer and neither knows the other, so its files import only what it builds on, a declaration naming the sibling
/// is refused, and its data interface extends the common one alone.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ModelModulesDependsOnBuildTests
{
    private const string Core = """
# Namespace: test.siblings

## Timestampable ::interface
- created_at: timestamp @not_null
- updated_at: timestamp @not_null

## Asset : Timestampable
- id: identifier @pk @generated
- name: string(50) @not_null
""";

    private const string Maint = """
# Namespace: test.siblings
# Prefix: maint

## MaintWorkOrder : Timestampable
- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset)
- title: string(80) @not_null
""";

    private const string Fleet = """
# Namespace: test.siblings
# Prefix: fleet

## FleetTrip : Timestampable
- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset)
- distance: integer @not_null
""";

    /// <summary>The product that names its sibling's model.</summary>
    private const string FleetNamingMaint = Fleet + "\n- work_order_id: identifier? @reference(MaintWorkOrder)\n";

    private const string Core_ = """{ "owners": [""], "projectPath": "../core", "namespace": "Sib.Core", "dataInterface": "ICoreData" }""";

    private static string ModuleList(params string[] modules) => "\"modules\": [\n  " + string.Join(",\n  ", modules) + " ]";

    private const string Siblings = """
      "modules": [
        { "owners": [""],      "projectPath": "../core",  "namespace": "Sib.Core",  "dataInterface": "ICoreData" },
        { "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dataInterface": "IMaintData", "dependsOn": [""] },
        { "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData", "dependsOn": [""] } ]
""";

    private static string NewRoot(string fleet = Fleet)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-siblings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        File.WriteAllText(Path.Combine(root, "mdd", "core.m3l.md"), Core);
        File.WriteAllText(Path.Combine(root, "mdd", "maint.m3l.md"), Maint);
        File.WriteAllText(Path.Combine(root, "mdd", "fleet.m3l.md"), fleet);
        return root;
    }

    private (int Exit, string Stderr) Build(string root, string modules)
    {
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), $$"""
            { "sources": ["./core.m3l.md", "./maint.m3l.md", "./fleet.m3l.md"], "targets": [
              { "type": "Model", "projectPath": "../host", "namespace": "Sib.Host", "dbContextName": "SibDbContext",
            {{modules}} } ] }
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

    private static string AllText(string root, string module) => string.Concat(
        Directory.GetFiles(Path.Combine(root, module), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    [Fact]
    public void A_product_imports_only_the_layers_it_builds_on()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, Siblings);
            Assert.True(exit == 0, stderr);

            var trip = Read(root, "fleet", "Entity_gen", "FleetTripExt.cs");
            Assert.Contains("using Sib.Core;", trip);
            Assert.DoesNotContain("Sib.Maint", AllText(root, "fleet"));
            Assert.DoesNotContain("Sib.Fleet.Maint", AllText(root, "fleet"));
            Assert.DoesNotContain("Sib.Fleet", AllText(root, "maint"));

            // The context serves every module.
            var context = Read(root, "host", "DbContext_gen", "SibDbContext.cs");
            Assert.Contains("using Sib.Maint;", context);
            Assert.Contains("using Sib.Fleet;", context);
        }
        finally { Cleanup(root); }
    }

    /// <summary>Without it a module builds on every module before it — the files a build produced before the key existed.</summary>
    [Fact]
    public void Without_dependsOn_the_second_product_sits_on_the_first()
    {
        var root = NewRoot();
        try
        {
            var linear = ModuleList(
                Core_,
                """{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dataInterface": "IMaintData" }""",
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData" }""");
            var (exit, stderr) = Build(root, linear);
            Assert.True(exit == 0, stderr);

            Assert.Contains("using Sib.Maint;", Read(root, "fleet", "Entity_gen", "FleetTripExt.cs"));
            Assert.Contains("interface IFleetData : IMaintData", Read(root, "fleet", "Entity_gen", "IFleetData.cs"));
            Assert.Contains(": IyuDbContext, IFleetData", Read(root, "host", "DbContext_gen", "SibDbContext.cs"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_declaration_naming_a_sibling_is_refused_where_a_line_of_layers_allows_it()
    {
        var root = NewRoot(FleetNamingMaint);
        try
        {
            var (exit, stderr) = Build(root, Siblings);
            Assert.NotEqual(0, exit);
            Assert.Contains("FleetTrip.work_order_id", stderr);
            Assert.Contains("MaintWorkOrder", stderr);
            Assert.Contains("dependsOn", stderr);

            // The same models in a line: fleet sits on maint, so the reference is allowed.
            var linear = ModuleList(
                Core_,
                """{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dataInterface": "IMaintData" }""",
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData" }""");
            var (lineExit, lineErr) = Build(root, linear);
            Assert.True(lineExit == 0, lineErr);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Each_products_data_interface_extends_the_common_one_alone()
    {
        var root = NewRoot();
        try
        {
            var (exit, stderr) = Build(root, Siblings);
            Assert.True(exit == 0, stderr);

            var core = Read(root, "core", "Entity_gen", "ICoreData.cs");
            Assert.Contains("public interface ICoreData\n", core.Replace("\r\n", "\n"));
            Assert.Contains("DbSet<TEntity> Set<TEntity>()", core);
            Assert.Contains("DbSet<Asset> Assets", core);

            var maint = Read(root, "maint", "Entity_gen", "IMaintData.cs");
            Assert.Contains("public interface IMaintData : ICoreData", maint);
            Assert.Contains("DbSet<MaintWorkOrder> MaintWorkOrders", maint);
            Assert.DoesNotContain("Set<TEntity>", maint);
            Assert.DoesNotContain("IFleetData", maint);

            var fleet = Read(root, "fleet", "Entity_gen", "IFleetData.cs");
            Assert.Contains("public interface IFleetData : ICoreData", fleet);
            Assert.Contains("DbSet<FleetTrip> FleetTrips", fleet);
            Assert.DoesNotContain("IMaintData", fleet);
            Assert.DoesNotContain("MaintWorkOrder", fleet);

            // Every interface no other extends, in module order.
            Assert.Contains(": IyuDbContext, IMaintData, IFleetData", Read(root, "host", "DbContext_gen", "SibDbContext.cs"));
        }
        finally { Cleanup(root); }
    }

    /// <summary>What a module builds on is the closure of what it names — a product on a product reaches the common layer too.</summary>
    [Fact]
    public void The_closure_is_transitive()
    {
        var root = NewRoot();
        try
        {
            var chain = ModuleList(
                Core_,
                """{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dataInterface": "IMaintData", "dependsOn": [""] }""",
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData", "dependsOn": ["maint"] }""");
            var (exit, stderr) = Build(root, chain);
            Assert.True(exit == 0, stderr);

            var trip = Read(root, "fleet", "Entity_gen", "FleetTripExt.cs");
            Assert.Contains("using Sib.Core;", trip);
            Assert.Contains("using Sib.Maint;", trip);
            Assert.Contains("public interface IFleetData : IMaintData", Read(root, "fleet", "Entity_gen", "IFleetData.cs"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void A_module_with_no_interface_is_folded_into_the_interfaces_that_reach_it()
    {
        var root = NewRoot();
        try
        {
            // Core declares no interface: both products reach it, so each interface declares the core sets itself — and the
            // lowest interfaces would then be two. That is refused (below); with one product declaring, core folds into it.
            var onlyFleet = ModuleList(
                """{ "owners": [""], "projectPath": "../core", "namespace": "Sib.Core" }""",
                """{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dependsOn": [""] }""",
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData", "dependsOn": [""] }""");
            var (exit, stderr) = Build(root, onlyFleet);
            Assert.True(exit == 0, stderr);

            var fleet = Read(root, "fleet", "Entity_gen", "IFleetData.cs");
            Assert.Contains("DbSet<Asset> Assets", fleet);
            Assert.Contains("DbSet<FleetTrip> FleetTrips", fleet);
            Assert.DoesNotContain("MaintWorkOrder", fleet);
            Assert.Contains(": IyuDbContext, IFleetData", Read(root, "host", "DbContext_gen", "SibDbContext.cs"));
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [InlineData("""{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dependsOn": ["ghost"] }""", "ghost")]
    [InlineData("""{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dependsOn": ["fleet"] }""", "modules[2]")]
    [InlineData("""{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dependsOn": ["maint"] }""", "자기 자신")]
    public void A_dependsOn_that_names_no_earlier_module_is_a_configuration_error(string maintModule, string expected)
    {
        var root = NewRoot();
        try
        {
            var modules = ModuleList(
                """{ "owners": [""], "projectPath": "../core", "namespace": "Sib.Core" }""",
                maintModule,
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet" }""");
            var (exit, stderr) = Build(root, modules);
            Assert.Equal(4, exit);
            Assert.Contains("dependsOn", stderr);
            Assert.Contains(expected, stderr);
        }
        finally { Cleanup(root); }
    }

    /// <summary>Two interfaces that do not build on one another would each declare the context's surface.</summary>
    [Fact]
    public void Two_lowest_interfaces_are_a_configuration_error()
    {
        var root = NewRoot();
        try
        {
            var modules = ModuleList(
                """{ "owners": [""], "projectPath": "../core", "namespace": "Sib.Core" }""",
                """{ "owners": ["maint"], "projectPath": "../maint", "namespace": "Sib.Maint", "dataInterface": "IMaintData", "dependsOn": [""] }""",
                """{ "owners": ["fleet"], "projectPath": "../fleet", "namespace": "Sib.Fleet", "dataInterface": "IFleetData", "dependsOn": [""] }""");
            var (exit, stderr) = Build(root, modules);
            Assert.Equal(4, exit);
            Assert.Contains("서로 아래층을 모릅니다", stderr);
        }
        finally { Cleanup(root); }
    }
}
