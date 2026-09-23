using MddBooster.Cli.Commands;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Tests.Cli;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A lookup path of more than one hop — <c>@lookup(order_id.customer_id.name)</c> — which M3L
/// defines (§4.5.1) and this generator used to refuse as MDD006, reading the path as one hop to a
/// column literally named <c>customer_id.name</c>.
/// </summary>
/// <remarks>
/// Each hop but the last joins the referenced model's <b>table</b>: it only needs the next key, a
/// stored column. That is what makes the shape worth having — it reaches a column on the far model
/// without depending on the middle model's FullView, which is the dependency that closes a cycle
/// when the middle model also rolls up a derived column of this one.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class MultiHopLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdd-multihop", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // Enterprise ← Order ← OrderItem. Order also rolls up a derived OrderItem column, which is
    // the shape of the reported cycle: a one-hop lookup of Order's own lookup closes it.
    private const string Models =
        "## Enterprise\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n\n" +
        "## Order\n" +
        "- id: identifier @pk @generated\n" +
        "- customer_id: identifier @reference(Enterprise) @not_null\n" +
        "- memo: string(50)\n" +
        "- plain_id: identifier\n" +
        "- opt_customer_id: identifier? @reference(Enterprise)?\n" +
        "- hidden_customer_id: identifier @reference(Enterprise) @internal\n\n" +
        "### Lookup\n" +
        "- customer_name: string @lookup(customer_id.name)\n\n" +
        "### Rollup\n" +
        "- supply_total: decimal(12,0) @rollup(OrderItem.order_id, sum(line_total))\n\n" +
        "## OrderItem\n" +
        "- id: identifier @pk @generated\n" +
        "- order_id: identifier @reference(Order) @not_null\n\n" +
        "### Computed\n" +
        "- line_total: decimal(12,0) @computed(`0`)\n\n" +
        "### Lookup\n";

    private IReadOnlyList<ResolvedModel> Load(string orderItemLookups, out M3L.Native.M3lAst ast)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "model.m3l.md");
        File.WriteAllText(path, "# Namespace: test\n\n" + Models + orderItemLookups);
        ast = new M3lLoader().LoadFile(path);
        return new InterfaceResolver(ast).ResolveAll();
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Derived(IEnumerable<ResolvedModel> models) =>
        models.Select(new ViewPlanner().Plan)
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

    private string RenderOrderItem(string lookups)
    {
        var models = Load(lookups, out _);
        var plan = new ViewPlanner().Plan(models.Single(m => m.Name == "OrderItem"));
        return FullViewRenderer.Render(plan, "dbo", Derived(models), models);
    }

    private IReadOnlyList<SemanticDiagnostic> Analyze(string lookups)
    {
        var models = Load(lookups, out var ast);
        return new SemanticAnalyzer(models.ToList(), ast.Enums).Analyze();
    }

    // ── Semantic analysis ───────────────────────────────────────────────────

    [Fact]
    public void A_two_hop_path_through_references_raises_no_diagnostic()
    {
        Assert.Empty(Analyze("- customer: string @lookup(order_id.customer_id.name)\n"));
    }

    [Fact]
    public void A_later_hop_without_a_reference_is_MDD005_naming_the_model_it_is_on()
    {
        var d = Assert.Single(Analyze("- x: string @lookup(order_id.plain_id.name)\n"));
        Assert.Equal("MDD005", d.Code);
        Assert.Contains("'Order.plain_id'", d.Message);
    }

    [Fact]
    public void A_later_hop_that_does_not_exist_is_MDD004_naming_the_model_it_is_on()
    {
        var d = Assert.Single(Analyze("- x: string @lookup(order_id.nothing_id.name)\n"));
        Assert.Equal("MDD004", d.Code);
        Assert.Contains("'Order'", d.Message);
        Assert.Contains("'nothing_id'", d.Message);
    }

    [Fact]
    public void A_missing_column_at_the_end_of_the_chain_is_MDD006_on_the_final_model()
    {
        var d = Assert.Single(Analyze("- x: string @lookup(order_id.customer_id.nope)\n"));
        Assert.Equal("MDD006", d.Code);
        Assert.Contains("'Enterprise'", d.Message);
        Assert.Contains("'nope'", d.Message);
    }

    // ── T-SQL FullView ──────────────────────────────────────────────────────

    [Fact]
    public void Each_hop_joins_from_the_previous_one_and_reads_tables_not_full_views()
    {
        var sql = RenderOrderItem("- customer: string @lookup(order_id.customer_id.name)\n");

        Assert.Contains("LEFT JOIN [dbo].[Order] AS j_order_id ON b.[OrderId] = j_order_id.[Id]", sql);
        Assert.Contains(
            "LEFT JOIN [dbo].[Enterprise] AS j_order_id__customer_id ON j_order_id.[CustomerId] = j_order_id__customer_id.[Id]",
            sql);
        Assert.Contains("j_order_id__customer_id.[Name] AS [Customer]", sql);
        Assert.DoesNotContain("FullView]", sql[sql.IndexOf("LEFT JOIN", StringComparison.Ordinal)..]);
        Assert.True(
            sql.IndexOf("AS j_order_id ON", StringComparison.Ordinal)
            < sql.IndexOf("AS j_order_id__customer_id ON", StringComparison.Ordinal),
            "a join must come after the join it reads its key from");
    }

    [Fact]
    public void Lookups_sharing_a_prefix_share_its_join()
    {
        var sql = RenderOrderItem(
            "- memo: string @lookup(order_id.memo)\n" +
            "- customer: string @lookup(order_id.customer_id.name)\n");

        Assert.Equal(1, CountOf(sql, "AS j_order_id ON"));
        Assert.Contains("j_order_id.[Memo] AS [Memo]", sql);
        Assert.Contains("j_order_id__customer_id.[Name] AS [Customer]", sql);
    }

    /// <summary>
    /// When the same key is also the end of a lookup that needs the middle model's FullView, the
    /// deeper hop reads its key from that FullView — the dependency exists already — rather than
    /// joining the table a second time.
    /// </summary>
    [Fact]
    public void A_prefix_that_already_reads_the_full_view_is_reused_for_the_next_hop()
    {
        var models = Load(
            "- supply: decimal(12,0) @lookup(order_id.supply_total)\n" +
            "- customer: string @lookup(order_id.customer_id.name)\n", out _);
        var plan = new ViewPlanner().Plan(models.Single(m => m.Name == "OrderItem"));
        // Rendered without the cycle check — this asserts the join shape only.
        var sql = FullViewRenderer.Render(plan, "dbo", Derived(models), models);

        Assert.Contains("LEFT JOIN [dbo].[OrderFullView] AS j_order_id ON b.[OrderId] = j_order_id.[Id]", sql);
        Assert.Contains("ON j_order_id.[CustomerId] = j_order_id__customer_id.[Id]", sql);
        Assert.DoesNotContain("j_order_id_base", sql);
    }

    /// <summary>
    /// The one case where a deeper hop cannot reuse a prefix that reads the FullView: the key it
    /// needs is field-internal, so the FullView does not project it. It gets its own table join.
    /// </summary>
    [Fact]
    public void A_field_internal_next_key_joins_the_table_beside_the_full_view()
    {
        var models = Load(
            "- supply: decimal(12,0) @lookup(order_id.supply_total)\n" +
            "- customer: string @lookup(order_id.hidden_customer_id.name)\n", out _);
        var plan = new ViewPlanner().Plan(models.Single(m => m.Name == "OrderItem"));
        var sql = FullViewRenderer.Render(plan, "dbo", Derived(models), models);

        Assert.Contains("LEFT JOIN [dbo].[OrderFullView] AS j_order_id ON b.[OrderId] = j_order_id.[Id]", sql);
        Assert.Contains("LEFT JOIN [dbo].[Order] AS j_order_id_base ON b.[OrderId] = j_order_id_base.[Id]", sql);
        Assert.Contains("ON j_order_id_base.[HiddenCustomerId] = j_order_id__hidden_customer_id.[Id]", sql);
    }

    // ── Nullability on the generated types ──────────────────────────────────

    /// <summary>
    /// M3L §4.5.4: an optional key at any hop makes the result optional — each hop is a LEFT JOIN.
    /// The key here is required on OrderItem and optional one model further along.
    /// </summary>
    [Fact]
    public void An_optional_key_at_a_later_hop_makes_the_generated_property_optional()
    {
        var models = Load(
            "- via_required: string @lookup(order_id.customer_id.name)\n" +
            "- via_optional: string @lookup(order_id.opt_customer_id.name)\n", out _);
        var item = models.Single(m => m.Name == "OrderItem");

        var pair = MddBooster.Generators.Model.EntityPairRenderer.Render(item, "Test.Entities", allModels: models);
        Assert.Contains("public string? ViaOptional", pair.Read);
        Assert.DoesNotContain("public string? ViaRequired", pair.Read);

        var ts = MddBooster.Generators.TypeScript.TsInterfaceRenderer.RenderAll(models);
        Assert.Contains("ViaOptional?: string | null", ts);
        Assert.Contains("ViaRequired?: string" + Environment.NewLine, ts);
    }

    // ── The reported cycle ──────────────────────────────────────────────────

    private void Generate(string lookups)
    {
        var models = Load(lookups, out var ast);
        new SqlGenerator(new SqlGeneratorOptions { ProjectPath = ".", EmitSqlProj = false, EmitRefreshScript = false })
            .Generate(new GeneratorContext { Models = models, Enums = ast.Enums, WorkingDirectory = _root });
    }

    /// <summary>
    /// The control: the one-hop lookup of Order's own lookup still closes the cycle — and the way
    /// out the error names is the multi-hop form, which the next test shows builds. The guidance
    /// used to name a way out this generator refused.
    /// </summary>
    [Fact]
    public void The_one_hop_path_through_a_derived_column_still_fails_as_a_cycle_and_names_a_way_out_that_builds()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Generate("- customer: string @lookup(order_id.customer_name)\n"));
        Assert.Contains("Circular FullView dependency", ex.Message);
        Assert.Contains("@lookup(order_id.customer_id.name)", ex.Message);
    }

    /// <summary>The same value reached through raw keys builds — no view depends on another's.</summary>
    [Fact]
    public void The_two_hop_path_to_the_same_value_builds_without_a_cycle()
    {
        Generate("- customer: string @lookup(order_id.customer_id.name)\n");

        var sql = File.ReadAllText(Path.Combine(_root, "dbo", "Views_gen", "OrderItemFullView.sql"));
        Assert.Contains("j_order_id__customer_id.[Name] AS [Customer]", sql);
        Assert.DoesNotContain("[OrderFullView]", sql);
    }

    // ── PostgreSQL ──────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_chains_the_same_joins_with_each_targets_own_key()
    {
        // `Order` is reserved in PG — the same shape under other names.
        var mddDir = Path.Combine(_root, "mdd");
        var dbDir = Path.Combine(_root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"),
            "# Namespace: X\n\n" +
            "## Company\n- id: identifier @pk @generated\n- name: string(50) @not_null\n\n" +
            "## Sale\n- id: identifier @pk @generated\n- company_id: identifier @reference(Company)\n\n" +
            "## SaleLine\n- id: identifier @pk @generated\n- sale_id: identifier @reference(Sale) @not_null\n" +
            "- company_name: string @lookup(sale_id.company_id.name)\n");
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{ \"sources\": [\"./model.m3l.md\"], \"targets\": [{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }] }");

        using var captured = new ConsoleErrorCapture(this);
        Assert.Equal(0, new BuildCommand().Run(mddDir));

        var sql = File.ReadAllText(Path.Combine(dbDir, "views_gen", "sale_line_full_view.sql"));
        Assert.Contains("LEFT JOIN public.sale AS j_sale_id ON b.sale_id = j_sale_id.id", sql);
        Assert.Contains("LEFT JOIN public.company AS j_sale_id__company_id ON j_sale_id.company_id = j_sale_id__company_id.id", sql);
        Assert.Contains("j_sale_id__company_id.name AS company_name", sql);
        Assert.DoesNotContain("[sql-pg]", captured.Text);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
