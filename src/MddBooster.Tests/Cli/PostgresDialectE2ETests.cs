using MddBooster.Cli.Commands;
using MddBooster.Core.Naming;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Sql 타깃 `dialect: postgres` 배선 E2E — mdd.json → PostgresSqlGenerator →
/// `tables_gen/{snake}.sql`. 게이트 위반·비호환 노브는 빌드 오류로 전파되고,
/// 방출 불가 항목(derived 필드·@index)은 stderr 경고로 표면화된다.
/// </summary>

[Collection(ConsoleCaptureCollection.Name)]
public class PostgresDialectE2ETests
{
    private const string ChainModel =
        "# Namespace: X\n\n" +
        "## Customer\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n\n" +
        "## WorkOrder\n" +
        "- id: identifier @pk @generated\n" +
        "- customer_id: identifier @reference(Customer)\n" +
        "- title: string(100) @not_null\n";

    // Model 타깃용 — 0.6.0부터 Timestampable(created_at/updated_at) 선언이 필수다.
    private const string TimestampedChainModel =
        "# Namespace: X\n\n" +
        "## Timestampable ::interface\n" +
        "- created_at: timestamp = now()\n" +
        "- updated_at: timestamp = now()\n\n" +
        "## Customer : Timestampable\n" +
        "- id: identifier @pk @generated\n" +
        "- name: string(50) @not_null\n\n" +
        "## WorkOrder : Timestampable\n" +
        "- id: identifier @pk @generated\n" +
        "- customer_id: identifier @reference(Customer)\n" +
        "- title: string(100) @not_null\n";

