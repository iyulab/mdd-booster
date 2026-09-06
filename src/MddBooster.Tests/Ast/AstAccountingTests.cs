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

    /// <summary>
    /// A section whose name the language does not define cannot be read by any
    /// target — it arrives as unstructured entries and nothing looks at them.
    /// Dropping it in silence is the same failure the standalone view had.
    /// </summary>
    [Fact]
    public void A_section_the_language_does_not_define_is_reported_unconsumed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
# Namespace: x

## A
- id: identifier @pk @generated
- order_id: identifier @not_null

### PrimaryKey
- fields: [id, order_id]
""");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);

            Assert.Contains("A: ### PrimaryKey", AstAccounting.ListUnconsumed(ast));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// The sections the language does define stay quiet here: `### Indexes` is
    /// read by the Sql target, and reporting a section that is consumed would
    /// train the reader to ignore this warning.
    /// </summary>
    [Fact]
    public void A_consumed_section_is_not_reported_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "table-with-labeled-indexes.m3l.md"));

        Assert.NotEmpty(ast.Models.SelectMany(m => m.Sections?.Indexes ?? []));
        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }

    [Fact]
    public void Model_only_ast_reports_nothing_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "bank-account.m3l.md"));

        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }
}
