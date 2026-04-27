using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class ExtViewRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ViewPlan OrderPlan()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        return new ViewPlanner().Plan(order);
    }

    [Fact]
    public void Renders_create_view_sourcing_full_view_when_lookups_exist()
    {
        var sql = ExtViewRenderer.Render(OrderPlan(), "dbo");

        Assert.Contains("CREATE VIEW [dbo].[OrderExtView]", sql);
        Assert.Contains("FROM [dbo].[OrderFullView] AS b", sql);
    }

    [Fact]
    public void Rollup_count_emits_count_star_subquery()
    {
        var sql = ExtViewRenderer.Render(OrderPlan(), "dbo");
        Assert.Contains("(SELECT COUNT(*) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id]) AS [ItemCount]", sql);
    }

    [Fact]
    public void Rollup_sum_emits_ISNULL_wrapped_subquery_and_indexed_flag_triggers_schemabinding()
    {
        var sql = ExtViewRenderer.Render(OrderPlan(), "dbo");
        Assert.Contains("(SELECT ISNULL(SUM([LineTotal]), 0) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id]) AS [TotalSum]", sql);
        // total_sum is @indexed → SCHEMABINDING
        Assert.Contains("WITH SCHEMABINDING", sql);
    }

    [Fact]
    public void Computed_expression_is_normalized_to_bracketed_PascalCase()
    {
        var sql = ExtViewRenderer.Render(OrderPlan(), "dbo");
        Assert.Contains("[Subtotal] * 0.1 AS [TaxAmount]", sql);
        Assert.Contains("[Subtotal] + [TaxAmount] AS [GrandTotal]", sql);
    }

    [Fact]
    public void Computed_expression_preserves_string_literal_contents()
    {
        // Regression: a CASE like `CASE vat_type WHEN 'taxable' THEN ... END`
        // must keep the literal `'taxable'` as-is rather than transforming the
        // inner word into `[Taxable]`. Without this guard the enum comparison
        // silently fails at runtime because the normalized literal never
        // matches any actual stored value.
        // Inline fixture so the test doesn't depend on external files.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-strlit-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- kind: string(20) @not_null\n" +
            "- amount: decimal(12,0) = 0\n\n" +
            "### Rollup\n" +
            "- total: decimal(12,0) @rollup(Bar.foo_id, sum(amount))\n\n" +
            "### Computed\n" +
            "- adjusted: decimal(12,0) @computed(`CASE kind WHEN 'taxable' THEN total * 0.1 ELSE 0 END`)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n" +
            "- amount: decimal(12,0) = 0\n");
        try
        {
            var ast2 = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast2).ResolveAll().Single(m => m.Name == "Foo");
            var plan2 = new ViewPlanner().Plan(foo);
            var sql = ExtViewRenderer.Render(plan2, "dbo");

            Assert.Contains("'taxable'", sql);
            Assert.DoesNotContain("'[Taxable]'", sql);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Computeds_are_layered_as_chained_CTEs_so_aliases_resolve()
    {
        // Regression: two computeds where the second references the first
        // (plus a rollup) must land in separate CTE layers. SQL Server does
        // not permit SELECT-list aliases to be referenced by sibling aliases
        // in the same SELECT — CTE layering is the fix.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-cte-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- qty: integer = 0\n\n" +
            "### Rollup\n" +
            "- subtotal: decimal(12,0) @rollup(Bar.foo_id, sum(amount))\n\n" +
            "### Computed\n" +
            "- tax: decimal(12,0) @computed(`subtotal * 0.1`)\n" +
            "- grand: decimal(12,0) @computed(`subtotal + tax`)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n" +
            "- amount: decimal(12,0) = 0\n");
        try
        {
            var ast2 = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast2).ResolveAll().Single(m => m.Name == "Foo");
            var plan2 = new ViewPlanner().Plan(foo);
            var sql = ExtViewRenderer.Render(plan2, "dbo");

            // 구조: WITH r(rollups) + c0(tax) + c1(grand)
            Assert.Contains("WITH", sql);
            Assert.Contains("r AS (", sql);
            Assert.Contains("c0 AS (", sql);
            Assert.Contains("c1 AS (", sql);
            // 마지막 레이어에서 전체 선택
            Assert.Contains("SELECT * FROM c1", sql);
            // tax 레이어는 r 를, grand 레이어는 c0 를 참조
            Assert.Contains("FROM r", sql);
            Assert.Contains("FROM c0", sql);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Rollup_only_models_keep_flat_select()
    {
        // 계산 필드가 없는 모델은 self-reference 위험이 없으므로 평탄한 SELECT 를 유지.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-flat-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Foo\n" +
            "- id: identifier @pk @generated\n\n" +
            "### Rollup\n" +
            "- cnt: integer @rollup(Bar.foo_id, count)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n");
        try
        {
            var ast2 = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast2).ResolveAll().Single(m => m.Name == "Foo");
            var plan2 = new ViewPlanner().Plan(foo);
            var sql = ExtViewRenderer.Render(plan2, "dbo");

            Assert.DoesNotContain("WITH", sql);
            Assert.DoesNotContain("c0 AS", sql);
            Assert.Contains("SELECT b.*", sql);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Throws_when_ext_view_not_needed()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single();
        var plan = new ViewPlanner().Plan(model);
        Assert.Throws<InvalidOperationException>(() => ExtViewRenderer.Render(plan, "dbo"));
    }
}