    private static (string mddDir, string dbDir) Scaffold(string tag, string model)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-pg-{tag}-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "db");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), model);
        return (mddDir, dbDir);
    }

    private static void WriteConfig(string mddDir, string targetJson) =>
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{ \"sources\": [\"./model.m3l.md\"], \"targets\": [" + targetJson + "] }");

    private static void Cleanup(string mddDir)
    {
        try { Directory.Delete(Path.GetDirectoryName(mddDir)!, recursive: true); } catch { }
    }

    [Fact]
    public void PostgresDialect_GeneratesSnakeCaseTablesGen()
    {
        var (mddDir, dbDir) = Scaffold("basic", ChainModel);
        // schema 생략 → PG 기본 public
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var workOrderPath = Path.Combine(dbDir, "tables_gen", "work_order.sql");
            Assert.True(File.Exists(Path.Combine(dbDir, "tables_gen", "customer.sql")));
            Assert.True(File.Exists(workOrderPath));

            var sql = File.ReadAllText(workOrderPath);
            Assert.Contains("CREATE TABLE public.work_order", sql);
            Assert.Contains("CONSTRAINT fk_work_order_customer_id FOREIGN KEY (customer_id) REFERENCES public.customer (id)", sql);
            Assert.DoesNotContain("GO", sql);
            Assert.DoesNotContain("dbo", sql);
            // SSDT 산출물이 생기지 않아야 한다
            Assert.False(Directory.Exists(Path.Combine(dbDir, "dbo")));
        }
        finally { Cleanup(mddDir); }
    }

    [Fact]
    public void PostgresDialect_ReservedModelName_FailsBuild()
    {
        var (mddDir, _) = Scaffold("reserved",
            "# Namespace: X\n\n## Order\n- id: identifier @pk @generated\n- title: string(50)\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        try
        {
            var ex = Assert.Throws<PostgresNamingException>(() => new BuildCommand().Run(mddDir));
            Assert.Contains("Order", ex.Message);
        }
        finally { Cleanup(mddDir); }
    }

    [Fact]
    public void PostgresDialect_WithEmitSqlProj_IsExplicitError()
    {
        var (mddDir, _) = Scaffold("sqlproj", ChainModel);
        WriteConfig(mddDir,
            "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\", \"emitSqlProj\": true }");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new BuildCommand().Run(mddDir));
            Assert.Contains("emitSqlProj", ex.Message);
        }
        finally { Cleanup(mddDir); }
    }

    [Fact]
    public void UnknownDialect_IsExplicitError()
    {
        var (mddDir, _) = Scaffold("unknown", ChainModel);
        WriteConfig(mddDir,
            "{ \"type\": \"Sql\", \"dialect\": \"oracle\", \"projectPath\": \"../db\" }");

        try
        {
            var ex = Assert.Throws<NotSupportedException>(() => new BuildCommand().Run(mddDir));
            Assert.Contains("oracle", ex.Message);
        }
        finally { Cleanup(mddDir); }
    }

    [Fact]
    public void PostgresDialect_LookupField_EmitsFullView()
    {
        // PG Sql 타깃도 T-SQL과 대칭으로 Lookup 파생 필드가 있는 모델의
        // {table}_full_view를 방출한다(더 이상 경고-스킵하지 않는다).
        var (mddDir, dbDir) = Scaffold("lookup",
            "# Namespace: X\n\n" +
            "## Product\n" +
            "- id: identifier @pk @generated\n" +
            "- cat_id: identifier @reference(Category) @not_null\n" +
            "- cat_name: string @lookup(cat_id.name)\n\n" +
            "## Category\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        using var captured = new ConsoleErrorCapture(this);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var productSql = File.ReadAllText(Path.Combine(dbDir, "tables_gen", "product.sql"));
            Assert.DoesNotContain("cat_name", productSql); // derived — 물리 컬럼 아님

            var viewPath = Path.Combine(dbDir, "views_gen", "product_full_view.sql");
            Assert.True(File.Exists(viewPath));
            var viewSql = File.ReadAllText(viewPath);
            Assert.Contains("CREATE VIEW public.product_full_view AS", viewSql);
            Assert.Contains("LEFT JOIN public.category AS j_cat_id ON b.cat_id = j_cat_id.id", viewSql);
            Assert.Contains("j_cat_id.name AS cat_name", viewSql);
            Assert.DoesNotContain("GO", viewSql);
            Assert.DoesNotContain("[", viewSql);

            Assert.DoesNotContain("[sql-pg]", captured.Text); // 이제 지원 대상이라 경고 없음
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_RollupField_EmitsFullView()
    {
        var (mddDir, dbDir) = Scaffold("rollup",
            "# Namespace: X\n\n" +
            "## Category\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- product_count: integer @rollup(Product.cat_id, count)\n\n" +
            "## Product\n" +
            "- id: identifier @pk @generated\n" +
            "- cat_id: identifier @reference(Category) @not_null\n" +
            "- price: decimal(10,2) @not_null\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var viewSql = File.ReadAllText(Path.Combine(dbDir, "views_gen", "category_full_view.sql"));
            Assert.Contains("CREATE VIEW public.category_full_view AS", viewSql);
            Assert.Contains("(SELECT COUNT(*) FROM public.product WHERE cat_id = b.id) AS product_count", viewSql);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_SoftDelete_EmitsUdView()
    {
        var (mddDir, dbDir) = Scaffold("softdelete",
            "# Namespace: X\n\n" +
            "## Widget\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- deleted_at: timestamp?\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var viewSql = File.ReadAllText(Path.Combine(dbDir, "views_gen", "widget_ud_view.sql"));
            Assert.Contains("CREATE VIEW public.widget_ud_view AS", viewSql);
            Assert.Contains("WHERE b.deleted_at IS NULL", viewSql);
            Assert.False(File.Exists(Path.Combine(dbDir, "views_gen", "widget_full_view.sql")));
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_UdViewName_OverLengthGate_FailsBuildInsteadOfSilentTruncation()
    {
        // 테이블명 자체는 63바이트 게이트를 통과해도 "_ud_view" 접미사가 붙으면 새로 넘을 수
        // 있다 — PG는 초과분을 조용히 절단하므로, 이 리포의 게이트 원칙(무음 보정 금지)대로
        // 여기서도 모아서 실패시켜야 한다.
        var longName = "X" + new string('a', 57); // snake화해도 58바이트 — 그 자체는 유효
        var (mddDir, _) = Scaffold("udlen",
            "# Namespace: X\n\n" +
            $"## {longName}\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n" +
            "- deleted_at: timestamp?\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        try
        {
            var ex = Assert.Throws<PostgresNamingException>(() => new BuildCommand().Run(mddDir));
            Assert.Contains("UdView명", ex.Message);
            Assert.Contains("63바이트", ex.Message);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_ComputedField_SkippedWithStderrWarning()
    {
        // Computed는 방언별 표현식 문법 차이로 안전한 자동 변환이 불가해 아직 방출하지
        // 않는다 — 무음 탈락 금지: stderr 경고 필수.
        var (mddDir, dbDir) = Scaffold("computed",
            "# Namespace: X\n\n" +
            "## Invoice\n" +
            "- id: identifier @pk @generated\n" +
            "- subtotal: decimal(12,2) @not_null\n" +
            "- grand_total: decimal(12,2) @computed(`subtotal * 1.1`)\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        using var captured = new ConsoleErrorCapture(this);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            Assert.False(File.Exists(Path.Combine(dbDir, "views_gen", "invoice_full_view.sql")));
            var stderr = captured.Text;
            Assert.Contains("[sql-pg]", stderr);
            Assert.Contains("Invoice", stderr);
            Assert.Contains("Computed", stderr);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_ChainedLookupThroughUnsupportedTarget_SkippedWithStderrWarning()
    {
        // Parent의 FullView는 Computed 때문에 아직 방출되지 않는다 — Child의 Lookup이 그
        // Computed 파생 컬럼을 체이닝으로 읽으므로, Child도 존재하지 않을 뷰를 참조하지
        // 않도록 함께 보류돼야 한다.
        var (mddDir, dbDir) = Scaffold("chained",
            "# Namespace: X\n\n" +
            "## Parent\n" +
            "- id: identifier @pk @generated\n" +
            "- subtotal: decimal(12,2) @not_null\n" +
            "- grand_total: decimal(12,2) @computed(`subtotal * 1.1`)\n\n" +
            "## Child\n" +
            "- id: identifier @pk @generated\n" +
            "- parent_id: identifier @reference(Parent) @not_null\n" +
            "- parent_total: decimal(12,2) @lookup(parent_id.grand_total)\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        using var captured = new ConsoleErrorCapture(this);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            Assert.False(File.Exists(Path.Combine(dbDir, "views_gen", "parent_full_view.sql")));
            Assert.False(File.Exists(Path.Combine(dbDir, "views_gen", "child_full_view.sql")));
            var stderr = captured.Text;
            Assert.Contains("'Parent'", stderr);
            Assert.Contains("'Child'", stderr);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void ModelTarget_PostgresDialect_BakesExplicitMappings()
    {
        var (mddDir, _) = Scaffold("model", TimestampedChainModel);
        var entitiesDir = Path.Combine(Path.GetDirectoryName(mddDir)!, "entities");
        Directory.CreateDirectory(entitiesDir);
        WriteConfig(mddDir,
            "{ \"type\": \"Model\", \"dialect\": \"postgres\", \"projectPath\": \"../entities\", " +
            "\"namespace\": \"X.Entities\", \"dbContextName\": \"XDbContext\" }");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var ctx = File.ReadAllText(Path.Combine(entitiesDir, "DbContext_gen", "XDbContext.cs"));
            Assert.Contains("e.ToTable(\"work_order\");", ctx);
            Assert.Contains("e.Property(x => x.CustomerId).HasColumnName(\"customer_id\");", ctx);
        }
        finally { Cleanup(mddDir); }
    }

    [Fact]
    public void SqlPostgresWithoutModelDialect_WarnsAboutMismatch()
    {
        // Sql 타깃은 snake DB를 만드는데 Model 타깃이 기본(tsql) 매핑이면 런타임에야
        // 깨진다 — 빌드 시점 경고로 표면화한다.
        var (mddDir, _) = Scaffold("mismatch", TimestampedChainModel);
        var entitiesDir = Path.Combine(Path.GetDirectoryName(mddDir)!, "entities");
        Directory.CreateDirectory(entitiesDir);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{ "sources": ["./model.m3l.md"], "targets": [
  { "type": "Sql", "dialect": "postgres", "projectPath": "../db" },
  { "type": "Model", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" }
] }
""");

        using var captured = new ConsoleErrorCapture(this);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);
            Assert.Contains("dialect", captured.Text);
        }
        finally
        {
            Cleanup(mddDir);
        }
    }

    [Fact]
    public void PostgresDialect_EnumCheckOptIn_EmitsAnsiCheck()
    {
        var (mddDir, dbDir) = Scaffold("enumcheck",
            "# Namespace: X\n\n" +
            "## DeviceStatus ::enum\n" +
            "- active: \"활성\"\n" +
            "- retired: \"퇴역\"\n\n" +
            "## Device\n" +
            "- id: identifier @pk @generated\n" +
            "- status: DeviceStatus @not_null\n");
        WriteConfig(mddDir,
            "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\", \"emitEnumCheckConstraints\": true }");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var deviceSql = File.ReadAllText(Path.Combine(dbDir, "tables_gen", "device.sql"));
            Assert.Contains("CONSTRAINT ck_device_status CHECK (status IN ('active', 'retired'))", deviceSql);
            Assert.DoesNotContain("N'", deviceSql);
        }
        finally { Cleanup(mddDir); }
    }
}
