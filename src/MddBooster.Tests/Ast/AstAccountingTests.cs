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
    /// 2026-09-09 — M3L.Native 0.8.0부터 파서 자신이 관계 표기(<c>- &gt;x</c> ·
    /// <c>- &lt;&gt;x: many-to-many</c>, 명세 §3.2.2·§3.2.4)를 필드 목록이 아니라
    /// <c>sections.relations</c>로 구조화해 내고(<c>declaredIn: "fields"</c>로 표시),
    /// <c>M3L-W009</c>로도 경고한다. 그 구조화 덕에 이 회계는 더 이상 필드 이름을 직접
    /// 판별할 필요가 없다 — 어떤 표기였는지는 파서가 낸 <c>raw</c>에서 그대로 읽는다.
    /// 문구는 종전과 같다: 무엇을 썼는지, 그리고 관계는 <c>@reference</c>로만 읽는다는 것.
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

            // 「무엇을 쓰면 되는지」.
            Assert.Contains(AstAccounting.RelationReadForm, entry, StringComparison.Ordinal);
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// <c>### Relations</c> 섹션 자체(필드 축이 아니라)는 여전히 이 일반 문구로만 보고된다 —
    /// 그 섹션은 어떤 개별 항목을 이름 대지 않아도 될 만큼 이미 자기 위치를 말하고 있다.
    /// </summary>
    [Fact]
    public void Relations_section_itself_is_still_reported_generically()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-acct-relsec-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, """
            # Namespace: x

            ## Post
            - id: identifier @primary
            - title: string(200)

            ### Relations
            - author: >Author
            """);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var unconsumed = AstAccounting.ListUnconsumed(ast);

            var entry = Assert.Single(unconsumed);
            Assert.Equal("Post: ### Relations", entry);
        }
        finally { File.Delete(tmp); }
    }
}
