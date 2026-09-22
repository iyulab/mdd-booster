using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// <c>::aspect</c> 의 관계가 EF 쪽에도 서는가 — 테이블만 맞고 EF 가 관계를 모르면 소비자는
/// <c>$expand</c> 로 도달할 수 없다.
/// </summary>
/// <remarks>
/// 🔴 EF 의 관례는 PK 가 곧 FK 인 1:1 을 <b>추론하지 못한다</b> — 알아볼
/// <c>&lt;Principal&gt;Id</c> 프로퍼티가 없기 때문이다. 보통 1:1 은 관례가 찾아내므로 이
/// 생성기가 구성 코드를 내지 않기로 했는데(그 결정의 근거가 「관례가 이미 추론한다」였다),
/// aspect 는 <b>그 근거가 성립하지 않는 자리</b>다. 두 처분이 갈리는 이유가 이것이고, 아래
/// negative control 이 그 갈림을 단언으로 고정한다.
/// </remarks>
public class AspectRelationshipTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static IReadOnlyList<ResolvedModel> Load(string fixture) =>
        new InterfaceResolver(new M3lLoader().LoadFile(FixturePath(fixture))).ResolveAll().ToList();

    [Fact]
    public void An_aspect_is_described_as_a_one_to_one_even_though_it_declares_no_unique()
    {
        var models = Load("aspect-shared-key.m3l.md");

        var r = Assert.Single(OneToOneRelationships.Describe(models));

        Assert.Equal("AssetMaintenanceProfile", r.DependentModel);
        Assert.Equal("Asset", r.TargetModel);
        Assert.Equal("asset_id", r.ForeignKeyField);
        Assert.Equal("Asset", r.ForwardName);
        Assert.Equal("MaintenanceProfile", r.ReverseName);
        Assert.Null(r.ReverseNameConflict);
    }

    [Fact]
    public void Both_read_types_carry_the_navigation_pair()
    {
        var models = Load("aspect-shared-key.m3l.md");
        var oneToOne = OneToOneRelationships.Describe(models);

        var aspect = EntityPairRenderer.Render(
            models.Single(m => m.Name == "AssetMaintenanceProfile"), "Test.Entities", oneToOne: oneToOne);
        var @base = EntityPairRenderer.Render(
            models.Single(m => m.Name == "Asset"), "Test.Entities", oneToOne: oneToOne);

        Assert.Contains("public AssetExt Asset { get; set; } = null!;", aspect.Read);
        Assert.Contains("public AssetMaintenanceProfileExt? MaintenanceProfile { get; set; }", @base.Read);
    }

    /// <summary>
    /// EF 가 관계를 «구성»으로 알게 된다 — aspect 에 한해서.
    /// </summary>
    [Fact]
    public void The_dbcontext_configures_the_aspect_relationship_explicitly()
    {
        var models = Load("aspect-shared-key.m3l.md");

        var code = DbContextRenderer.Render(models, "AppDbContext", "Test.Entities", oneToOne:
            OneToOneRelationships.Describe(models));

        Assert.Contains(
            "modelBuilder.Entity<AssetMaintenanceProfileExt>()"
            + ".HasOne(e => e.Asset).WithOne(e => e.MaintenanceProfile)"
            + ".HasForeignKey<AssetMaintenanceProfileExt>(e => e.Id)"
            + ".OnDelete(DeleteBehavior.Cascade);",
            code);
    }

    /// <summary>
    /// 두 방언이 같은 구성을 낸다 — 관계는 CLR 축의 사실이고 방언이 바꿀 것이 아니다.
    /// </summary>
    /// <remarks>
    /// 방언 분기가 하나라도 이 줄에 닿으면 한쪽 소비자만 관계를 잃고, 그 손실은 생성이 아니라
    /// <c>$expand</c> 가 빈 값을 돌려줄 때 나타난다. 두 벌을 돌려 <b>같은지</b> 본다.
    /// </remarks>
    [Fact]
    public void Both_dialects_configure_the_relationship_identically()
    {
        var models = Load("aspect-shared-key.m3l.md");
        var oneToOne = OneToOneRelationships.Describe(models);

        const string expected =
            "modelBuilder.Entity<AssetMaintenanceProfileExt>()"
            + ".HasOne(e => e.Asset).WithOne(e => e.MaintenanceProfile)"
            + ".HasForeignKey<AssetMaintenanceProfileExt>(e => e.Id)"
            + ".OnDelete(DeleteBehavior.Cascade);";

        var tsql = DbContextRenderer.Render(
            models, "AppDbContext", "Test.Entities", postgresNaming: false, oneToOne: oneToOne);
        var pg = DbContextRenderer.Render(
            models, "AppDbContext", "Test.Entities", postgresNaming: true, oneToOne: oneToOne);

        Assert.Contains(expected, tsql);
        Assert.Contains(expected, pg);
    }

    /// <summary>
    /// negative control — 보통 1:1 은 구성 코드를 내지 <b>않는다</b>. 관례가 추론하기 때문이고,
    /// 그 결정은 그대로다. 이 단언이 없으면 위 하나만으로는 「이 생성기가 모든 1:1 에 구성을
    /// 낸다」로도 통과한다.
    /// </summary>
    [Fact]
    public void An_ordinary_one_to_one_is_left_to_convention()
    {
        var models = Load("one-to-one-unique.m3l.md");

        var code = DbContextRenderer.Render(models, "AppDbContext", "Test.Entities", oneToOne:
            OneToOneRelationships.Describe(models));

        Assert.DoesNotContain(".HasOne(", code);
    }
}
