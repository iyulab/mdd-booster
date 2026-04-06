using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// Cycle 22 — Lookup/Rollup/Computed field rendering. Derived fields only
/// appear on the Ext read model (view-backed); the Write class and the
/// marker interface stay stored-only.
/// </summary>
public class DerivedFieldsTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Lookup_through_nullable_fk_becomes_nullable_string()
    {
        // 회귀: LEFT JOIN 결과가 NULL 일 수 있으므로, nullable FK 를
        // 거친 lookup 필드는 C# 측에서도 string? 로 나와야 한다.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-nullable-lookup-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Bank\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(30) @not_null\n\n" +
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- title: string(50) @not_null\n" +
            "- default_bank_id: identifier? @reference(Bank)? \"옵션 계좌\"\n\n" +
            "### Lookup\n" +
            "- default_bank_name: string @lookup(default_bank_id.name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var ent = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Enterprise");
            var pair = EntityPairRenderer.Render(ent, "Test.Entities", knownEnumNames: null);

            // Ext class 내에 string? 타입의 DefaultBankName 이 있어야 함.
            Assert.Contains("public string? DefaultBankName", pair.Read);
            Assert.DoesNotContain("DefaultBankName { get; set; } = string.Empty;", pair.Read);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static (M3lAst ast, ResolvedModel order, IReadOnlySet<string> enumNames) LoadOrder()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);
        return (ast, order, enumNames);
    }

    [Fact]
    public void Lookup_fields_only_appear_on_Ext_class()
    {
        var (_, order, enumNames) = LoadOrder();
        var pair = EntityPairRenderer.Render(order, "Test.Orders", enumNames);

        // Ext has the lookup property
        Assert.Contains("public string CustomerName", pair.Read);
        Assert.Contains("[global::Iyu.Core.Attributes.Lookup(\"customer_id.name\")]", pair.Read);
        Assert.Contains("public string CustomerEmail", pair.Read);

        // Write class does NOT include lookup fields
        Assert.DoesNotContain("CustomerName", pair.Write);
        Assert.DoesNotContain("CustomerEmail", pair.Write);

        // Interface does NOT include lookup fields
        Assert.DoesNotContain("CustomerName", pair.Interface);
    }

    [Fact]
    public void Rollup_fields_carry_Rollup_attribute_and_indexed_flag()
    {
        var (_, order, enumNames) = LoadOrder();
        var pair = EntityPairRenderer.Render(order, "Test.Orders", enumNames);

        Assert.Contains("[global::Iyu.Core.Attributes.Rollup(", pair.Read);
        Assert.Contains("public int ItemCount", pair.Read);
        Assert.Contains("public decimal TotalSum", pair.Read);

        // @indexed → Indexed = true on the attribute
        Assert.Contains("Indexed = true", pair.Read);

        // Write class does not carry rollup fields
        Assert.DoesNotContain("ItemCount", pair.Write);
        Assert.DoesNotContain("TotalSum", pair.Write);
    }

    [Fact]
    public void Computed_fields_carry_Computed_attribute_with_unwrapped_expression()
    {
        var (_, order, enumNames) = LoadOrder();
        var pair = EntityPairRenderer.Render(order, "Test.Orders", enumNames);

        // Backticks from M3L are stripped; the raw expression is inside the attribute.
        Assert.Contains("[global::Iyu.Core.Attributes.Computed(\"subtotal * 0.1\")]", pair.Read);
        Assert.Contains("[global::Iyu.Core.Attributes.Computed(\"subtotal + tax_amount\")]", pair.Read);
        Assert.Contains("public decimal TaxAmount", pair.Read);
        Assert.Contains("public decimal GrandTotal", pair.Read);

        Assert.DoesNotContain("TaxAmount", pair.Write);
        Assert.DoesNotContain("GrandTotal", pair.Write);
    }

    [Fact]
    public void Derived_field_fixture_end_to_end_parses_as_valid_csharp()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);

        var sources = new List<(string, string)>();
        foreach (var e in ast.Enums)
            sources.Add((e.Name + ".cs", EnumRenderer.Render(e, "Test.Orders")));
        foreach (var m in models)
        {
            var p = EntityPairRenderer.Render(m, "Test.Orders", enumNames);
            sources.Add(($"I{m.Name}.cs", p.Interface));
            sources.Add(($"{m.Name}.cs", p.Write));
            sources.Add(($"{m.Name}Ext.cs", p.Read));
        }
        sources.Add(("OrdersDbContext.cs",
            DbContextRenderer.Render(models, "OrdersDbContext", "Test.Orders")));

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        foreach (var (name, src) in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(src, parseOptions);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"Errors in {name}: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{src}");
        }
    }
}
