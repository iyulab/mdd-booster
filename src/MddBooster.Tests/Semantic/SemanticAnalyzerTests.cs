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

    /// <summary>
    /// A model name cannot contain a colon, so a reference target that does is
    /// naming a scheme — something the language specifies and this generator does
    /// not read. Reporting it as a missing entity sends the reader looking for a
    /// typo in a name that was never meant to be one.
    /// </summary>
    [Fact]
    public void Reference_to_a_scheme_is_flagged_as_MDD012_not_MDD002()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- other_id: identifier @reference(external://taxonomy.Category) @not_null
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            Assert.Contains(diagnostics, d => d.Code == "MDD012");
            Assert.DoesNotContain(diagnostics, d => d.Code == "MDD002");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// The scheme is named back to the reader, so the message identifies which
    /// unsupported notation was used rather than only that one was.
    /// </summary>
    [Fact]
    public void The_MDD012_message_names_the_scheme_that_was_written()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-sem-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- other_id: identifier @reference(external://taxonomy.Category) @not_null
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

            var scheme = Assert.Single(diagnostics, d => d.Code == "MDD012");
            Assert.Contains("external", scheme.Message);
        }
        finally
        {
            File.Delete(tmp);
        }
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
}
