using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators.Model;

public class EnumRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static M3lAst LoadFixture() =>
        new M3lLoader().LoadFile(FixturePath("order-with-enum.m3l.md"));

    [Fact]
    public void Renders_enum_members_in_PascalCase_with_snake_case_EnumMember()
    {
        var ast = LoadFixture();
        var orderStatus = ast.Enums.Single(e => e.Name == "OrderStatus");

        var rendered = EnumRenderer.Render(orderStatus, "Test.Orders");

        Assert.Contains("public enum OrderStatus", rendered);
        Assert.Contains("[EnumMember(Value = \"in_production\")]", rendered);
        Assert.Contains("    InProduction", rendered);
        Assert.Contains("[EnumMember(Value = \"draft\")]", rendered);
        Assert.Contains("    Draft", rendered);
    }

    [Fact]
    public void Emits_xmldoc_for_enum_and_members_when_description_present()
    {
        var ast = LoadFixture();
        var priority = ast.Enums.Single(e => e.Name == "Priority");

        var rendered = EnumRenderer.Render(priority, "Test.Orders");

        Assert.Contains("/// 주문 우선순위", rendered);
        Assert.Contains("/// 낮음", rendered);
        Assert.Contains("/// 보통", rendered);
    }

    [Fact]
    public void Rendered_enum_parses_as_valid_csharp()
    {
        var ast = LoadFixture();
        foreach (var enumNode in ast.Enums)
        {
            var source = EnumRenderer.Render(enumNode, "Test.Orders");
            var tree = CSharpSyntaxTree.ParseText(source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
            var errors = tree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"Errors in generated enum {enumNode.Name}: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{source}");
        }
    }

    [Fact]
    public void EntityPairRenderer_uses_enum_type_name_for_enum_fields()
    {
        var ast = LoadFixture();
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);

        var pair = EntityPairRenderer.Render(order, "Test.Orders", enumNames);

        // Interface uses bare enum type name (same namespace)
        Assert.Contains("OrderStatus Status { get; }", pair.Interface);
        Assert.Contains("Priority? Priority { get; }", pair.Interface);

        // Write class has no initializer for non-nullable enum field (value type default is valid)
        Assert.Contains("public OrderStatus Status { get; set; }", pair.Write);
        Assert.DoesNotContain("Status { get; set; } = ", pair.Write);

        // Nullable enum renders with `?`
        Assert.Contains("public Priority? Priority { get; set; }", pair.Write);
    }

    [Fact]
    public void Multiline_description_emits_xmldoc_prefix_per_line()
    {
        // 2026-04-27 회귀 차단 — 멀티라인 description의 둘째 줄 이후에 ///가 누락되면
        // "v3+: ..." 같은 텍스트가 C# 코드로 해석되어 컴파일 에러 33건 발생한 yesung 사례.
        var ast = new M3lLoader().LoadFile(FixturePath("enum-multiline-desc.m3l.md"));
        var status = ast.Enums.Single(e => e.Name == "Status");

        var rendered = EnumRenderer.Render(status, "Test.X");

        Assert.Contains("/// 주문 상태.", rendered);
        Assert.Contains("/// 두 번째 줄에 추가 설명.", rendered);
        Assert.Contains("/// 세 번째 줄까지.", rendered);

        // Roslyn 파싱 — 멀티라인이 코드로 해석되면 syntax error 발생
        var tree = CSharpSyntaxTree.ParseText(rendered,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Multiline description이 코드로 해석되어 syntax error: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{rendered}");
    }

    [Fact]
    public void OrderWithEnum_fixture_end_to_end_parses_clean()
    {
        var ast = LoadFixture();
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);

        var sources = new List<(string name, string src)>();
        foreach (var enumNode in ast.Enums)
            sources.Add(($"{enumNode.Name}.cs", EnumRenderer.Render(enumNode, "Test.Orders")));
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
