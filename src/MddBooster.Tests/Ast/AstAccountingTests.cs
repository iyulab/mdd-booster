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

    /// <summary>
    /// `### Relations`, `### Behaviors` and `### Metadata` are sections the language
    /// defines, and no target reads any of them — generating the same model with and
    /// without all three produced byte-identical output across every target. Silence
    /// about them is the same silence a section the language does not name got.
    /// </summary>
    private static void AssertSectionIsReportedUnconsumed(string section, string expected)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, $@"# Namespace: x

## B
- id: identifier @pk @generated

## A
- id: identifier @pk @generated
- b_id: identifier @reference(B) @not_null

{section}
");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);

            Assert.Contains(expected, AstAccounting.ListUnconsumed(ast));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void A_relations_section_is_reported_unconsumed() =>
        AssertSectionIsReportedUnconsumed(
            """
### Relations
- >author
  - target: B
  - from: b_id
""",
            "A: ### Relations");

    [Fact]
    public void A_behaviors_section_is_reported_unconsumed() =>
        AssertSectionIsReportedUnconsumed(
            """
### Behaviors
- on_create: set_timestamp
""",
            "A: ### Behaviors");

    [Fact]
    public void A_metadata_section_is_reported_unconsumed() =>
        AssertSectionIsReportedUnconsumed(
            """
### Metadata
- domain: "editorial"
""",
            "A: ### Metadata");

    [Fact]
    public void Model_only_ast_reports_nothing_unconsumed()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "bank-account.m3l.md"));

        Assert.Empty(AstAccounting.ListUnconsumed(ast));
    }

    /// <summary>
    /// 2026-09-09 — 회계가 «섹션 축»만 덮고 있어, 같은 구문의 «필드 축»은 조용했다.
    /// 명세 §3.2.2·§3.2.4 의 관계 표기(<c>- &gt;x</c> · <c>- &lt;&gt;x: many-to-many</c>)를
    /// 파서는 관계로 모델링하지 않고 줄 전체를 필드 이름으로 남긴다(type=null). 그대로
    /// 흘러가면 렌더러가 <i>「필드 '…'에 타입이 없습니다」</i>로 죽었다 — <c>docs/M3L.md</c>
    /// §3.2 는 「<c>@reference</c> 만 읽는다」를 정확히 적어 뒀는데 <b>진단이 다른 층위를
    /// 가리켰다.</b> 경고는 무엇이 안 읽히는지에 더해 «무엇을 쓰면 되는지»까지 말해야 한다.
    /// </summary>
    [Theory]
    [InlineData("- <>tags: many-to-many")]   // §3.2.4 many-to-many
    [InlineData("- >category: one-to-one")]  // §3.2.4 to-relationship
    [InlineData("- <posts: one-to-many")]    // §3.2.4 from-relationship
    [InlineData("- >author")]                // §3.2.2 model-level single line
    public void Field_level_relation_notation_is_reported_unconsumed_and_names_the_read_form(string line)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-rel-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, $"""
            # Namespace: x

            ## Post
            - id: identifier @primary
            - title: string(200)
            {line}
            """);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var unconsumed = AstAccounting.ListUnconsumed(ast);

            var entry = Assert.Single(unconsumed);

            // 「무엇이」 — 사용자가 쓴 줄을 그대로 되돌려 준다.
            Assert.Contains("관계 표기", entry, StringComparison.Ordinal);

            // 「무엇을 쓰면 되는지」 — 이 한마디가 없는 것이 원 결함이었다.
            Assert.Contains(M3lRelationNotation.ReadForm, entry, StringComparison.Ordinal);
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// 판별자가 «진짜 결함»까지 삼키면 안 된다 — 관계 기호로 시작하지 않는 타입-없는
    /// 필드는 종전대로 렌더러 가드가 잡아야 하고, 회계가 미리 흡수해서는 안 된다.
    /// </summary>
    [Fact]
    public void A_typeless_field_that_is_not_relation_notation_is_left_to_the_renderer_guard()
    {
        Assert.False(M3lRelationNotation.IsRelationNotation(
            new M3L.Native.FieldNode { Name = "broken_field", Type = null }));

        // 그리고 타입이 있는 필드는 이름이 무엇이든 관계 표기가 아니다.
        Assert.False(M3lRelationNotation.IsRelationNotation(
            new M3L.Native.FieldNode { Name = ">looks_like_one", Type = "string" }));
    }
}
