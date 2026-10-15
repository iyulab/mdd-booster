using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// A <c>@unique</c> reference is a one-to-one on the write entity as well as on the read type and in
/// the database. Left to convention, a reference navigation without an inverse is many-to-one, so the
/// EF model called the key repeatable while the unique constraint said it was not — and a schema built
/// from the EF model (<c>EnsureCreated</c>, migrations) read the relationship the wrong way.
/// </summary>
public class WriteSideOneToOneTests
{
    private const string Source = """
        # Namespace: test.writeOneToOne

        ## Asset
        - id: identifier @pk

        ## Node
        - id: identifier @pk
        - successor_id: identifier? @reference(Node) @unique

        ## Warranty
        - id: identifier @pk
        - asset_id: identifier @reference(Asset) @unique
        - kept_asset_id: identifier? @reference(Asset)? @unique
        - plain_asset_id: identifier @reference(Asset)
        """;

    private static string RenderContext(bool postgresNaming = false)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-write11-{Guid.NewGuid():N}.m3l.md");
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
    public void A_unique_reference_is_configured_as_one_to_one_on_the_write_entity(bool postgresNaming)
    {
        var code = RenderContext(postgresNaming);

        Assert.Contains("modelBuilder.Entity<Warranty>().HasOne(e => e.Asset).WithOne().HasForeignKey<Warranty>(e => e.AssetId);", code);
    }

    [Fact]
    public void A_declared_delete_action_rides_on_the_one_to_one()
    {
        var code = RenderContext();

        Assert.Contains("modelBuilder.Entity<Warranty>().HasOne(e => e.KeptAsset).WithOne().HasForeignKey<Warranty>(e => e.KeptAssetId).OnDelete(DeleteBehavior.SetNull);", code);
    }

    [Fact]
    public void A_self_referencing_unique_key_is_one_to_one_too()
    {
        var code = RenderContext();

        Assert.Contains("modelBuilder.Entity<Node>().HasOne(e => e.Successor).WithOne().HasForeignKey<Node>(e => e.SuccessorId);", code);
    }

    [Fact]
    public void A_reference_that_is_not_unique_stays_many_to_one()
    {
        var code = RenderContext();

        Assert.DoesNotContain("e => e.PlainAsset).WithOne()", code);
    }
}
