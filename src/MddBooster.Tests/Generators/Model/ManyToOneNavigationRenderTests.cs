using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The forward navigation a read type gains for an ordinary reference key — what lets a client
/// follow the key with <c>$expand</c>. Which keys qualify and what they are called is decided once
/// by <see cref="ManyToOneNavigations"/>; this checks that decision reaches the read class and the
/// context, on both dialects, and that nothing else moves.
/// </summary>
public class ManyToOneNavigationRenderTests
{
    private const string Fixture = "many-to-one-reference.m3l.md";

    private static IReadOnlyList<ResolvedModel> Load()
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", Fixture));
        return new InterfaceResolver(ast).ResolveAll().ToList();
    }

    private static (IReadOnlyList<OneToOneRelationships.Relationship> OneToOne,
        IReadOnlyList<ManyToOneNavigations.Navigation> ManyToOne) Describe(IReadOnlyList<ResolvedModel> models)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        return (oneToOne, ManyToOneNavigations.Describe(models, oneToOne));
    }

    private static EntityPairRenderer.RenderedPair Render(string modelName, bool postgres = false)
    {
        var models = Load();
        var (oneToOne, manyToOne) = Describe(models);
        return EntityPairRenderer.Render(
            models.Single(m => m.Name == modelName), "Probe.Entities",
            oneToOne: oneToOne, allModels: models, postgres: postgres, manyToOne: manyToOne);
    }

    [Fact]
    public void A_reference_key_gives_the_read_type_a_navigation_to_the_targets_read_type()
    {
        var asset = Render("Asset").Read;

        // Named from the key, typed as the target's *read* type, optional because the key is.
        Assert.Contains("public AssetLifecycleStateExt? LifecycleState { get; set; }", asset, StringComparison.Ordinal);
        // A required key cannot point at a missing row.
        Assert.Contains("public OrganizationExt Owner { get; set; } = null!;", asset, StringComparison.Ordinal);
        // A self-reference needs no reverse name, so unlike the one-to-one case it is kept.
        Assert.Contains("public AssetExt? Parent { get; set; }", asset, StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_off_the_read_surface_and_names_already_taken_get_none()
    {
        var asset = Render("Asset").Read;

        // @internal: the key is not on the read type, so there is nothing to hang a navigation on.
        Assert.DoesNotContain(" Operator ", asset, StringComparison.Ordinal);
        // `vendor` is a declared lookup; the derived navigation would take the same name and loses.
        // (optional: the lookup reads through a nullable key)
        Assert.Contains("public string? Vendor { get; set; }", asset, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationExt? Vendor", asset, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_to_one_key_keeps_its_pair_and_the_other_key_to_the_same_target_is_many_to_one()
    {
        var profile = Render("AssetMaintenanceProfile").Read;

        Assert.Contains("public AssetExt Asset { get; set; } = null!;", profile, StringComparison.Ordinal);
        Assert.Contains("public AssetExt? ReplacedAsset { get; set; }", profile, StringComparison.Ordinal);

        // The target gains no collection — only the one-to-one's reverse half.
        var asset = Render("Asset").Read;
        Assert.Contains("public AssetMaintenanceProfileExt? MaintenanceProfile { get; set; }", asset, StringComparison.Ordinal);
        Assert.DoesNotContain("ICollection", asset, StringComparison.Ordinal);
    }

    /// <summary>The write entity's navigations are its own, for insert ordering; this adds none.</summary>
    [Fact]
    public void The_write_entity_is_unchanged()
    {
        var models = Load();
        var (oneToOne, manyToOne) = Describe(models);
        var asset = models.Single(m => m.Name == "Asset");

        var with = EntityPairRenderer.Render(asset, "Probe.Entities", oneToOne: oneToOne, allModels: models, manyToOne: manyToOne);
        var without = EntityPairRenderer.Render(asset, "Probe.Entities", oneToOne: oneToOne, allModels: models);

        Assert.Equal(without.Write, with.Write);
        Assert.Equal(without.Interface, with.Interface);
        Assert.NotEqual(without.Read, with.Read);
    }

    [Fact]
    public void The_read_entity_file_is_the_same_bytes_on_both_dialects()
    {
        // Column naming differs per dialect in the context, never in the entity classes.
        Assert.Equal(Render("Asset").Read, Render("Asset", postgres: true).Read);
    }

    /// <summary>
    /// Each navigation is configured as a many-to-one with no inverse. Left to convention, the profile's
    /// two navigations to <c>AssetExt</c> against the asset's one navigation back is a pairing
    /// convention refuses to choose — the context would not build.
    /// </summary>
    [Fact]
    public void The_context_configures_each_navigation_as_many_to_one_on_both_dialects()
    {
        var models = Load();
        var (oneToOne, manyToOne) = Describe(models);

        foreach (var postgres in new[] { false, true })
        {
            var mapping = ModelMappingRenderer.Render(
                models.ToList(), "AppDbContext", "Probe.Entities", postgresNaming: postgres,
                oneToOne: oneToOne, manyToOne: manyToOne);
            var readTypes = string.Concat(mapping.Configurations
                .Where(c => c.EntityType.EndsWith("Ext", StringComparison.Ordinal))
                .Select(c => c.Source));

            Assert.Contains(
                "builder.HasOne(e => e.LifecycleState).WithMany().HasForeignKey(e => e.LifecycleStateId).OnDelete(DeleteBehavior.NoAction);",
                mapping["AssetExt"], StringComparison.Ordinal);
            Assert.Contains(
                "builder.HasOne(e => e.ReplacedAsset).WithMany().HasForeignKey(e => e.ReplacedAssetId).OnDelete(DeleteBehavior.NoAction);",
                mapping["AssetMaintenanceProfileExt"], StringComparison.Ordinal);
            Assert.DoesNotContain("builder.HasOne(e => e.Vendor)", readTypes, StringComparison.Ordinal);
            Assert.DoesNotContain("builder.HasOne(e => e.Operator)", readTypes, StringComparison.Ordinal);
        }
    }
}
