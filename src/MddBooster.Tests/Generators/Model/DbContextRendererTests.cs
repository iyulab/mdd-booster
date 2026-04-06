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
        Assert.Contains("public partial class TestDbContext : global::Iyu.Data.IyuDbContext", output);
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
}
