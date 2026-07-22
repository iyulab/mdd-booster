using MddBooster.Cli.Commands;
using MddBooster.Core.Naming;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Sql 타깃 `dialect: postgres` 배선 E2E — mdd.json → PostgresSqlGenerator →
/// `tables_gen/{snake}.sql`. 게이트 위반·비호환 노브는 빌드 오류로 전파되고,
/// 방출 불가 항목(derived 필드·@index)은 stderr 경고로 표면화된다.
/// </summary>
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
    public void PostgresDialect_DerivedFields_SkippedWithStderrWarning()
    {
        // T-SQL 타깃은 derived 필드를 _ext 뷰로 물질화하지만 PG 방언은 뷰를 방출하지
        // 않는다(Schemorph P3 전) — 무음 탈락 금지: stderr 경고 필수.
        var (mddDir, dbDir) = Scaffold("derived",
            "# Namespace: X\n\n" +
            "## Product\n" +
            "- id: identifier @pk @generated\n" +
            "- cat_id: identifier @reference(Category) @not_null\n" +
            "- cat_name: string @lookup(cat_id.name)\n\n" +
            "## Category\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");
        WriteConfig(mddDir, "{ \"type\": \"Sql\", \"dialect\": \"postgres\", \"projectPath\": \"../db\" }");

        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var productSql = File.ReadAllText(Path.Combine(dbDir, "tables_gen", "product.sql"));
            Assert.DoesNotContain("cat_name", productSql); // derived — 물리 컬럼 아님

            var stderr = captured.ToString();
            Assert.Contains("[sql-pg]", stderr);
            Assert.Contains("Product", stderr);
        }
        finally
        {
            Console.SetError(originalError);
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

        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);
            Assert.Contains("dialect", captured.ToString());
        }
        finally
        {
            Console.SetError(originalError);
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
