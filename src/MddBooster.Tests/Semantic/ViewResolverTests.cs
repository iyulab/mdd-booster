using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

public class ViewResolverTests
{
    private static ViewResolution ResolveFile(string path)
    {
        var ast = new M3lLoader().LoadFile(path);
        return ViewResolver.Resolve(ast.Views, new InterfaceResolver(ast).ResolveAll());
    }

    private static ViewResolution ResolveFixture() =>
        ResolveFile(Path.Combine(AppContext.BaseDirectory, "fixtures", "standalone-views.m3l.md"));

    private const string Models = """
        # Namespace: test

        ## Customer
        - id: identifier @pk @generated
        - name: string(100) @not_null

        ## Order
        - id: identifier @pk @generated
        - customer_id: identifier @reference(Customer)
        - total_amount: decimal(12,2)

        """;

    private static ViewResolution ResolveInline(string views)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-views-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, Models + views);
        try { return ResolveFile(tmp); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void The_specification_examples_resolve_without_diagnostics()
    {
        var result = ResolveFixture();

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["ActiveProducts", "OrderDetail", "CustomerStats", "BigSpenders"],
            result.Views.Select(v => v.Name));
    }

    [Fact]
    public void A_join_condition_resolves_to_columns_of_the_relations_it_names()
    {
        var detail = ResolveFixture().Views.Single(v => v.Name == "OrderDetail");

        Assert.Equal(["Customer", "OrderItem", "Product"], detail.Joins.Select(j => j.Target.Name));
        var product = detail.Joins[2].On.Single();
        Assert.Equal(("OrderItem", "product_id"), (product.Left.Relation.Name, product.Left.Column));
        Assert.Equal(("Product", "id"), (product.Right.Relation.Name, product.Right.Column));
        var email = Assert.IsType<ViewFromColumn>(detail.Columns.Single(c => c.Name == "customer_email"));
        Assert.Equal(("Customer", "email"), (email.Source.Relation.Name, email.Source.Column));
    }

    [Fact]
    public void An_aggregate_view_keeps_its_group_and_its_rollups()
    {
        var stats = ResolveFixture().Views.Single(v => v.Name == "CustomerStats");

        Assert.Equal(["id", "name", "email"], stats.GroupBy.Select(g => g.Column));
        var spent = Assert.IsType<ViewRollupColumn>(stats.Columns.Single(c => c.Name == "total_spent"));
        Assert.Equal(("Order", "customer_id", "sum", "total_amount"),
            (spent.Target.Name, spent.Rollup.Fk, spent.Rollup.Aggregate, spent.Rollup.Field));
        Assert.False(stats.HasExpressions);
    }

    [Fact]
    public void A_nested_view_reads_the_view_it_names_and_comes_after_it()
    {
        var views = ResolveFixture().Views;
        var big = views.Single(v => v.Name == "BigSpenders");

        Assert.Equal(ViewRelationKind.View, big.From.Kind);
        Assert.Same(views.Single(v => v.Name == "CustomerStats"), big.From.View);
        Assert.Equal(2, big.Depth);
        Assert.True(views.ToList().IndexOf(big) > views.ToList().FindIndex(v => v.Name == "CustomerStats"));
    }

    [Fact]
    public void A_where_or_computed_column_is_carried_as_an_expression()
    {
        var active = ResolveFixture().Views.Single(v => v.Name == "ActiveProducts");

        Assert.Equal("is_active = true AND stock_quantity > 0", active.Where);
        Assert.True(active.HasExpressions);
    }

    [Theory]
    [InlineData("- join: Nowhere on Nowhere.id = Customer.id", "정의되지 않은 'Nowhere'")]
    [InlineData("- join: Order on Order.customer_id > Customer.id", "해석할 수 없다")]
    [InlineData("- join: Order on Order.customer_id = Customer.id OR Order.id = Customer.id", "해석할 수 없다")]
    [InlineData("- join: Order on Order.missing_id = Customer.id", "'Order' 에 'missing_id' 이(가) 없다")]
    [InlineData("- join: Order on Customer.id = Customer.id", "'Order' 을(를) 쓰지 않는다")]
    public void A_join_that_does_not_resolve_is_an_error(string join, string expected)
    {
        var result = ResolveInline($$"""
            ## V ::view
            ### Source
            - from: Customer
            {{join}}

            - name: string @from(Customer.name)
            """);

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(("MDD030", SemanticSeverity.Error), (error.Code, error.Severity));
        Assert.Contains(expected, error.Message);
        Assert.Empty(result.Views);
    }

    [Theory]
    [InlineData("- name: string @from(Ghost.name)", "이 뷰의 from/join 이 아니다")]
    [InlineData("- name: string @from(Customer.nickname)", "'Customer' 에 'nickname' 이(가) 없다")]
    [InlineData("- name: string", "값의 원천이 없다")]
    [InlineData("- n: integer @rollup(Order.missing_id, count)", "'Order' 에 'missing_id' 이(가) 없다")]
    public void A_column_that_does_not_resolve_is_an_error(string column, string expected)
    {
        var result = ResolveInline($$"""
            ## V ::view
            ### Source
            - from: Customer

            {{column}}
            """);

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("MDD030", error.Code);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Views_that_read_each_other_are_a_cycle_error()
    {
        var result = ResolveInline("""
            ## A ::view
            ### Source
            - from: B

            - name: string @from(B.name)

            ## B ::view
            ### Source
            - from: A

            - name: string @from(A.name)
            """);

        Assert.Contains(result.Diagnostics, d => d.Code == "MDD030" && d.Message.Contains("A → B → A"));
        Assert.Empty(result.Views);
    }

    [Fact]
    public void A_sql_block_source_is_left_out_with_a_warning()
    {
        var result = ResolveInline("""
            ## V ::view
            ### Source
            ```sql
            SELECT c.name FROM Customer c
            ```

            - name: string @from(Customer.name)
            """);

        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(("MDD028", SemanticSeverity.Warning), (warning.Code, warning.Severity));
        Assert.Empty(result.Views);
    }

    [Theory]
    [InlineData("## V ::view @materialized", "- from: Customer", "- name: string @from(Customer.name)", "구체화")]
    [InlineData("## V ::view", "- from: Order", "- who: string @lookup(customer_id.name)", "@lookup")]
    [InlineData("## V ::view", "- from: Customer\n- group_by: [label]", "- label: string @from(Customer.name)", "출력 열")]
    public void A_shape_this_generator_does_not_emit_is_left_out_with_a_warning(
        string header, string source, string column, string expected)
    {
        var result = ResolveInline($"""
            {header}
            ### Source
            {source}

            {column}
            """);

        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(("MDD031", SemanticSeverity.Warning), (warning.Code, warning.Severity));
        Assert.Contains(expected, warning.Message);
        Assert.Empty(result.Views);
    }

    [Fact]
    public void Nesting_deeper_than_two_views_is_generated_with_a_warning()
    {
        var result = ResolveInline("""
            ## V1 ::view
            ### Source
            - from: Customer

            - name: string @from(Customer.name)

            ## V2 ::view
            ### Source
            - from: V1

            - name: string @from(V1.name)

            ## V3 ::view
            ### Source
            - from: V2

            - name: string @from(V2.name)
            """);

        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(("MDD032", SemanticSeverity.Warning), (warning.Code, warning.Severity));
        Assert.Equal(3, result.Views.Count);
        Assert.Equal(3, result.Views.Single(v => v.Name == "V3").Depth);
    }
}
