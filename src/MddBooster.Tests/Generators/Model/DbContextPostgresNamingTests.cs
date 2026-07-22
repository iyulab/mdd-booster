using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// ADR-0001 §2.3 (U-Platform): 모델 PascalCase ↔ DB snake_case 간극은 **생성 코드에 구운
/// 명시 매핑**이 흡수한다 — 런타임 컨벤션 라이브러리 추론 금지. PG 방언에서 DbContext는
/// 엔티티마다 `ToTable`/`HasColumnName`(+ json → `HasColumnType("jsonb")`, D24)을 방출한다.
/// 기본(tsql) 렌더는 바이트 단위로 무변경 — 기존 소비자 무영향.
/// </summary>
public class DbContextPostgresNamingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static IReadOnlyList<ResolvedModel> Load(string fixture) =>
        new InterfaceResolver(new M3lLoader().LoadFile(FixturePath(fixture)))
            .ResolveAll().ToList();

    [Fact]
    public void PostgresNaming_MapsTableAndEveryStoredColumn()
    {
        var output = DbContextRenderer.Render(
            Load("bank-account.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        Assert.Contains("e.ToTable(\"bank_account\");", output);
        Assert.Contains("e.Property(x => x.Id).HasColumnName(\"id\");", output);
        Assert.Contains("e.Property(x => x.BankName).HasColumnName(\"bank_name\");", output);
        Assert.Contains("e.Property(x => x.AccountNumber).HasColumnName(\"account_number\");", output);
        Assert.Contains("e.Property(x => x.CreatedAt).HasColumnName(\"created_at\");", output);
    }

    [Fact]
    public void PostgresNaming_ExtReadsSnakeTableWhenNoViewBacking()
    {
        var output = DbContextRenderer.Render(
            Load("bank-account.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        // Ext(뷰 없음 backing)는 스네이크 테이블명을 읽고, 컬럼 매핑도 함께 받는다
        Assert.Contains("modelBuilder.Entity<BankAccountExt>(e =>", output);
        Assert.Contains("e.ToView(\"bank_account\");", output);
    }

    [Fact]
    public void PostgresNaming_SharedPkRemapsIdToSnakePkColumn()
    {
        var output = DbContextRenderer.Render(
            Load("shared-pk-extension.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        // 공유 PK: 상속된 Id → 물리 PK 컬럼(snake). T-SQL 경로의 "AssetId"가 아니다.
        Assert.Contains("e.Property(x => x.Id).HasColumnName(\"asset_id\");", output);
        Assert.Contains("e.Property(x => x.CriticalityId).HasColumnName(\"criticality_id\");", output);
        Assert.DoesNotContain("HasColumnName(\"AssetId\")", output);
    }

    [Fact]
    public void PostgresNaming_JsonFieldGetsJsonbColumnType()
    {
        var output = DbContextRenderer.Render(
            Load("pg-json.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        Assert.Contains(
            "e.Property(x => x.Payload).HasColumnName(\"payload\").HasColumnType(\"jsonb\");",
            output);
    }

    [Fact]
    public void DefaultRender_IsUnchangedByTheNewParameter()
    {
        var models = Load("bank-account.m3l.md");

        var legacy = DbContextRenderer.Render(models, "TestDbContext", "Test.Ns");
        var explicitDefault = DbContextRenderer.Render(
            models, "TestDbContext", "Test.Ns", postgresNaming: false);

        Assert.Equal(legacy, explicitDefault);
        Assert.DoesNotContain("ToTable(\"bank_account\")", legacy);
        Assert.DoesNotContain("HasColumnName(\"bank_name\")", legacy);
    }
}
