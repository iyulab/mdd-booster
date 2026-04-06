using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

public class SemanticAnalyzerTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Clean_fixture_produces_no_diagnostics()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Reference_to_missing_entity_is_flagged_as_MDD002()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- ghost_id: identifier @reference(Nowhere) @not_null
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            Assert.Contains(diagnostics, d => d.Code == "MDD002" && d.Message.Contains("Nowhere"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Unknown_field_type_is_flagged_as_MDD001()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- mystery: QuantumFoo @not_null
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            Assert.Contains(diagnostics, d => d.Code == "MDD001" && d.Message.Contains("QuantumFoo"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Enum_referenced_by_field_type_is_resolved()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-enum.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Lookup_with_missing_target_field_is_flagged_as_MDD006()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## Customer
- id: identifier @pk @generated
- name: string @not_null

## Order
- id: identifier @pk @generated
- customer_id: identifier @reference(Customer) @not_null
- ghost_field: string @lookup(customer_id.nonexistent)
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            Assert.Contains(diagnostics, d => d.Code == "MDD006" && d.Message.Contains("nonexistent"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Rollup_with_missing_target_entity_is_flagged_as_MDD008()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## Order
- id: identifier @pk @generated
- phantom_count: integer @rollup(Ghost.order_id, count)
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            Assert.Contains(diagnostics, d => d.Code == "MDD008" && d.Message.Contains("Ghost"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Yesung_full_fixture_passes_semantic_analysis()
    {
        var yesung = @"D:\data\yesung\mdd\tables.m3l.md";
        if (!File.Exists(yesung)) return;

        var ast = new M3lLoader().LoadFile(yesung);
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

        Assert.True(diagnostics.Count == 0,
            $"Yesung fixture produced {diagnostics.Count} semantic diagnostics:\n" +
            string.Join("\n", diagnostics.Select(d => d.Format())));
    }
}
