using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class DbContextRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Render_produces_partial_context_with_DbSet_pairs()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var output = DbContextRenderer.Render(models, "TestDbContext", "Test.Ns");

        Assert.Contains("namespace Test.Ns;", output);
        Assert.Contains("public partial class TestDbContext : IyuDbContext", output);
        Assert.Contains("public TestDbContext(DbContextOptions<TestDbContext> options)", output);
        Assert.Contains("public DbSet<Customer> Customers => Set<Customer>();", output);
        Assert.Contains("public DbSet<CustomerExt> CustomersExt => Set<CustomerExt>();", output);
        Assert.Contains("public DbSet<Order> Orders => Set<Order>();", output);
        Assert.Contains("public DbSet<OrderExt> OrdersExt => Set<OrderExt>();", output);
    }

    [Fact]
    public void Render_orders_entities_deterministically()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var output = DbContextRenderer.Render(models, "Ctx", "Ns");

        // Customer appears before Order lexicographically (Ordinal sort).
        var customerIdx = output.IndexOf("DbSet<Customer>", StringComparison.Ordinal);
        var orderIdx = output.IndexOf("DbSet<Order>", StringComparison.Ordinal);
        Assert.True(customerIdx > 0);
        Assert.True(orderIdx > customerIdx);
    }

    [Fact]
    public void Render_rejects_empty_context_name()
    {
        Assert.Throws<ArgumentException>(() =>
            DbContextRenderer.Render([], "", "ns"));
    }

    // ---------------------------------------------------------------- @unique/@index → HasIndex()

    private static ResolvedModel LoadIndexSample()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("field-constraints.m3l.md"));
        return new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "IndexSample");
    }

    /// <summary>
    /// SQL Server naming mode: constraint/index names mirror TableRenderer's own
    /// <c>UK_{Model}_{Column}</c>/<c>IX_{Model}_{Column}</c> exactly, so a unique-violation error
    /// naming the SQL object also names the same object in the EF model.
    /// </summary>
    [Fact]
    public void Unique_and_index_are_emitted_as_HasIndex_with_sql_server_names()
    {
        var output = DbContextRenderer.Render([LoadIndexSample()], "TestDbContext", "Test.Ns");

        Assert.Contains(
            "modelBuilder.Entity<IndexSample>().HasIndex(x => x.Email).IsUnique().HasDatabaseName(\"UK_IndexSample_Email\");",
            output);
        Assert.Contains(
            "modelBuilder.Entity<IndexSample>().HasIndex(x => x.Tag).HasDatabaseName(\"IX_IndexSample_Tag\");",
            output);
    }

    /// <summary>
    /// Nullable does not branch the emitted line — the SQL Server provider's own
    /// <c>SqlServerIndexConvention</c> adds the <c>WHERE ... IS NOT NULL</c> filter automatically
    /// for any nullable column in a unique index, so <c>optional_code</c> (nullable) and
    /// <c>email</c> (non-nullable, asserted above) render identically here.
    /// </summary>
    [Fact]
    public void Nullable_unique_fields_render_the_same_HasIndex_line_as_non_nullable()
    {
        var output = DbContextRenderer.Render([LoadIndexSample()], "TestDbContext", "Test.Ns");

        Assert.Contains(
            "modelBuilder.Entity<IndexSample>().HasIndex(x => x.OptionalCode).IsUnique().HasDatabaseName(\"UK_IndexSample_OptionalCode\");",
            output);
    }

    [Fact]
    public void Unique_wins_over_index_when_a_field_declares_both()
    {
        var output = DbContextRenderer.Render([LoadIndexSample()], "TestDbContext", "Test.Ns");

        Assert.Contains(
            "modelBuilder.Entity<IndexSample>().HasIndex(x => x.SerialNo).IsUnique().HasDatabaseName(\"UK_IndexSample_SerialNo\");",
            output);
        Assert.DoesNotContain("x.SerialNo).HasDatabaseName(\"IX_", output);
    }

    [Fact]
    public void Pk_field_never_gets_a_redundant_HasIndex_even_when_attributed_index()
    {
        var output = DbContextRenderer.Render([LoadIndexSample()], "TestDbContext", "Test.Ns");

        Assert.DoesNotContain("x.Id).HasDatabaseName(", output);
    }

    [Fact]
    public void A_field_with_neither_attribute_gets_no_HasIndex_call()
    {
        var output = DbContextRenderer.Render([LoadIndexSample()], "TestDbContext", "Test.Ns");

        Assert.DoesNotContain("PlainRef", output);
    }

    /// <summary>
    /// Postgres naming mode: same declarations, but the fluent call sits inside the entity's
    /// <c>e =></c> config block (alongside <see cref="AppendPostgresColumns"/>'s column mapping)
    /// and the constraint/index names mirror PgTableRenderer's <c>uq_{table}_{field}</c>/
    /// <c>ix_{table}_{field}</c> — snake_case, matching the real PG object name.
    /// </summary>
    [Fact]
    public void Unique_and_index_are_emitted_inside_the_postgres_entity_block_with_pg_names()
    {
        var output = DbContextRenderer.Render(
            [LoadIndexSample()], "TestDbContext", "Test.Ns", postgresNaming: true);

        Assert.Contains(
            "e.HasIndex(x => x.Email).IsUnique().HasDatabaseName(\"uq_index_sample_email\");",
            output);
        Assert.Contains(
            "e.HasIndex(x => x.Tag).HasDatabaseName(\"ix_index_sample_tag\");",
            output);

        // Only the write-entity block carries indexes — the Ext block maps to a view.
        var extBlockStart = output.IndexOf("modelBuilder.Entity<IndexSampleExt>", StringComparison.Ordinal);
        Assert.True(extBlockStart > 0);
        Assert.DoesNotContain("HasIndex", output[extBlockStart..]);
    }
}
