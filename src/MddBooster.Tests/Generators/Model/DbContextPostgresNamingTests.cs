using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// ADR-0001 §2.3: 모델 PascalCase ↔ DB snake_case 간극은 **생성 코드에 구운
/// 명시 매핑**이 흡수한다 — 런타임 컨벤션 라이브러리 추론 금지. PG 방언에서 엔티티 구성은
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
        var mapping = ModelMappingRenderer.Render(
            Load("bank-account.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        var write = mapping["BankAccount"];
        Assert.Contains("builder.ToTable(\"bank_account\");", write);
        Assert.Contains("builder.Property(x => x.Id).HasColumnName(\"id\");", write);
        Assert.Contains("builder.Property(x => x.BankName).HasColumnName(\"bank_name\");", write);
        Assert.Contains("builder.Property(x => x.AccountNumber).HasColumnName(\"account_number\");", write);
        Assert.Contains("builder.Property(x => x.CreatedAt).HasColumnName(\"created_at\");", write);
    }

    [Fact]
    public void PostgresNaming_ExtReadsSnakeTableWhenNoViewBacking()
    {
        var mapping = ModelMappingRenderer.Render(
            Load("bank-account.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        // Ext(뷰 없음 backing)는 스네이크 테이블명을 읽고, 컬럼 매핑도 함께 받는다
        Assert.Contains("builder.ToView(\"bank_account\");", mapping["BankAccountExt"]);
    }

    [Fact]
    public void PostgresNaming_SharedPkRemapsIdToSnakePkColumn()
    {
        var mapping = ModelMappingRenderer.Render(
            Load("shared-pk-extension.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        // 공유 PK: 상속된 Id → 물리 PK 컬럼(snake). T-SQL 경로의 "AssetId"가 아니다.
        var write = mapping["AssetMaintenanceProfile"];
        Assert.Contains("builder.Property(x => x.Id).HasColumnName(\"asset_id\");", write);
        Assert.Contains("builder.Property(x => x.CriticalityId).HasColumnName(\"criticality_id\");", write);
        Assert.DoesNotContain("HasColumnName(\"AssetId\")", string.Concat(mapping.Sources));
    }

    [Fact]
    public void PostgresNaming_JsonFieldGetsJsonbColumnType()
    {
        var mapping = ModelMappingRenderer.Render(
            Load("pg-json.m3l.md"), "TestDbContext", "Test.Ns", postgresNaming: true);

        Assert.Contains(
            "builder.Property(x => x.Payload).HasColumnName(\"payload\").HasColumnType(\"jsonb\");",
            mapping["ApiLog"]);
    }

    [Fact]
    public void PostgresNaming_LookupBackedModel_MapsToSnakeFullViewAndDerivedColumn()
    {
        // Ext는 이제 PascalCase "{Model}FullView"가 아니라 PG Sql 타깃이 실제로 방출하는
        // snake_case "{table}_full_view"를 가리키고, 파생(Lookup) 컬럼도 HasColumnName으로
        // 명시돼야 EF 기본 컨벤션(PascalCase 추정)과 실제 뷰 컬럼(snake)이 어긋나지 않는다.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-dbctx-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Worker\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## UserAccount\n" +
            "- id: identifier @pk @generated\n" +
            "- username: string(50) @not_null\n" +
            "- worker_id: identifier? @reference(Worker)\n" +
            "- worker_name: string? @lookup(worker_id.name)\n");
        try
        {
            var models = new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll().ToList();
            var mapping = ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns", postgresNaming: true);

            var read = mapping["UserAccountExt"];
            Assert.Contains("builder.ToView(\"user_account_full_view\");", read);
            Assert.DoesNotContain("builder.ToView(\"UserAccountFullView\");", string.Concat(mapping.Sources));
            Assert.Contains("builder.Property(x => x.WorkerName).HasColumnName(\"worker_name\");", read);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void PostgresNaming_ComputedField_MapsItsViewColumn()
    {
        // The PostgreSQL full view now computes the field (as its snake_case name); without the mapping EF would read a
        // PascalCase column the view does not have, and every query of the read type would fail.
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-dbctx-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Invoice\n" +
            "- id: identifier @pk @generated\n" +
            "- subtotal: decimal(12,2) @not_null\n" +
            "- grand_total: decimal(12,2) @computed(`subtotal * 1.1`)\n");
        try
        {
            var models = new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll().ToList();
            var mapping = ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns", postgresNaming: true);

            Assert.Contains("builder.ToView(\"invoice_full_view\");", mapping["InvoiceExt"]);
            Assert.Contains("builder.Property(x => x.GrandTotal).HasColumnName(\"grand_total\");", mapping["InvoiceExt"]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// A field-level <c>@internal</c> field is stored — the write type maps its column — but the read
    /// type has no such property, so its mapping must not name it (CS1061). Both backings: the read
    /// type reading the table itself, and reading the full view.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostgresNaming_ReadTypeDoesNotMapAFieldLevelInternalColumn(bool viewBacked)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-dbctx-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp,
            "# Namespace: test\n\n" +
            "## Worker\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n\n" +
            "## Account\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- secret: string(100) @not_null @internal\n" +
            (viewBacked
                ? "- worker_id: identifier? @reference(Worker)\n- worker_name: string? @lookup(worker_id.name)\n"
                : string.Empty));
        try
        {
            var models = new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll().ToList();
            var mapping = ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns", postgresNaming: true);

            var write = mapping["Account"];
            var read = mapping["AccountExt"];
            Assert.Contains("builder.Property(x => x.Secret).HasColumnName(\"secret\");", write);
            Assert.DoesNotContain("Secret", read);
            Assert.Contains("builder.Property(x => x.Name).HasColumnName(\"name\");", read);
            Assert.Contains(viewBacked ? "builder.ToView(\"account_full_view\");" : "builder.ToView(\"account\");", read);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void DefaultRender_IsUnchangedByTheNewParameter()
    {
        var models = Load("bank-account.m3l.md");

        var legacy = string.Concat(ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns").Sources);
        var explicitDefault = string.Concat(ModelMappingRenderer.Render(
            models, "TestDbContext", "Test.Ns", postgresNaming: false).Sources);

        Assert.Equal(legacy, explicitDefault);
        Assert.DoesNotContain("ToTable(\"bank_account\")", legacy);
        Assert.DoesNotContain("HasColumnName(\"bank_name\")", legacy);
    }
}
