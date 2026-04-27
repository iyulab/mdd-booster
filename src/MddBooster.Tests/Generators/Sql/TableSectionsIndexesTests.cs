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
        // 하나라도 끼면 filtered unique index로 emit해야 함. yesung Order
        // (part?, season?, original_order_number?) 사례에서 발견된 결함 회귀 차단.
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
