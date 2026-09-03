using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class FullViewRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    // Mirrors SqlGenerator's construction — model name → PascalCase names of that
    // model's own Lookup/Rollup/Computed fields.
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> DerivedFieldsByModel(
        IEnumerable<ResolvedModel> models, ViewPlanner planner) =>
        models.Select(planner.Plan)
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

    private static string WriteInlineM3l(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-fv-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        return tmp;
    }

    // ── Lookup-only (flat SELECT path) ──────────────────────────────────────

    [Fact]
    public void Lookup_only_renders_flat_select_with_left_join()
    {
        var tmp = WriteInlineM3l(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- customer_id: identifier @reference(Customer) @not_null\n" +
            "- customer_name: string @lookup(customer_id.name)\n\n" +
            "## Customer\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
            var plan = new ViewPlanner().Plan(order);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("CREATE VIEW [dbo].[OrderFullView]", sql);
            Assert.Contains("FROM [dbo].[Order] AS b", sql);
            // Base columns projected explicitly (declaration order), never `b.*`.
            Assert.Contains("SELECT b.[Id], b.[CustomerId],", sql);
            Assert.DoesNotContain("b.*", sql);
            Assert.Contains("LEFT JOIN [dbo].[Customer] AS j_customer_id ON b.[CustomerId] = j_customer_id.[Id]", sql);
            Assert.Contains("j_customer_id.[Name] AS [CustomerName]", sql);
            Assert.DoesNotContain("WITH", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Field_marked_internal_is_excluded_from_the_select_but_base_table_column_stays()
    {
        var tmp = WriteInlineM3l(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- customer_id: identifier @reference(Customer) @not_null\n" +
            "- secret: string @internal\n" +
            "- customer_name: string @lookup(customer_id.name)\n\n" +
            "## Customer\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
            var plan = new ViewPlanner().Plan(order);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("SELECT b.[Id], b.[CustomerId],", sql);
            Assert.DoesNotContain("[Secret]", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Internal_lookup_field_is_excluded_and_its_join_is_dropped_when_unused_elsewhere()
    {
        var tmp = WriteInlineM3l(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- customer_id: identifier @reference(Customer) @not_null\n" +
            "- customer_name: string @lookup(customer_id.name) @internal\n\n" +
            "## Customer\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
            var plan = new ViewPlanner().Plan(order);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.DoesNotContain("CustomerName", sql);
            Assert.DoesNotContain("LEFT JOIN", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Chained_lookup_through_a_lookup_field_joins_the_targets_full_view()
    {
        // `Order.customer_id.name` is a raw column on Customer, so the join targets the base
        // table. But when the chained path's second hop is itself a Lookup field (only
        // projected on {Target}FullView, not the base table), joining to the base table
        // produces a column reference SSDT cannot resolve. The join must target the target
        // model's FullView instead — the same fallback rollup subqueries already use.
        var tmp = WriteInlineM3l(
            "## Region\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Customer\n" +
            "- id: identifier @pk @generated\n" +
            "- region_id: identifier @reference(Region) @not_null\n\n" +
            "### Lookup\n" +
            "- region_name: string @lookup(region_id.name)\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- customer_id: identifier @reference(Customer) @not_null\n\n" +
            "### Lookup\n" +
            "- region_name: string @lookup(customer_id.region_name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var order = models.Single(m => m.Name == "Order");
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);
            var plan = planner.Plan(order);

            var sql = FullViewRenderer.Render(plan, "dbo", derivedFieldsByModel);

            Assert.Contains("LEFT JOIN [dbo].[CustomerFullView] AS j_customer_id", sql);
            Assert.DoesNotContain("LEFT JOIN [dbo].[Customer] AS j_customer_id", sql);
            Assert.Contains("j_customer_id.[RegionName] AS [RegionName]", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Same_named_lookup_column_reused_at_two_chain_depths_does_not_conflict()
    {
        // A derived column exposed directly on one model's FullView, and the *same-named*
        // column reached one hop further via chaining, are not a conflict: each FullView is
        // its own SELECT list, so two views projecting a column under the same alias is
        // unremarkable. This mirrors `Chained_lookup_through_a_lookup_field_joins_the_targets_
        // full_view` one level deeper (three models in a chain, with the leaf reusing the same
        // field name as the middle model's own lookup) — confirming the 0.12.3/0.12.4 fix
        // (see CHANGELOG) still holds at this depth.
        var tmp = WriteInlineM3l(
            "## Enterprise\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- enterprise_id: identifier @reference(Enterprise) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(enterprise_id.name)\n\n" +
            "## OrderItem\n" +
            "- id: identifier @pk @generated\n" +
            "- order_id: identifier @reference(Order) @not_null\n\n" +
            "### Lookup\n" +
            "- customer_name: string @lookup(order_id.customer_name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);

            var orderPlan = planner.Plan(models.Single(m => m.Name == "Order"));
            var orderItemPlan = planner.Plan(models.Single(m => m.Name == "OrderItem"));

            var orderSql = FullViewRenderer.Render(orderPlan, "dbo", derivedFieldsByModel);
            var orderItemSql = FullViewRenderer.Render(orderItemPlan, "dbo", derivedFieldsByModel);

            // Order's own lookup reads Enterprise's raw base column — base table, not a FullView.
            Assert.Contains("LEFT JOIN [dbo].[Enterprise] AS j_enterprise_id", orderSql);
            Assert.Contains("j_enterprise_id.[Name] AS [CustomerName]", orderSql);

            // OrderItem's chained lookup reads Order's own derived CustomerName — must join
            // OrderFullView, not the base Order table (which has no CustomerName column).
            Assert.Contains("LEFT JOIN [dbo].[OrderFullView] AS j_order_id", orderItemSql);
            Assert.DoesNotContain("LEFT JOIN [dbo].[Order] AS j_order_id", orderItemSql);
            Assert.Contains("j_order_id.[CustomerName] AS [CustomerName]", orderItemSql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Self_referencing_lookup_at_a_raw_column_joins_the_base_table_not_its_own_full_view()
    {
        // Regression: a self-referencing FK (Category.parent_id → Category) whose lookup
        // reads a raw column (name) must join the base table. Redirecting to CategoryFullView
        // whenever "the target has a FullView" makes the view reference itself in its own
        // definition — SQL Server can't compute a deployment order for that (SQL72009).
        var tmp = WriteInlineM3l(
            "## Category\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- parent_id: identifier? @reference(Category)?\n\n" +
            "### Lookup\n" +
            "- parent_name: string? @lookup(parent_id.name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var category = models.Single(m => m.Name == "Category");
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);
            var plan = planner.Plan(category);

            var sql = FullViewRenderer.Render(plan, "dbo", derivedFieldsByModel);

            Assert.Contains("LEFT JOIN [dbo].[Category] AS j_parent_id", sql);
            Assert.DoesNotContain("LEFT JOIN [dbo].[CategoryFullView] AS j_parent_id", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Rollup_and_reverse_lookup_over_raw_columns_do_not_create_a_view_cycle()
    {
        // Regression (mdd-booster 0.12.3): Parent.Rollup(Child.parent_id, count) and
        // Child.Lookup(parent_id.name) — a count aggregate and a raw-column lookup — used
        // to redirect to each other's FullView just because a FullView existed at all,
        // producing ParentFullView ⇄ ChildFullView, which SQL Server refuses (SQL72009).
        // Neither actually reads a derived column, so neither should leave the base table.
        var tmp = WriteInlineM3l(
            "## Parent\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "### Rollup\n" +
            "- child_count: integer @rollup(Child.parent_id, count)\n\n" +
            "## Child\n" +
            "- id: identifier @pk @generated\n" +
            "- parent_id: identifier @reference(Parent) @not_null\n\n" +
            "### Lookup\n" +
            "- parent_name: string @lookup(parent_id.name)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll();
            var planner = new ViewPlanner();
            var derivedFieldsByModel = DerivedFieldsByModel(models, planner);

            var parentPlan = planner.Plan(models.Single(m => m.Name == "Parent"));
            var childPlan = planner.Plan(models.Single(m => m.Name == "Child"));

            var parentSql = FullViewRenderer.Render(parentPlan, "dbo", derivedFieldsByModel);
            var childSql = FullViewRenderer.Render(childPlan, "dbo", derivedFieldsByModel);

            Assert.Contains("FROM [dbo].[Child] WHERE [ParentId] = b.[Id]", parentSql);
            Assert.DoesNotContain("[ChildFullView]", parentSql);
            Assert.Contains("LEFT JOIN [dbo].[Parent] AS j_parent_id", childSql);
            Assert.DoesNotContain("[ParentFullView]", childSql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Two_lookups_on_same_fk_produce_one_join()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var plan = new ViewPlanner().Plan(order);

        // order-with-derived has Computeds → CTE path; but still only one JOIN per FK.
        var sql = FullViewRenderer.Render(plan, "dbo");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"LEFT JOIN \[dbo\]\.\[Customer\]"));
    }

    // ── Rollup-only (flat SELECT path) ──────────────────────────────────────

    [Fact]
    public void Rollup_only_renders_flat_select_with_subquery()
    {
        var tmp = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n\n" +
            "### Rollup\n" +
            "- cnt: integer @rollup(Bar.foo_id, count)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Foo");
            var plan = new ViewPlanner().Plan(foo);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("CREATE VIEW [dbo].[FooFullView]", sql);
            Assert.Contains("FROM [dbo].[Foo] AS b", sql);
            Assert.Contains("(SELECT COUNT(*) FROM [dbo].[Bar] WHERE [FooId] = b.[Id]) AS [Cnt]", sql);
            Assert.DoesNotContain("WITH", sql);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Rollup_sum_emits_ISNULL_wrapped_subquery_and_indexed_triggers_schemabinding()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var plan = new ViewPlanner().Plan(order);
        var sql = FullViewRenderer.Render(plan, "dbo");

        Assert.Contains(
            "(SELECT ISNULL(SUM([LineTotal]), 0) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id]) AS [TotalSum]",
            sql);
        Assert.Contains("WITH SCHEMABINDING", sql);
    }

    // `RollupDef` is constructed directly here rather than through `M3lLoader` — the
    // point of these tests is the generator's own handling of an already-parsed
    // `Where` value, decoupled from whichever released parser version happens to be
    // pinned. `RenderRollupSubquery` is `internal` for exactly this seam.

    [Fact]
    public void Rollup_where_clause_filters_the_subquery()
    {
        var def = new RollupDef { Target = "Order", Fk = "customer_id", Aggregate = "count", Where = "status != 'cancelled'" };

        var sql = FullViewRenderer.RenderRollupSubquery(def, "dbo", "b", derivedFieldsByModel: null);

        Assert.Equal(
            "(SELECT COUNT(*) FROM [dbo].[Order] WHERE [CustomerId] = b.[Id] AND ([Status] != 'cancelled'))",
            sql);
    }

    [Fact]
    public void Rollup_where_clause_with_in_list_normalizes_identifiers_but_preserves_string_literals()
    {
        var def = new RollupDef
        {
            Target = "OrderItem",
            Fk = "order_id",
            Aggregate = "count",
            Where = "row_type IN ('product', 'print_order')",
        };

        var sql = FullViewRenderer.RenderRollupSubquery(def, "dbo", "b", derivedFieldsByModel: null);

        Assert.Equal(
            "(SELECT COUNT(*) FROM [dbo].[OrderItem] WHERE [OrderId] = b.[Id] AND ([RowType] IN ('product', 'print_order')))",
            sql);
    }

    [Fact]
    public void Rollup_without_where_clause_is_unfiltered()
    {
        // The AND(...) clause must not appear at all when there is no filter — this
        // is the pre-existing, still-supported plain form.
        var def = new RollupDef { Target = "Bar", Fk = "foo_id", Aggregate = "count" };

        var sql = FullViewRenderer.RenderRollupSubquery(def, "dbo", "b", derivedFieldsByModel: null);

        Assert.Equal("(SELECT COUNT(*) FROM [dbo].[Bar] WHERE [FooId] = b.[Id])", sql);
        Assert.DoesNotContain("AND", sql);
    }

    // Now routed through `M3lLoader`, unlike the three tests above — with the
    // `M3L.Native` pin actually carrying the parser's `where:` fix (docket #174),
    // this closes the integration gap those tests deliberately left open.
    [Fact]
    public void Rollup_where_clause_parsed_from_m3l_source_reaches_the_rendered_subquery()
    {
        var tmp = WriteInlineM3l(
            "## Order\n" +
            "- id: identifier @pk @generated\n" +
            "- customer_id: identifier @reference(Customer) @not_null\n" +
            "- status: string(20) @not_null\n\n" +
            "## Customer\n" +
            "- id: identifier @pk @generated\n" +
            "- active_orders: integer @rollup(Order.customer_id, count, where: \"status != 'cancelled'\")\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var customer = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Customer");
            var plan = new ViewPlanner().Plan(customer);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("WHERE [CustomerId] = b.[Id] AND ([Status] != 'cancelled')", sql);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── Computed (CTE path) ──────────────────────────────────────────────────

    [Fact]
    public void Computed_expressions_use_cte_layers()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var plan = new ViewPlanner().Plan(order);
        var sql = FullViewRenderer.Render(plan, "dbo");

        // CTE structure: r (lookups + rollups), c0 (tax_amount), c1 (grand_total)
        Assert.Contains("WITH", sql);
        Assert.Contains("r AS (", sql);
        Assert.Contains("c0 AS (", sql);
        Assert.Contains("c1 AS (", sql);
        Assert.Contains("SELECT * FROM c1", sql);
        Assert.Contains("[Subtotal] * 0.1 AS [TaxAmount]", sql);
        Assert.Contains("[Subtotal] + [TaxAmount] AS [GrandTotal]", sql);
    }

    [Fact]
    public void Computed_string_literal_preserved()
    {
        var tmp = WriteInlineM3l(
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
            var ast = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Foo");
            var plan = new ViewPlanner().Plan(foo);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("'taxable'", sql);
            Assert.DoesNotContain("'[Taxable]'", sql);
        }
        finally { File.Delete(tmp); }
    }

    // ── UdView as base ───────────────────────────────────────────────────────

    [Fact]
    public void When_model_has_deleted_at_full_view_sources_from_ud_view()
    {
        var tmp = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- bar_id: identifier @reference(Bar) @not_null\n" +
            "- deleted_at: timestamp\n" +
            "- bar_name: string @lookup(bar_id.name)\n\n" +
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var foo = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Foo");
            var plan = new ViewPlanner().Plan(foo);
            var sql = FullViewRenderer.Render(plan, "dbo");

            Assert.Contains("FROM [dbo].[FooUdView] AS b", sql);
            Assert.DoesNotContain("FROM [dbo].[Foo] AS b", sql);
            // Base columns from the UdView are projected explicitly (incl. the FK and deleted_at),
            // never `b.*` — so an added column changes this view's text and re-defines it.
            Assert.Contains("b.[Id], b.[BarId], b.[DeletedAt]", sql);
            Assert.DoesNotContain("b.*", sql);
        }
        finally { File.Delete(tmp); }
    }

    // ── Anti-staleness contract (the reason base columns are explicit) ────────

    [Fact]
    public void Adding_a_base_column_changes_the_view_text_and_lists_the_new_column()
    {
        // The whole point of explicit base columns: an added stored column must change
        // the generated view text so a declarative diff tool re-defines the view instead
        // of leaving a `SELECT *` view silently stale. A future refactor that reintroduces
        // `b.*` would make these two renders identical and fail this test.
        const string bar =
            "## Bar\n" +
            "- id: identifier @pk @generated\n" +
            "- foo_id: identifier @reference(Foo)\n";
        var before = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "### Rollup\n" +
            "- cnt: integer @rollup(Bar.foo_id, count)\n\n" + bar);
        var after = WriteInlineM3l(
            "## Foo\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- billed_date: date\n\n" +               // ← new base column
            "### Rollup\n" +
            "- cnt: integer @rollup(Bar.foo_id, count)\n\n" + bar);
        try
        {
            var sqlBefore = FullViewRenderer.Render(
                new ViewPlanner().Plan(new InterfaceResolver(new M3lLoader().LoadFile(before)).ResolveAll().Single(m => m.Name == "Foo")), "dbo");
            var sqlAfter = FullViewRenderer.Render(
                new ViewPlanner().Plan(new InterfaceResolver(new M3lLoader().LoadFile(after)).ResolveAll().Single(m => m.Name == "Foo")), "dbo");

            Assert.DoesNotContain("[BilledDate]", sqlBefore);
            Assert.Contains("b.[BilledDate]", sqlAfter);
            Assert.NotEqual(sqlBefore, sqlAfter); // text tracks schema → declarative tool re-defines
        }
        finally { File.Delete(before); File.Delete(after); }
    }

    // ── Error guard ──────────────────────────────────────────────────────────

    [Fact]
    public void Throws_when_model_does_not_need_full_view()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single();
        var plan = new ViewPlanner().Plan(model);

        Assert.Throws<InvalidOperationException>(() => FullViewRenderer.Render(plan, "dbo"));
    }
}
