using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// `### Indexes` 항목을 컬럼 목록 + unique 여부로 읽어내는 공유 파서.
/// </summary>
/// <remarks>
/// 이 파싱은 두 방언 렌더러와 외래키 인덱스 판정기가 함께 쓴다. 셋이 각자 읽으면
/// 상류 AST 의 entry 형식이 바뀔 때 세 곳을 같이 고쳐야 하고, 한 곳을 빠뜨려도
/// 산출물이 조용히 갈린다 — 그래서 형식 해석은 여기 한 곳에만 있다.
/// <para>
/// 소비처마다 필요한 것은 다르다(전체 컬럼 vs 선두 컬럼, PascalCase 변환 여부,
/// 널 허용 컬럼 분기). 그것들은 각 소비처에 남고, 여기서는 컬럼명을 <em>원문 그대로</em>
/// 돌려준다.
/// </para>
/// </remarks>
public class SectionIndexParserTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ResolvedModel Load(string fixture, string modelName)
    {
        var ast = new M3lLoader().LoadFile(FixturePath(fixture));
        return new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == modelName);
    }

    /// <summary>합성 entry — 현재 상류 파서가 내지 않는 형태까지 고정하기 위한 것.</summary>
    private static ResolvedModel WithEntries(params string[] json) =>
        new()
        {
            Name = "Synthetic",
            Fields = [],
            Source = new ModelNode
            {
                Name = "Synthetic",
                Sections = new Sections
                {
                    Indexes = [.. json.Select(j => JsonDocument.Parse(j).RootElement.Clone())],
                },
            },
        };

    // ---- 실제 파서 산출물 (M3lLoader 경유) ----

    [Fact]
    public void Directive_entries_carry_every_declared_column_in_order()
    {
        // 선두 컬럼만 읽으면 복합 인덱스가 단일 인덱스로 무너진다.
        var entries = SectionIndexParser.Parse(Load("table-with-indexes.m3l.md", "Order"));

        Assert.Collection(entries,
            e =>
            {
                Assert.Equal(["part", "season", "original_number"], e.Columns);
                Assert.True(e.IsUnique);
            },
            e =>
            {
                Assert.Equal(["customer_id"], e.Columns);
                Assert.False(e.IsUnique);
            },
            e =>
            {
                Assert.Equal(["status", "season"], e.Columns);
                Assert.False(e.IsUnique);
            });
    }

    [Fact]
    public void Labeled_entries_are_read_the_same_as_directive_entries()
    {
        // `idx_xxx: @index(col)` 형식도 attribute 정보를 그대로 보유한다.
        var entries = SectionIndexParser.Parse(
            Load("table-with-labeled-indexes.m3l.md", "Order"));

        Assert.Collection(entries,
            e =>
            {
                Assert.Equal(["customer_id"], e.Columns);
                Assert.False(e.IsUnique);
            },
            e =>
            {
                Assert.Equal(["status", "season"], e.Columns);
                Assert.False(e.IsUnique);
            },
            e =>
            {
                Assert.Equal(["customer_id", "season"], e.Columns);
                Assert.True(e.IsUnique);
            });
    }

    [Fact]
    public void Column_names_keep_their_source_casing()
    {
        // 방언별 케이싱 변환은 소비처의 몫이다 — 여기서 한쪽 방언으로 바꿔 두면
        // 다른 방언이 되돌리는 변환을 하게 된다.
        var entries = SectionIndexParser.Parse(Load("table-with-indexes.m3l.md", "Order"));

        Assert.Contains("original_number", entries.SelectMany(e => e.Columns));
    }

    // ---- 상류가 형식을 바꿔도 버텨야 하는 자리 ----

    [Fact]
    public void Scalar_args_are_read_as_a_single_column()
    {
        // 현재 핀에서는 나오지 않는 형태다. 이 관용을 지키는 이유는 호환이 아니라 «조용함»이다 —
        // 디코딩에 실패한 항목은 컬럼 0개가 되어 인덱스째 탈락하는데, `### Indexes` 는 소비되는
        // 섹션이라 미소비 요소 회계에서 의도적으로 제외돼 있어 그 탈락을 알릴 곳이 없다.
        var entries = SectionIndexParser.Parse(
            WithEntries("""{"type":"directive","args":"customer_id"}"""));

        var entry = Assert.Single(entries);
        Assert.Equal(["customer_id"], entry.Columns);
    }

    [Fact]
    public void Unique_is_false_when_the_entry_does_not_say()
    {
        var entries = SectionIndexParser.Parse(
            WithEntries("""{"type":"directive","args":["customer_id"]}"""));

        Assert.False(Assert.Single(entries).IsUnique);
    }

    [Fact]
    public void Entries_of_an_unrecognized_type_are_skipped_even_with_usable_args()
    {
        // 인덱스 선언이 아닌 항목이 `### Indexes` 아래 섞여 들어올 수 있다.
        var entries = SectionIndexParser.Parse(
            WithEntries("""{"type":"text","args":["customer_id"]}"""));

        Assert.Empty(entries);
    }

    [Theory]
    [InlineData("\"just a string\"")]                        // entry 가 객체가 아님
    [InlineData("""{"args":["customer_id"]}""")]             // type 없음
    [InlineData("""{"type":"directive"}""")]                 // args 없음
    [InlineData("""{"type":"directive","args":[]}""")]       // 컬럼 없음
    [InlineData("""{"type":"directive","args":["  "]}""")]   // 공백뿐인 컬럼
    public void Entries_that_yield_no_column_are_skipped(string json)
    {
        // 컬럼을 모르면 인덱스를 만들 수 없다 — 이름만 있는 산출물을 내지 않는다.
        Assert.Empty(SectionIndexParser.Parse(WithEntries(json)));
    }
}
