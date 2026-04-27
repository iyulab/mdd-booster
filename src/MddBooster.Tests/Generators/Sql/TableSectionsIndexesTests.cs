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
    public void Directive_unique_emits_named_unique_constraint()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("table-with-indexes.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(resolved, schema: "dbo");

        Assert.Contains(
            "CONSTRAINT [UK_Order_Part_Season_OriginalNumber] UNIQUE NONCLUSTERED ([Part], [Season], [OriginalNumber])",
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
