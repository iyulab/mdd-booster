using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A standalone <c>::view</c> that projects a key is served as a read model: an entity mapped to the view, a set on
/// the context, a read-only registration and controller, a read permission. A view with no key is still SQL and
/// nothing more — and the build says which, so its absence from the entities is not a surprise.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class StandaloneViewReadModelBuildTests
{
    // The specification's view examples: ActiveProducts projects `id` (a read model); OrderDetail, CustomerStats
    // and BigSpenders do not.
    private static string SpecFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "standalone-views.m3l.md"));

    private const string Timestamps = """

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

""";

    // The same view over one model, without the specification fixture's `Order` — a reserved word on PostgreSQL.
    private const string ProductOnly = """
# Namespace: test.views

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Product : Timestampable
- id: identifier @pk @generated
- name: string(100) @not_null
- stock_quantity: integer @not_null
- is_active: boolean @not_null

## ActiveProducts ::view

### Source
- from: Product
- where: "is_active = true AND stock_quantity > 0"

- id: identifier @from(Product.id)
- name: string @from(Product.name)
- stock: integer @from(Product.stock_quantity)
""";

    private const string TypeScriptTarget = """, { "type": "TypeScript", "outputPath": "../ts" }""";

    [Fact]
    public void The_typescript_target_gets_the_views_read_type_and_lists_its_set_apart_from_the_written_ones()
    {
        using var o = Build("tsql", ProductOnly, extraTargets: TypeScriptTarget);

        // The view's own columns, each required, and no audit timestamps: it does not extend the entity base.
        var entities = o.Read("ts", "entities_gen.ts");
        Assert.Contains("export interface ActiveProducts {\n  Id: string\n  Name: string\n  Stock: number\n}\n", entities);

        // A set that refuses every write is not an EntitySetName — code typed on that name writes.
        var names = o.Read("ts", "entity_names_gen.ts");
        Assert.Contains("export const ENTITY_NAMES = [\n  'Products',\n] as const", names);
        Assert.Contains("export const READ_ONLY_SET_NAMES = [\n  'ActiveProducts',\n] as const", names);
        Assert.Contains("export type ReadOnlySetName = typeof READ_ONLY_SET_NAMES[number]", names);

        // Nothing is entered into a view: no write-side schema for it.
        Assert.DoesNotContain("ActiveProducts", o.Read("ts", "field_schema_gen.ts"));
    }

    [Fact]
    public void A_model_with_no_read_model_view_still_declares_the_read_only_list_empty()
    {
        var model = ProductOnly.Replace("## ActiveProducts ::view", "## ActiveProducts ::view @internal");
        using var o = Build("tsql", model, extraTargets: TypeScriptTarget);

        Assert.Contains("export const READ_ONLY_SET_NAMES = [\n] as const", o.Read("ts", "entity_names_gen.ts"));
        Assert.DoesNotContain("ActiveProducts", o.Read("ts", "entities_gen.ts"));
    }

    [Fact]
    public void A_typescript_target_listing_a_view_no_api_target_registers_is_warned()
    {
        var (output, exit, stderr) = Run(
            "tsql", ProductOnly, apiExtra: """, "excludeEntities": ["ActiveProducts"]""", extraTargets: TypeScriptTarget);
        using (output)
        {
            Assert.True(exit == 0, stderr);
            Assert.Contains("ReadOnlySetName", stderr);
            Assert.Contains("ActiveProducts", stderr);
        }
    }

    [Theory]
    [InlineData("ProductExt", "클래스 'ProductExt' 는 모델 'Product' 의 읽기 타입")]
    [InlineData("Products", "셋 'Products' 는 모델 'Product' 의 셋")]
    public void A_view_whose_generated_name_is_a_models_stops_the_build(string viewName, string expected)
    {
        var model = ProductOnly.Replace("## ActiveProducts ::view", $"## {viewName} ::view");

        var (exit, stderr) = TryBuild(model);

        Assert.Equal(3, exit);
        Assert.Contains("MDD038", stderr);
        Assert.Contains(expected, stderr);
    }

    [Fact]
    public void An_internal_view_may_share_a_generated_name_because_it_generates_none()
    {
        var model = ProductOnly.Replace("## ActiveProducts ::view", "## ProductExt ::view @internal");

        var (exit, stderr) = TryBuild(model);

        Assert.True(exit == 0, stderr);
    }

    private sealed record Output(string Root, string Stdout) : IDisposable
    {
        public string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path])).ReplaceLineEndings("\n");

        public bool Exists(params string[] path) => File.Exists(Path.Combine([Root, .. path]));

        public string Api(string file)
            => File.ReadAllText(Directory.GetFiles(Root, file, SearchOption.AllDirectories).Single()).ReplaceLineEndings("\n");

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private Output Build(string dialect, string? model = null, string apiExtra = "", string extraTargets = "")
    {
        var (output, exit, stderr) = Run(dialect, model, apiExtra, extraTargets);
        Assert.True(exit == 0, $"exit {exit}: {stderr}");
        return output;
    }

    /// <summary>A build that is expected to stop: its exit code and what it said on the error stream.</summary>
    private (int Exit, string Stderr) TryBuild(string model)
    {
        var (output, exit, stderr) = Run("tsql", model, apiExtra: "");
        using (output)
            return (exit, stderr);
    }

    private (Output Output, int Exit, string Stderr) Run(
        string dialect, string? model, string apiExtra, string extraTargets = "")
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-view-read-{dialect}-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        foreach (var d in new[] { "db", "entities", "api" })
            Directory.CreateDirectory(Path.Combine(root, d));

        // The Model target needs the timestamp contract on every model; the specification's examples do not declare it.
        var text = (model ?? SpecFixture()).ReplaceLineEndings("\n")
            .Replace("\n## Customer\n", Timestamps + "## Customer : Timestampable\n")
            .Replace("\n## Product\n", "\n## Product : Timestampable\n")
            .Replace("\n## Order\n", "\n## Order : Timestampable\n")
            .Replace("\n## OrderItem\n", "\n## OrderItem : Timestampable\n");
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), text);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "dialect": "{{dialect}}", "projectPath": "../db", "emitSqlProj": false },
    { "type": "Model", "dialect": "{{dialect}}", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
    { "type": "Api", "projectPath": "../api", "namespace": "X.Server", "permissionKeyTemplate": "{entitySet}.{verb}"{{apiExtra}} }{{extraTargets}}
  ]
}
""");

        using var stdout = new ConsoleOutCapture(this);
        using var stderr = new ConsoleErrorCapture(this);
        var exit = new BuildCommand().Run(mddDir);
        return (new Output(root, stdout.Text), exit, stderr.Text);
    }

    [Fact]
    public void A_keyed_view_gets_a_read_entity_with_a_key_and_the_views_columns_only()
    {
        using var o = Build("tsql");
        var entity = o.Read("entities", "Entity_gen", "ActiveProducts.cs");

        Assert.Contains("public partial class ActiveProducts : IKeyed", entity);
        Assert.Contains("    public Guid Id { get; set; }", entity);
        Assert.Contains("    public string Name { get; set; } = string.Empty;", entity);
        Assert.Contains("    public decimal Price { get; set; }", entity);
        Assert.Contains("    public int Stock { get; set; }", entity);
        // A view has no audit timestamps of its own, and nothing is written through it.
        Assert.DoesNotContain("CreatedAt", entity);
        Assert.DoesNotContain("IyuEntity", entity);
        Assert.False(o.Exists("entities", "Entity_gen", "ActiveProductsExt.cs"));
    }

    [Fact]
    public void The_context_maps_the_entity_to_the_view_the_sql_target_creates()
    {
        using var tsql = Build("tsql");
        var context = tsql.Read("entities", "DbContext_gen", "XDbContext.cs");
        Assert.Contains("public DbSet<ActiveProducts> ActiveProducts => Set<ActiveProducts>();", context);
        Assert.Contains("e.ToView(\"ActiveProducts\");", context);
        Assert.True(tsql.Exists("db", "dbo", "Views_gen", "ActiveProducts.sql"));

        using var pg = Build("postgres", ProductOnly);
        var pgContext = pg.Read("entities", "DbContext_gen", "XDbContext.cs");
        Assert.Contains("e.ToView(\"active_products\");", pgContext);
        Assert.Contains("e.Property(x => x.Stock).HasColumnName(\"stock\");", pgContext);
        Assert.True(pg.Exists("db", "views_gen", "active_products.sql"));
    }

    [Fact]
    public void The_api_target_registers_a_read_only_set_with_a_read_only_controller_and_a_read_permission()
    {
        using var o = Build("tsql");

        var registration = o.Api("ApiRegistration_gen.cs");
        Assert.Contains("options.ODataModel.AddReadOnlySet<X.Entities.ActiveProducts>(\"ActiveProducts\");", registration);
        Assert.DoesNotContain("AddEntityPair<X.Entities.ActiveProducts", registration);

        var controllers = o.Api("Controllers_gen.cs");
        Assert.Contains(
            "public sealed partial class ActiveProductsController\n    : IyuODataReadOnlyController<X.Entities.ActiveProducts>",
            controllers);

        var permissions = o.Api("Permissions_gen.cs");
        Assert.Contains("public const string ActiveProductsRead = \"ActiveProducts.read\";", permissions);
        Assert.DoesNotContain("ActiveProductsWrite", permissions);
        Assert.Contains("[typeof(X.Entities.ActiveProducts)] = (ActiveProductsRead, ActiveProductsRead),", permissions);
    }

    [Fact]
    public void A_view_with_no_key_stays_sql_and_the_build_says_so()
    {
        using var o = Build("tsql");

        Assert.True(o.Exists("db", "dbo", "Views_gen", "CustomerStats.sql"));
        Assert.False(o.Exists("entities", "Entity_gen", "CustomerStats.cs"));
        Assert.DoesNotContain("CustomerStats", o.Api("ApiRegistration_gen.cs"));
        Assert.Contains("뷰 'CustomerStats' 은(는) 읽기 모델로 내지 않는다", o.Stdout);
        Assert.DoesNotContain("뷰 'ActiveProducts' 은(는) 읽기 모델로 내지 않는다", o.Stdout);
    }

    [Fact]
    public void An_internal_view_is_kept_off_the_read_surface_without_comment()
    {
        using var o = Build("tsql", SpecFixture().Replace("## ActiveProducts ::view", "## ActiveProducts ::view @internal"));

        Assert.True(o.Exists("db", "dbo", "Views_gen", "ActiveProducts.sql"));
        Assert.False(o.Exists("entities", "Entity_gen", "ActiveProducts.cs"));
        Assert.DoesNotContain("ActiveProducts", o.Api("ApiRegistration_gen.cs"));
        // Kept off deliberately, so there is nothing to explain.
        Assert.DoesNotContain("뷰 'ActiveProducts' 은(는) 읽기 모델로 내지 않는다", o.Stdout);
    }

    [Fact]
    public void The_api_surface_filter_can_leave_a_view_out_by_name()
    {
        using var o = Build("tsql", apiExtra: """, "excludeEntities": ["ActiveProducts"]""");

        Assert.DoesNotContain("ActiveProducts", o.Api("ApiRegistration_gen.cs"));
        Assert.DoesNotContain("ActiveProducts", o.Api("Controllers_gen.cs"));
        // The entity is the Model target's, which is never a subset.
        Assert.True(o.Exists("entities", "Entity_gen", "ActiveProducts.cs"));
    }
}
