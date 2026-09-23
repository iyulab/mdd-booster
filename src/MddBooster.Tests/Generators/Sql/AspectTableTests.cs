using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// <c>::aspect</c> 모델이 실제로 「PK 가 곧 base FK」인 테이블로 나가는가 — <b>두 방언 모두</b>.
/// </summary>
/// <remarks>
/// <para>
/// 작성자는 키를 쓰지 않았다. 키는 <c>::aspect(Asset)</c> 가 만들고, 그것이 필드로 합성되기
/// 때문에 두 렌더러는 aspect 를 <b>특별 취급하지 않는다</b> — 공유 PK 확장 테이블을 이미
/// 다루던 길을 그대로 탄다. 그 사실이 여기서 증명되는 것이고, 어느 한 방언만 맞으면 그것은
/// 「구성에 의해 참」이 아니라 우연이다.
/// </para>
/// </remarks>
public class AspectTableTests
{
    private const string Fixture = "aspect-shared-key.m3l.md";
    private const string Aspect = "AssetMaintenanceProfile";

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static (IReadOnlyDictionary<string, ResolvedModel> Lookup,
                    IReadOnlyDictionary<string, string> TableNames,
                    IReadOnlyDictionary<string, EnumNode> Enums)
        Load()
    {
        var ast = new M3lLoader().LoadFile(FixturePath(Fixture));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        return (models.ToDictionary(m => m.Name, StringComparer.Ordinal),
                PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name)),
                ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal));
    }

    /// <summary>
    /// 모델이 선언하지 않은 키가 <b>필드로</b> 존재한다 — 아래 두 방언 단언이 렌더러의
    /// 특별 취급이 아니라 이 합성에서 나온다는 것을 먼저 고정한다.
    /// </summary>
    [Fact]
    public void An_aspect_carries_a_synthesized_key_field_named_after_its_base()
    {
        var (lookup, _, _) = Load();
        var model = lookup[Aspect];

        Assert.Equal("Asset", model.AspectBase);
        var key = ModelPrimaryKey.Find(model);
        Assert.NotNull(key);
        Assert.Equal("asset_id", key!.Name);
        Assert.Contains(model.Source.Fields ?? [], f => f.Name == "level");
        Assert.DoesNotContain(model.Source.Fields ?? [], f => f.Name == "asset_id");
    }

    [Fact]
    public void The_tsql_table_keys_the_aspect_by_its_base()
    {
        var (lookup, _, _) = Load();

        var sql = TableRenderer.Render(lookup[Aspect], schema: "dbo", allModels: lookup.Values.ToList());

        Assert.Contains(
            "[AssetId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES [dbo].[Asset]([Id]) ON DELETE CASCADE",
            sql);
    }

    [Fact]
    public void The_postgres_table_keys_the_aspect_by_its_base()
    {
        var (lookup, tableNames, enums) = Load();

        var artifact = PgTableRenderer.Render(lookup[Aspect], "public", tableNames, lookup, enums);

        Assert.Contains("CONSTRAINT pk_asset_maintenance_profile PRIMARY KEY (asset_id)", artifact.Sql);
        Assert.Contains(
            "CONSTRAINT fk_asset_maintenance_profile_asset_id FOREIGN KEY (asset_id) "
            + "REFERENCES public.asset (id) ON DELETE CASCADE",
            artifact.Sql);
    }

    /// <summary>
    /// negative control: 보통 모델의 FK 는 <b>cascade 하지 않는다</b>. 없으면 위 두 단언은
    /// 「이 생성기가 모든 FK 에 CASCADE 를 붙인다」로도 통과한다.
    /// </summary>
    [Fact]
    public void An_ordinary_reference_does_not_cascade()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll();
        var model = models.Single(m => m.Name == "Order");

        var sql = TableRenderer.Render(model, schema: "dbo", allModels: models);

        Assert.Contains("REFERENCES [dbo].[Customer]([Id])", sql);
        Assert.DoesNotContain("CASCADE", sql);
    }
}
