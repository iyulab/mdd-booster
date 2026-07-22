using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// 2026-07-22 — 미인식 속성 경고. 스펙 §10.8은 카탈로그 밖 속성을 custom으로
/// 허용하므로 무차별 경고는 오탐이다. 알려진 어휘와 편집거리 ≤2인 이름만
/// "오타 의심" Warning(MDD006)으로 보고하고, 그 외 custom은 침묵한다.
/// Warning은 빌드를 실패시키지 않는다 (Error만 exit≠0).
/// </summary>
public class AttributeTypoTests
{
    private static IReadOnlyList<SemanticDiagnostic> Analyze()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "attr-typo.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        return new SemanticAnalyzer(models, ast.Enums.ToList()).Analyze();
    }

    [Fact]
    public void Typo_near_known_attribute_yields_warning_with_suggestion()
    {
        var diagnostics = Analyze();

        var typo = Assert.Single(diagnostics, d => d.Code == "MDD006");
        Assert.Equal(SemanticSeverity.Warning, typo.Severity);
        Assert.Contains("uniqe", typo.Message);
        Assert.Contains("unique", typo.Message);
    }

    [Fact]
    public void Distant_custom_attribute_stays_silent()
    {
        var diagnostics = Analyze();

        // @tenant_scope: 합법 custom (스펙 §10.8) — 어떤 진단도 내지 않는다.
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("tenant_scope"));
    }

    [Fact]
    public void Warnings_do_not_carry_error_severity()
    {
        var diagnostics = Analyze();

        Assert.DoesNotContain(diagnostics,
            d => d.Code == "MDD006" && d.Severity == SemanticSeverity.Error);
    }
}
