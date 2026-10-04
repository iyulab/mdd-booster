using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Generation;

/// <summary>
/// Which reference keys give a read type a many-to-one navigation, what it is called, and what
/// happens when the name is already taken (<c>MDD027</c>).
/// </summary>
public class ManyToOneNavigationsTests
{
    private static IReadOnlyList<ResolvedModel> Load(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-many-to-one-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll().ToList();
        }
        finally { File.Delete(tmp); }
    }

    private static IReadOnlyList<ManyToOneNavigations.Navigation> Describe(IReadOnlyList<ResolvedModel> models) =>
        ManyToOneNavigations.Describe(models, OneToOneRelationships.Describe(models));

    private static IReadOnlyList<SemanticDiagnostic> Analyze(IReadOnlyList<ResolvedModel> models) =>
        new SemanticAnalyzer(models, []).Analyze();

    private const string Base = """
# Namespace: x

## Organization
- id: identifier @pk @generated
- name: string(200)

""";

    [Fact]
    public void Ordinary_keys_qualify_and_one_to_one_keys_do_not()
    {
        var models = Load(Base + """
## Asset
- id: identifier @pk @generated
- owner_id: identifier @reference(Organization)
- site_id: identifier? @reference(Organization) @unique
- maker: identifier? @reference(Organization)
""");

        var nav = Assert.Single(Describe(models));
        Assert.Equal(("Asset", "Organization", "owner_id", "Owner", false, (string?)null),
            (nav.DependentModel, nav.TargetModel, nav.ForeignKeyField, nav.Name, nav.IsOptional, nav.CollidesWith));
        // `site_id` is a one-to-one (both ends get a navigation elsewhere); `maker` does not follow the
        // `_id` convention the write side's navigation also requires.
    }

    [Fact]
    public void A_navigation_name_the_read_type_already_uses_is_reported_and_left_out()
    {
        var models = Load(Base + """
## Asset
- id: identifier @pk @generated
- vendor_id: identifier? @reference(Organization)
- vendor: string @lookup(vendor_id.name)
""");

        var nav = Assert.Single(Describe(models));
        Assert.Equal("vendor", nav.CollidesWith);

        var warning = Assert.Single(Analyze(models), d => d.Code == "MDD027");
        Assert.Equal(SemanticSeverity.Warning, warning.Severity);
        Assert.Contains("vendor_id", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'Vendor'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collision_with_a_one_to_one_navigation_is_reported_too()
    {
        // The one-to-one gives Asset the reverse navigation `Profile`; a key spelled `profile_id`
        // would take the same name.
        var models = Load(Base + """
## Asset
- id: identifier @pk @generated
- profile_id: identifier? @reference(Organization)

## AssetProfile
- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @unique
""");

        var nav = Assert.Single(Describe(models), n => n.ForeignKeyField == "profile_id");
        Assert.NotNull(nav.CollidesWith);
        Assert.Contains("1:1", nav.CollidesWith, StringComparison.Ordinal);
        Assert.Single(Analyze(models), d => d.Code == "MDD027");
    }

    [Fact]
    public void A_model_without_collisions_is_clean()
    {
        var models = Load(Base + """
## Asset
- id: identifier @pk @generated
- owner_id: identifier @reference(Organization)
- parent_id: identifier? @reference(Asset)
""");

        Assert.All(Describe(models), n => Assert.Null(n.CollidesWith));
        Assert.DoesNotContain(Analyze(models), d => d.Code == "MDD027");
    }
}
