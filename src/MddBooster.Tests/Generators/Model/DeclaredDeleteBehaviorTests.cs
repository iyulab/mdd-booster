using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The delete action a <c>@reference</c> symbol declares reaches EF Core as well as the database.
/// Without it EF's convention runs against tracked rows — a required key cascades — and deletes
/// children the database was told to refuse.
/// </summary>
public class DeclaredDeleteBehaviorTests
{
    private const string Source = """
        # Namespace: test.onDelete

        ## Customer
        - id: identifier @pk

        ## Invoice
        - id: identifier @pk
        - plain_id: identifier @reference(Customer)
        - kept_id: identifier @reference(Customer)!
        - cleared_id: identifier? @reference(Customer)?
        - guarded_id: identifier @reference(Customer)!!
        """;

    private static string RenderContext(bool postgresNaming)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-efdelete-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, Source);
        try
        {
            var models = new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll();
            return DbContextRenderer.Render(models, "AppDbContext", "Test.Entities", postgresNaming: postgresNaming);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_declared_action_is_configured_on_the_write_navigation(bool postgresNaming)
    {
        var code = RenderContext(postgresNaming);

        Assert.Contains("modelBuilder.Entity<Invoice>().HasOne(e => e.Kept).WithMany().HasForeignKey(e => e.KeptId).OnDelete(DeleteBehavior.NoAction);", code);
        Assert.Contains("modelBuilder.Entity<Invoice>().HasOne(e => e.Cleared).WithMany().HasForeignKey(e => e.ClearedId).OnDelete(DeleteBehavior.SetNull);", code);
        Assert.Contains("modelBuilder.Entity<Invoice>().HasOne(e => e.Guarded).WithMany().HasForeignKey(e => e.GuardedId).OnDelete(DeleteBehavior.Restrict);", code);
    }

    /// <summary>A reference without a symbol keeps EF's convention, as it keeps the database default.</summary>
    [Fact]
    public void A_bare_reference_is_left_to_the_convention()
    {
        var code = RenderContext(postgresNaming: false);

        Assert.DoesNotContain("e => e.Plain)", code);
    }
}
