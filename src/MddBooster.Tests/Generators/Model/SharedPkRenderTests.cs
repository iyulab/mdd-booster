using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// 2026-07-22 회귀 — 공유 PK 1:1 확장 테이블(PK 필드명이 `id`가 아닌 모델)에서
/// Model 생성기가 PK 필드를 조용히 삼켜 SQL 타깃과 모순된 엔티티가 생성됨.
/// 설계: `IyuEntity.Id` 상속을 유지하고 DbContext fluent `HasColumnName`으로
/// PK 컬럼에 재매핑한다. Guid와 양립 불가한 PK는 명시적 오류.
/// </summary>
public class SharedPkRenderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static IReadOnlyList<ResolvedModel> LoadSharedPk() =>
        new InterfaceResolver(new M3lLoader().LoadFile(FixturePath("shared-pk-extension.m3l.md")))
            .ResolveAll().ToList();

    [Fact]
    public void Entity_elides_shared_pk_field_and_keeps_other_fk_fields()
    {
        var profile = LoadSharedPk().Single(m => m.Name == "AssetMaintenanceProfile");

        var rendered = EntityPairRenderer.Render(profile, "Test.Entities");

        // PK 필드는 IyuEntity.Id로 표현된다 — 별도 AssetId 속성이 나오면 안 된다.
        Assert.DoesNotContain("AssetId", rendered.Write);
        // PK가 아닌 FK 필드는 그대로 스칼라 속성으로 렌더된다.
        Assert.Contains("public Guid? CriticalityId { get; set; }", rendered.Write);
    }

    [Fact]
    public void DbContext_remaps_inherited_Id_to_shared_pk_column_for_base_and_ext()
    {
        var models = LoadSharedPk();

        var output = DbContextRenderer.Render(models, "TestDbContext", "Test.Ns");

        Assert.Contains(
            "modelBuilder.Entity<AssetMaintenanceProfile>().Property(e => e.Id).HasColumnName(\"AssetId\");",
            output);
        Assert.Contains(
            "modelBuilder.Entity<AssetMaintenanceProfileExt>().Property(e => e.Id).HasColumnName(\"AssetId\");",
            output);
    }

    [Fact]
    public void DbContext_does_not_remap_models_whose_pk_is_id()
    {
        var models = LoadSharedPk();

        var output = DbContextRenderer.Render(models, "TestDbContext", "Test.Ns");

        Assert.DoesNotContain("Entity<Asset>().Property", output);
        Assert.DoesNotContain("Entity<AssetCriticality>().Property", output);
    }

    [Fact]
    public void Shared_pk_sql_column_and_dbcontext_mapping_agree()
    {
        var models = LoadSharedPk();
        var profile = models.Single(m => m.Name == "AssetMaintenanceProfile");
        var pkField = profile.Fields.Single(f => f.Name == "asset_id");

        // SQL 타깃: PK 컬럼명 [AssetId] + 공유 PK REFERENCES.
        var sqlLine = MddBooster.Generators.Sql.ColumnRenderer.Render(pkField);
        Assert.Equal(
            "[AssetId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES [dbo].[Asset]([Id])",
            sqlLine);

        // Model 타깃: 같은 컬럼명으로 Id 재매핑 — 두 타깃 산출물이 모순되지 않는다.
        var dbContext = DbContextRenderer.Render(models, "Ctx", "Ns");
        Assert.Contains("HasColumnName(\"AssetId\")", dbContext);
    }

    [Fact]
    public void Shared_pk_end_to_end_produces_well_formed_csharp()
    {
        var models = LoadSharedPk();

        var sources = models
            .Select(m => EntityPairRenderer.Render(m, "Test.Entities"))
            .SelectMany(p => new[] { p.Interface, p.Write, p.Read })
            .Append(DbContextRenderer.Render(models, "TestDbContext", "Test.Entities"));

        foreach (var source in sources)
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
            Assert.DoesNotContain(tree.GetDiagnostics(),
                d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void Non_identifier_pk_fails_loudly_instead_of_silent_drop()
    {
        var model = new InterfaceResolver(
                new M3lLoader().LoadFile(FixturePath("non-guid-pk.m3l.md")))
            .ResolveAll().Single(m => m.Name == "CodeKeyed");

        var ex = Assert.Throws<InvalidOperationException>(
            () => EntityPairRenderer.Render(model, "Test.Entities"));

        Assert.Contains("identifier", ex.Message);
        Assert.Contains("code", ex.Message);
    }
}
