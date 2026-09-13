using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// 2026-04-27 — `### Indexes` 섹션의 directive 형식 (`- @unique(...)` / `- @index(...)`)이
/// 각각 `CONSTRAINT UNIQUE NONCLUSTERED` / `CREATE NONCLUSTERED INDEX`로 emit되는지 회귀 차단.
/// </summary>
public class TableSectionsIndexesTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Directive_unique_with_nullable_columns_emits_filtered_index()
    {
        // SQL Server UNIQUE 제약은 NULL 다중을 허용하지 않으므로, nullable 컬럼이
        // 하나라도 끼면 filtered unique index로 emit해야 한다. 널 허용 컬럼을 포함한
        // 복합 유니크 선언은 그러지 않으면 두 번째 NULL 행에서 실패한다 — 회귀 차단.
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains(
            "CREATE UNIQUE NONCLUSTERED INDEX [UK_Order_Part_Season_OriginalNumber] ON [dbo].[Order] ([Part], [Season], [OriginalNumber]) WHERE [Part] IS NOT NULL AND [Season] IS NOT NULL AND [OriginalNumber] IS NOT NULL;",
            sql);
        // inline CONSTRAINT으로 emit되면 안 됨 (NULL 다중 허용 위반)
        Assert.DoesNotContain(
            "CONSTRAINT [UK_Order_Part_Season_OriginalNumber] UNIQUE NONCLUSTERED",
            sql);
    }

    [Fact]
    public void Directive_unique_with_all_not_null_columns_emits_inline_constraint()
    {
        // 모든 UK 컬럼이 NOT NULL이면 기존처럼 inline CONSTRAINT.
        // EnterpriseRole(EnterpriseId, RoleType) 같은 케이스.
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Membership");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains(
            "CONSTRAINT [UK_Membership_UserId_GroupId] UNIQUE NONCLUSTERED ([UserId], [GroupId])",
            sql);
        Assert.DoesNotContain(
            "CREATE UNIQUE NONCLUSTERED INDEX [UK_Membership_UserId_GroupId]",
            sql);
    }

    [Fact]
    public void Directive_index_emits_post_table_create_index()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_Order_CustomerId] ON [dbo].[Order] ([CustomerId]);",
            sql);
        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_Order_Status_Season] ON [dbo].[Order] ([Status], [Season]);",
            sql);
    }

    [Fact]
    public void Labeled_indexes_emit_same_as_directive_form()
    {
        // M3L.Native 0.5.5+ 라벨드 형식 지원 — `- idx_xxx: @index(col)` 도
        // attribute 정보를 보존하므로 directive 형식과 동등하게 emit되어야 함.
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-labeled-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_Order_CustomerId] ON [dbo].[Order] ([CustomerId]);",
            sql);
        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_Order_Status_Season] ON [dbo].[Order] ([Status], [Season]);",
            sql);
        Assert.Contains(
            "CONSTRAINT [UK_Order_CustomerId_Season] UNIQUE NONCLUSTERED ([CustomerId], [Season])",
            sql);
    }

    // ---- `nulls: "not_distinct"` (docket #271) ----
    //
    // 현재 핀(M3L.Native 0.8.0)은 아직 `nulls` 필드를 내지 않는다 — m3l 0.9.0이 게시되고
    // 핀이 올라갈 때까지는 합성 `ResolvedModel`로 이 분기를 고정한다
    // (`SectionIndexParserTests.WithEntries`와 같은 이유).

    private static ResolvedModel EnterpriseChannelDefaultFixture(string? nulls) =>
        new()
        {
            Name = "EnterpriseChannelDefault",
            Fields =
            [
                new FieldNode { Name = "enterprise_id", Type = "identifier", Nullable = true },
                new FieldNode { Name = "channel", Type = "string", Nullable = true },
                new FieldNode { Name = "part", Type = "string", Nullable = false },
            ],
            Source = new ModelNode
            {
                Name = "EnterpriseChannelDefault",
                Sections = new Sections
                {
                    Indexes =
                    [
                        JsonDocument.Parse(
                            $$"""
                            {
                              "type": "directive",
                              "unique": true,
                              "args": ["enterprise_id", "channel", "part"]
                              {{(nulls is null ? "" : $""", "nulls": "{nulls}" """)}}
                            }
                            """).RootElement.Clone(),
                    ],
                },
            },
        };

    [Fact]
    public void Nulls_not_distinct_emits_inline_constraint_despite_nullable_columns()
    {
        // 기본값(위 두 테스트)과 정반대 요구 — NULL이 "범위 전체"를 뜻하는 폴백 테이블은
        // filtered index로 NULL 행을 제외하면 유일성이 약해진다(docket #271 원 신고). SQL
        // Server의 plain UNIQUE는 NULL을 값으로 취급해 이미 NULLS NOT DISTINCT를 구현하므로,
        // 필터 없는 inline CONSTRAINT로 충분하다 — 새 SQL 문법을 발명하지 않는다.
        var sql = TableRenderer.Render(
            EnterpriseChannelDefaultFixture(nulls: "not_distinct"), schema: "dbo");

        Assert.Contains(
            "CONSTRAINT [UK_EnterpriseChannelDefault_EnterpriseId_Channel_Part] UNIQUE NONCLUSTERED ([EnterpriseId], [Channel], [Part])",
            sql);
        Assert.DoesNotContain(
            "CREATE UNIQUE NONCLUSTERED INDEX [UK_EnterpriseChannelDefault_EnterpriseId_Channel_Part]",
            sql);
    }

    [Fact]
    public void Absent_nulls_field_keeps_default_filtered_index_behavior()
    {
        // 회귀 차단 — `nulls` 필드가 아예 없으면(현재 핀의 실제 산출물 형태) 기존 필터드
        // 인덱스 기본값이 그대로여야 한다. `Directive_unique_with_nullable_columns_emits_filtered_index`
        // 와 같은 주장을 합성 픽스처로도 한 번 더 고정한다 — 실제 파서가 `nulls` 필드 자체를
        // 내지 않는 상황을 정확히 재현하는 유일한 테스트다.
        var sql = TableRenderer.Render(
            EnterpriseChannelDefaultFixture(nulls: null), schema: "dbo");

        Assert.Contains(
            "CREATE UNIQUE NONCLUSTERED INDEX [UK_EnterpriseChannelDefault_EnterpriseId_Channel_Part] ON [dbo].[EnterpriseChannelDefault] ([EnterpriseId], [Channel], [Part]) WHERE [EnterpriseId] IS NOT NULL AND [Channel] IS NOT NULL AND [Part] IS NOT NULL;",
            sql);
    }

    [Fact]
    public void Directive_index_emits_after_create_table_go()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo").Replace("\r\n", "\n");

        // 인덱스는 CREATE TABLE GO 뒤에 위치해야 함 (테이블 본문 안에 들어가면 SSDT 파싱 에러)
        var tableEnd = sql.IndexOf(")\nGO\n", StringComparison.Ordinal);
        var firstIndex = sql.IndexOf("CREATE NONCLUSTERED INDEX", StringComparison.Ordinal);
        Assert.True(tableEnd > 0 && firstIndex > tableEnd,
            $"인덱스가 CREATE TABLE 본문 뒤에 위치해야 함. tableEnd={tableEnd}, firstIndex={firstIndex}");
    }
}
