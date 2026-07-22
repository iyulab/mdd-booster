using MddBooster.Core.Ast;

namespace MddBooster.Tests.Ast;

/// <summary>
/// 2026-07-22 — standalone `::view`가 생성 파이프라인에서 무경고로 탈락(silent).
/// 회계 원칙: 파싱된 모든 요소는 소비되거나 경고된다.
/// </summary>
public class AstAccountingTests
{
    [Fact]
    public void Standalone_view_is_parsed_into_Views_and_reported_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "standalone-view.m3l.md"));

        // 파서는 §4.7 view를 Models가 아닌 Views 컬렉션으로 분리한다 — 생성기는 Models만 소비.
        Assert.DoesNotContain(ast.Models, m => m.Name == "VAssetFailureStats");
        Assert.Contains(ast.Views, v => v.Name == "VAssetFailureStats");

        var unconsumed = AstAccounting.ListUnconsumed(ast);

        Assert.Contains("::view VAssetFailureStats", unconsumed);
    }

    [Fact]
    public void Model_only_ast_reports_nothing_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "bank-account.m3l.md"));

        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }
}
