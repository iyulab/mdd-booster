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
        // (속성을 본다: 모델 설명이 문서 주석으로 실리면 그 글이 열 이름을 말할 수 있다.)
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\bpublic\s+\S+\s+AssetId\b"), rendered.Write);
        // PK가 아닌 FK 필드는 그대로 스칼라 속성으로 렌더된다.
        Assert.Contains("public Guid? CriticalityId { get; set; }", rendered.Write);
    }

    [Fact]
    public void DbContext_remaps_inherited_Id_to_shared_pk_column_for_base_and_ext()
    {
        var models = LoadSharedPk();

        var mapping = ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns");

        Assert.Contains(
            "builder.Property(e => e.Id).HasColumnName(\"AssetId\");",
            mapping["AssetMaintenanceProfile"]);
        Assert.Contains(
            "builder.Property(e => e.Id).HasColumnName(\"AssetId\");",
            mapping["AssetMaintenanceProfileExt"]);
    }

    [Fact]
    public void DbContext_does_not_remap_models_whose_pk_is_id()
    {
        var models = LoadSharedPk();

        var mapping = ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns");

        Assert.DoesNotContain("builder.Property", mapping["Asset"]);
        Assert.DoesNotContain("builder.Property", mapping["AssetCriticality"]);
    }

    [Fact]
    public void Shared_pk_sql_column_and_dbcontext_mapping_agree()
    {
        var models = LoadSharedPk();
        var profile = models.Single(m => m.Name == "AssetMaintenanceProfile");
        var pkField = profile.Fields.Single(f => f.Name == "asset_id");

        // SQL 타깃: PK 컬럼명 [AssetId] + 공유 PK REFERENCES.
        var tableSql = MddBooster.Generators.Sql.TableRenderer.Render(profile, "dbo", allModels: models);
        Assert.Contains(
            "[AssetId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES [dbo].[Asset]([Id])",
            tableSql);
        Assert.Equal("asset_id", pkField.Name);

        // Model 타깃: 같은 컬럼명으로 Id 재매핑 — 두 타깃 산출물이 모순되지 않는다.
        var mapping = ModelMappingRenderer.Render(models, "Ctx", "Ns");
        Assert.Contains("HasColumnName(\"AssetId\")", mapping["AssetMaintenanceProfile"]);
    }

    [Fact]
    public void Shared_pk_end_to_end_produces_well_formed_csharp()
    {
        var models = LoadSharedPk();

        var sources = models
            .Select(m => EntityPairRenderer.Render(m, "Test.Entities"))
            .SelectMany(p => new[] { p.Interface, p.Write, p.Read })
            .Concat(ModelMappingRenderer.Render(models, "TestDbContext", "Test.Entities").Sources);

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
