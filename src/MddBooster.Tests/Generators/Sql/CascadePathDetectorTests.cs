using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// SQL Server refuses a set of delete actions that is not a tree (error 1785): one delete may not
/// reach a table twice, nor come back to its own table. A CASCADE continues from the rows it
/// deletes; a SET NULL ends at the rows it updates. PostgreSQL has no such limit. Every shape here
/// was applied to a live SQL Server 2022 while the detector was written — the refused ones failed
/// with 1785, the accepted ones created every constraint.
/// </summary>
public class CascadePathDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mdd-cascade-{Guid.NewGuid():N}");

    public CascadePathDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private (IReadOnlyList<ResolvedModel> Models, M3lAst Ast) Load(string body)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(path, "# Namespace: test.cascade\n\n" + body);
        var ast = new M3lLoader().LoadFile(path);
        return (new InterfaceResolver(ast).ResolveAll(), ast);
    }

    private CascadePathDetector.CascadeConflict? Detect(string body) =>
        CascadePathDetector.Detect(Load(body).Models);

    [Fact]
    public void A_set_null_self_reference_is_a_cycle()
    {
        var conflict = Detect("""
            ## Category
            - id: identifier @pk
            - parent_id: identifier? @reference(Category)?
            """);

        Assert.NotNull(conflict);
        Assert.Null(conflict!.Second);
        var edge = Assert.Single(conflict.First);
        Assert.Equal(("Category", "Category", "parent_id", ReferentialAction.SetNull),
            (edge.Principal, edge.Dependent, edge.Field, edge.Action));
    }

    [Fact]
    public void A_set_null_key_back_to_where_a_cascade_started_is_a_cycle()
    {
        // Deleting an Asset deletes its AssetNote (the aspect key cascades); that delete then
        // clears Asset.pinned_note_id — back on the table the delete started from.
        var conflict = Detect("""
            ## Asset
            - id: identifier @pk
            - pinned_note_id: identifier? @reference(AssetNote)?

            ## AssetNote ::aspect(Asset)
            - text: string(200)?
            """);

        Assert.NotNull(conflict);
        Assert.Null(conflict!.Second);
        Assert.Equal([ReferentialAction.Cascade, ReferentialAction.SetNull], conflict.First.Select(e => e.Action));
        Assert.Contains("comes back to Asset", conflict.Describe());
    }

    [Fact]
    public void Set_null_keys_that_form_a_loop_are_accepted()
    {
        // SET NULL updates a row and starts nothing further, so Team -> Member -> Team is two
        // separate one-step trees. SQL Server creates all three constraints.
        var conflict = Detect("""
            ## Root
            - id: identifier @pk

            ## Team
            - id: identifier @pk
            - root_id: identifier? @reference(Root)?
            - lead_id: identifier? @reference(Member)?

            ## Member
            - id: identifier @pk
            - team_id: identifier? @reference(Team)?
            """);

        Assert.Null(conflict);
    }

    [Fact]
    public void Two_set_null_keys_to_one_table_reach_it_twice()
    {
        var conflict = Detect("""
            ## Customer
            - id: identifier @pk

            ## Order
            - id: identifier @pk
            - buyer_id: identifier? @reference(Customer)?
            - referrer_id: identifier? @reference(Customer)?
            """);

        Assert.NotNull(conflict);
        Assert.NotNull(conflict!.Second);
        Assert.Equal(["buyer_id", "referrer_id"],
            new[] { conflict.First, conflict.Second! }.Select(p => p.Single().Field).Order());
        Assert.Contains("reaches Order twice", conflict.Describe());
    }

    [Fact]
    public void A_cascade_and_a_set_null_meeting_on_one_table_reach_it_twice()
    {
        // Deleting a Store deletes its StoreDetail (cascade), which clears Shipment.detail_id;
        // the same delete also clears Shipment.store_id directly.
        var conflict = Detect("""
            ## Store
            - id: identifier @pk

            ## StoreDetail ::aspect(Store)
            - note: string(200)?

            ## Shipment
            - id: identifier @pk
            - store_id: identifier? @reference(Store)?
            - detail_id: identifier? @reference(StoreDetail)?
            """);

        Assert.NotNull(conflict);
        Assert.NotNull(conflict!.Second);
        Assert.Equal("Store", conflict.First[0].Principal);
        Assert.Equal("Shipment", conflict.First[^1].Dependent);
        Assert.Equal("Shipment", conflict.Second![^1].Dependent);
        Assert.Contains(new[] { conflict.First, conflict.Second }, p => p.Count == 2);
    }

    [Fact]
    public void Set_null_chains_meeting_on_one_table_are_accepted()
    {
        // Region -> Store and Region -> Warehouse each end at their SET NULL; Shipment is reached
        // only from Store and from Warehouse, once each.
        var conflict = Detect("""
            ## Region
            - id: identifier @pk

            ## Store
            - id: identifier @pk
            - region_id: identifier? @reference(Region)?

            ## Warehouse
            - id: identifier @pk
            - region_id: identifier? @reference(Region)?

            ## Shipment
            - id: identifier @pk
            - store_id: identifier? @reference(Store)?
            - warehouse_id: identifier? @reference(Warehouse)?
            """);

        Assert.Null(conflict);
    }

    [Fact]
    public void An_aspect_key_cascades_and_counts_as_a_path()
    {
        var conflict = Detect("""
            ## Asset
            - id: identifier @pk

            ## AssetNote ::aspect(Asset)
            - replaced_by_id: identifier? @reference(Asset)?
            """);

        Assert.NotNull(conflict);
        Assert.Contains(new[] { conflict!.First, conflict.Second! }.SelectMany(p => p),
            e => e.Action == ReferentialAction.Cascade);
    }

    [Fact]
    public void Keys_that_act_on_no_other_row_never_conflict()
    {
        // One SET NULL beside NO ACTION / RESTRICT / bare keys to the same table, and a self
        // reference that declares NO ACTION: only one action path from any table.
        var conflict = Detect("""
            ## Customer
            - id: identifier @pk

            ## Category
            - id: identifier @pk
            - parent_id: identifier? @reference(Category)!

            ## Order
            - id: identifier @pk
            - customer_id: identifier @reference(Customer)!
            - referrer_id: identifier? @reference(Customer)?
            - guard_id: identifier @reference(Customer)!!
            - plain_id: identifier @reference(Customer)
            - category_id: identifier? @reference(Category)?

            ## OrderLine
            - id: identifier @pk
            - order_id: identifier? @reference(Order)?
            """);

        Assert.Null(conflict);
    }

    private const string SelfReference = """
        ## Category
        - id: identifier @pk
        - parent_id: identifier? @reference(Category)?
        """;

    [Fact]
    public void The_tsql_generator_stops_before_replacing_the_last_output()
    {
        var (models, ast) = Load(SelfReference);
        var previous = Path.Combine(_root, "dbo", "Tables_gen", "Category.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "-- last good output");

        var generator = new SqlGenerator(new SqlGeneratorOptions { ProjectPath = ".", EmitSqlProj = false, EmitRefreshScript = false });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            generator.Generate(new GeneratorContext { Models = models, Enums = ast.Enums, WorkingDirectory = _root }));

        Assert.Contains("error 1785", ex.Message);
        Assert.Contains("Category.parent_id (SET NULL)", ex.Message);
        Assert.Equal("-- last good output", File.ReadAllText(previous));
    }

    [Fact]
    public void The_postgres_generator_writes_the_same_model()
    {
        var (models, ast) = Load(SelfReference);

        new PostgresSqlGenerator(new PostgresSqlGeneratorOptions { ProjectPath = "." })
            .Generate(new GeneratorContext { Models = models, Enums = ast.Enums, WorkingDirectory = _root });

        var sql = string.Concat(Directory.GetFiles(_root, "*.sql", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("ON DELETE SET NULL", sql);
    }
}
