using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class DbContextViewMappingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void DbContext_emits_ToView_for_models_with_derived_fields()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = DbContextRenderer.Render(models, "TestDbContext", "Test.Entities");

        // Order has rollup+computed → _ext view
        Assert.Contains("modelBuilder.Entity<OrderExt>().ToTable((string?)null).ToView(\"OrderExtView\")", src);
        // Customer/OrderItem have no derived fields → mapped to base table as view
        Assert.Contains("CustomerExt>().ToTable((string?)null).ToView(\"Customer\")", src);
        Assert.Contains("OrderItemExt>().ToTable((string?)null).ToView(\"OrderItem\")", src);
    }

    [Fact]
    public void DbContext_emits_OnModelCreating_even_when_no_derived_models_present()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = DbContextRenderer.Render(models, "T", "Test");

        // Table-only Ext models map to base table via ToView to avoid shared-table errors
        Assert.Contains("OnModelCreating", src);
        Assert.Contains("BankAccountExt>().ToTable((string?)null).ToView(\"BankAccount\")", src);
    }
}
