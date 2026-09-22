using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The navigation a read type gains for a declared one-to-one. Names are settled and tested in
/// <see cref="Generation.OneToOneRelationshipsTests"/>; what is checked here is that the pair
/// actually reaches the generated read class, on both sides, and that the write class is untouched.
/// </summary>
public class OneToOneNavigationRenderTests
{
    private static (IReadOnlyList<ResolvedModel> Models, IReadOnlyList<OneToOneRelationships.Relationship> Rel)
        Load(string fixture)
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        return (models, OneToOneRelationships.Describe(models));
    }

    private static EntityPairRenderer.RenderedPair Render(string fixture, string modelName)
    {
        var (models, rel) = Load(fixture);
        var model = models.Single(m => m.Name == modelName);
        return EntityPairRenderer.Render(model, "Probe.Entities", oneToOne: rel);
    }

    [Fact]
    public void The_dependent_read_type_points_at_the_targets_read_type()
    {
        var pair = Render("one-to-one-unique.m3l.md", "AssetMaintenanceProfile");

        // The target's *read* type, not the write one — a read type that reached across to a write
        // type would drag the write model into every projection.
        Assert.Contains("public AssetExt Asset { get; set; } = null!;", pair.Read, StringComparison.Ordinal);
        // A required key cannot point at a missing row, so the navigation is not nullable.
        Assert.DoesNotContain("public AssetExt? Asset", pair.Read, StringComparison.Ordinal);
    }

    [Fact]
    public void The_target_read_type_points_back_and_that_side_is_always_optional()
    {
        var pair = Render("one-to-one-unique.m3l.md", "Asset");

        // A target row exists whether or not anything points at it.
        Assert.Contains(
            "public AssetMaintenanceProfileExt? MaintenanceProfile { get; set; }", pair.Read, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_key_makes_the_forward_side_optional_too()
    {
        var contractor = Render("one-to-one-unique.m3l.md", "Contractor");
        Assert.Contains("public OrganizationExt? Organization { get; set; }", contractor.Read, StringComparison.Ordinal);

        var organization = Render("one-to-one-unique.m3l.md", "Organization");
        Assert.Contains("public ContractorExt? Contractor { get; set; }", organization.Read, StringComparison.Ordinal);
    }

    /// <summary>
    /// The write entity is not part of this. Its navigation exists to let EF infer insert order and
    /// points at write types; adding a read-side one there would change what a save sees.
    /// </summary>
    [Fact]
    public void The_write_entity_gains_nothing()
    {
        var profile = Render("one-to-one-unique.m3l.md", "AssetMaintenanceProfile");
        var asset = Render("one-to-one-unique.m3l.md", "Asset");

        Assert.DoesNotContain("Ext", profile.Write.Replace("AssetMaintenanceProfile", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.DoesNotContain("MaintenanceProfile", asset.Write, StringComparison.Ordinal);
    }

    /// <summary>
    /// A relationship whose reverse name collides is skipped. The build already fails on it, so this
    /// is about never emitting the duplicate property that would result if it ever ran.
    /// </summary>
    [Fact]
    public void A_colliding_relationship_emits_no_navigation()
    {
        var asset = Render("one-to-one-reverse-clash.m3l.md", "Asset");
        var profile = Render("one-to-one-reverse-clash.m3l.md", "AssetProfile");

        Assert.DoesNotContain("AssetProfileExt?", asset.Read, StringComparison.Ordinal);
        Assert.DoesNotContain("public AssetExt", profile.Read, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without the relationship set the output is what it was before the parameter existed — the
    /// negative control for every assertion above.
    /// </summary>
    [Fact]
    public void Omitting_the_relationship_set_emits_no_navigation()
    {
        var (models, _) = Load("one-to-one-unique.m3l.md");
        var model = models.Single(m => m.Name == "AssetMaintenanceProfile");

        var pair = EntityPairRenderer.Render(model, "Probe.Entities");

        Assert.DoesNotContain("AssetExt", pair.Read, StringComparison.Ordinal);
    }
}
