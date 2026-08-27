using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// Exercises <see cref="SqlGenerator"/> end to end (not just the renderer) so a wiring mistake
/// between <see cref="FullViewCycleDetector"/> and the generator's render loop shows up here,
/// not only at first live deployment.
/// </summary>
public sealed class SqlGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mddbooster-sqlgen", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string WriteInlineM3l(string dir, string body)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "model.m3l.md");
        File.WriteAllText(path, "# Namespace: test\n\n" + body);
        return path;
    }

    [Fact]
    public void Generate_throws_a_build_time_error_naming_both_models_when_full_views_form_a_cycle()
    {
        // Same fixture as FullViewCycleDetectorTests — asserts the detector is actually wired
        // into SqlGenerator.Generate, not just correct in isolation (docket #101).
        var srcPath = WriteInlineM3l(
            _root,
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- enterprise_id: identifier @reference(Enterprise) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(enterprise_id.name)\n\n" +
            "### Rollup\n" +
            "- supply_total: decimal(12,0) @rollup(OrderItem.order_id, sum(line_total))\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(order_id.customer_name)\n\n" +
            "### Computed\n" +
            "- line_total: decimal(12,0) @computed(`0`)\n");

        var ast = new M3lLoader().LoadFile(srcPath);
        var context = new GeneratorContext
        {
            Models = new InterfaceResolver(ast).ResolveAll(),
            Enums = ast.Enums,
            WorkingDirectory = _root,
        };
        var generator = new SqlGenerator(new SqlGeneratorOptions
        {
            ProjectPath = ".",
            EmitSqlProj = false,
            EmitRefreshScript = false,
        });

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Generate(context));

        Assert.Contains("Circular FullView dependency", ex.Message);
        Assert.Contains("Order", ex.Message);
        Assert.Contains("OrderItem", ex.Message);
        // Each hop after the first names the field that redirected the previous model's
        // FullView here, not just the bare model-name path.
        Assert.Contains("(via Order.SupplyTotal rollup)", ex.Message);
        Assert.Contains("(via OrderItem.CustomerName lookup)", ex.Message);
    }

    [Fact]
    public void Generate_succeeds_for_the_non_cyclic_chained_lookup_regression()
    {
        // Same shape as the cycle fixture minus the reverse rollup — must build clean.
        var srcPath = WriteInlineM3l(
            _root,
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- enterprise_id: identifier @reference(Enterprise) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(enterprise_id.name)\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(order_id.customer_name)\n");

        var ast = new M3lLoader().LoadFile(srcPath);
        var context = new GeneratorContext
        {
            Models = new InterfaceResolver(ast).ResolveAll(),
            Enums = ast.Enums,
            WorkingDirectory = _root,
        };
        var generator = new SqlGenerator(new SqlGeneratorOptions
        {
            ProjectPath = ".",
            EmitSqlProj = false,
            EmitRefreshScript = false,
        });

        generator.Generate(context);

        Assert.True(File.Exists(Path.Combine(_root, "dbo", "Views_gen", "OrderItemFullView.sql")));
    }
}
