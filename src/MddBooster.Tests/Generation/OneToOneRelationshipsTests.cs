using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Generation;

/// <summary>
/// The naming rules for a one-to-one, tested where they live rather than through a renderer — a
/// wrong name and a missing emission look the same from the outside, and only one of them is a
/// naming bug.
/// </summary>
public class OneToOneRelationshipsTests
{
    private static IReadOnlyList<ResolvedModel> Load(string fixture)
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", fixture));
        return new InterfaceResolver(ast).ResolveAll().ToList();
    }

    [Fact]
    public void A_unique_reference_is_described_as_a_one_to_one()
    {
        var found = OneToOneRelationships.Describe(Load("one-to-one-unique.m3l.md"));

        Assert.Equal(2, found.Count);

        // The reverse name drops the target's own name from the front: the navigation hangs off
        // Asset, so repeating "Asset" in it says nothing.
        var profile = Assert.Single(found, r => r.DependentModel == "AssetMaintenanceProfile");
        Assert.Equal("Asset", profile.TargetModel);
        Assert.Equal("asset_id", profile.ForeignKeyField);
        Assert.Equal("Asset", profile.ForwardName);
        Assert.Equal("MaintenanceProfile", profile.ReverseName);
        Assert.False(profile.IsOptional);
        Assert.Null(profile.ReverseNameConflict);

        // A name that does not start with the target's has nothing to strip, and shortening it
        // further would be inventing one. The nullable key makes the relationship optional.
        var contractor = Assert.Single(found, r => r.DependentModel == "Contractor");
        Assert.Equal("Organization", contractor.TargetModel);
        Assert.Equal("Organization", contractor.ForwardName);
        Assert.Equal("Contractor", contractor.ReverseName);
        Assert.True(contractor.IsOptional);
    }

    /// <summary>
    /// Two references from one model to the same target derive the same reverse name. Both are
    /// returned marked, each naming the other's key, so a diagnostic can say what collided instead
    /// of one navigation silently winning.
    /// </summary>
    [Fact]
    public void Two_references_to_one_target_are_marked_as_a_reverse_name_conflict()
    {
        var found = OneToOneRelationships.Describe(Load("one-to-one-reverse-clash.m3l.md"));

        Assert.Equal(2, found.Count);
        // AssetProfile starts with Asset, so the rule strips it — both sides land on Profile, and
        // that is precisely why they collide.
        Assert.All(found, r => Assert.Equal("Profile", r.ReverseName));
        Assert.All(found, r => Assert.NotNull(r.ReverseNameConflict));

        var primary = Assert.Single(found, r => r.ForeignKeyField == "primary_asset_id");
        var backup = Assert.Single(found, r => r.ForeignKeyField == "backup_asset_id");
        Assert.Equal("backup_asset_id", primary.ReverseNameConflict);
        Assert.Equal("primary_asset_id", backup.ReverseNameConflict);

        // The forward names are distinct — the collision is only on the reverse side, which is what
        // makes "rename one field" a real fix rather than a coin toss.
        Assert.Equal("PrimaryAsset", primary.ForwardName);
        Assert.Equal("BackupAsset", backup.ForwardName);
    }

    /// <summary>
    /// A reference without <c>@unique</c> is a many-to-one and has no place here. Without this, the
    /// test above would pass for an implementation that described every reference.
    /// </summary>
    [Fact]
    public void A_reference_without_unique_is_not_a_one_to_one()
    {
        var found = OneToOneRelationships.Describe(Load("order-with-ref.m3l.md"));

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("owner_id", "Owner")]
    [InlineData("channel_partner_id", "ChannelPartner")]
    [InlineData("owner", "Owner")]   // no _id suffix — the field name is the name
    public void The_forward_name_comes_from_the_foreign_key_field(string field, string expected)
        => Assert.Equal(expected, OneToOneRelationships.ForwardNameFrom(field));

    [Theory]
    [InlineData("AssetMaintenanceProfile", "Asset", "MaintenanceProfile")]
    [InlineData("Contractor", "Organization", "Contractor")]
    [InlineData("AssetAsset", "Asset", "Asset")]
    // Stripping leaves nothing, and a navigation cannot be nameless.
    [InlineData("Asset", "Asset", "Asset")]
    public void The_reverse_name_strips_the_targets_name_only_when_it_is_a_prefix(
        string dependent, string target, string expected)
        => Assert.Equal(expected, OneToOneRelationships.ReverseNameFor(dependent, target));

    /// <summary>
    /// The build line says what was derived, so a declared one-to-one is never invisible in a log —
    /// and a conflict says so on its own line rather than being summarised away.
    /// </summary>
    [Fact]
    public void The_build_lines_name_both_navigations_and_flag_a_conflict()
    {
        var ok = OneToOneRelationships.Describe(
            OneToOneRelationships.Describe(Load("one-to-one-unique.m3l.md")));
        Assert.Contains(ok, l => l.Contains("AssetMaintenanceProfile.MaintenanceProfile", StringComparison.Ordinal)
                                 || l.Contains("Asset.MaintenanceProfile", StringComparison.Ordinal));
        Assert.Contains(ok, l => l.Contains("(선택적)", StringComparison.Ordinal));

        var clashing = OneToOneRelationships.Describe(
            OneToOneRelationships.Describe(Load("one-to-one-reverse-clash.m3l.md")));
        Assert.All(clashing, l => Assert.Contains("충돌", l, StringComparison.Ordinal));
    }
}
