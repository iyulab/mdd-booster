using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// 2026-07-22 — 스펙 §10.8.1 정합 회귀. 표준 카탈로그의 `@index`(DB 인덱스)와
/// `@default(value)`(`= value` 구문의 대안)가 SQL 타깃에서 무경고로 탈락하고 있었다.
/// </summary>
public class SpecConformanceTests
{
    private static ResolvedModel LoadItem() =>
        new InterfaceResolver(new M3lLoader().LoadFile(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "spec-conformance.m3l.md")))
            .ResolveAll().Single(m => m.Name == "Item");

    [Fact]
    public void Field_level_index_attribute_emits_nonclustered_index()
    {
        var sql = TableRenderer.Render(LoadItem(), "dbo");

        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_Item_Sku] ON [dbo].[Item] ([Sku]);",
            sql);
    }

    [Fact]
    public void Default_attribute_form_emits_sql_default()
    {
        var sql = TableRenderer.Render(LoadItem(), "dbo");

        Assert.Contains("[Status] NVARCHAR(20) NOT NULL DEFAULT N'active'", sql);
    }

    [Fact]
    public void Equals_syntax_default_still_works()
    {
        var sql = TableRenderer.Render(LoadItem(), "dbo");

        Assert.Contains("[Qty] INT NOT NULL DEFAULT 0", sql);
    }
}
