using MddBooster.Core.Naming;

namespace MddBooster.Generators.Sql.Postgres;

public sealed class PostgresSqlGeneratorOptions
{
    /// <summary>desired-state 스키마 디렉터리 루트. 상대 경로면 WorkingDirectory 기준.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>PG 스키마 이름. 기본 <c>public</c>. 식별자 게이트를 통과해야 한다.</summary>
    public string Schema { get; init; } = "public";

    /// <summary>enum 컬럼 CHECK 제약(<c>ck_{table}_{column}</c>) 방출 여부.</summary>
    public bool EmitEnumCheckConstraints { get; init; }
}

/// <summary>
/// PostgreSQL 방언 Sql 타깃 — 모델 전체를 <c>{projectPath}/tables_gen/{table}.sql</c>
/// (Schemorph desired-state 관례: 테이블당 한 파일)로 방출한다. 범위는 Schemorph PG P1과
/// 정렬(테이블·컬럼·제약). 게이트 위반은 **전 모델에 걸쳐 모아** 한 번에 실패시키고,
/// 실패 시 기존 산출물을 지우지 않는다(부분 출력 금지). 방출 불가 항목(derived 필드·@index)은
/// stderr 경고로 표면화한다 — 무음 탈락 금지.
/// </summary>
public sealed class PostgresSqlGenerator : IArtifactGenerator
{
    private readonly PostgresSqlGeneratorOptions _options;

    public PostgresSqlGenerator(PostgresSqlGeneratorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "sql-pg";

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schemaViolation = PostgresIdentifiers.Check(_options.Schema);
        if (schemaViolation is not null)
        {
            throw new PostgresNamingException([$"스키마명: {schemaViolation}"]);
        }

        var tableNames = PostgresIdentifiers.BuildTableNameMap(context.Models.Select(m => m.Name));
        var modelLookup = context.Models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var enumLookup = context.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var planner = new ViewPlanner();

        // 1. 전 모델 렌더 — 위반은 모델 단위로 멈추지 않고 전부 수집한 뒤 한 번에 실패
        var artifacts = new List<PgTableArtifact>();
        var violations = new List<string>();
        foreach (var model in context.Models)
        {
            if (planner.Plan(model).NeedsAnyView)
            {
                Console.Error.WriteLine(
                    $"[sql-pg] 경고: 모델 '{model.Name}'의 derived 필드(lookup/rollup/computed)는 " +
                    "PG 방언이 아직 방출하지 않는다 — 뷰는 Schemorph P3(프로그래머블) 지원 후 재개");
            }

            try
            {
                var artifact = PgTableRenderer.Render(
                    model, _options.Schema, tableNames, modelLookup, enumLookup,
                    _options.EmitEnumCheckConstraints);
                foreach (var warning in artifact.Warnings)
                {
                    Console.Error.WriteLine($"[sql-pg] 경고 ({model.Name}): {warning}");
                }
                artifacts.Add(artifact);
            }
            catch (PostgresNamingException ex)
            {
                violations.AddRange(ex.Violations);
            }
        }

        if (violations.Count > 0)
        {
            throw new PostgresNamingException(violations);
        }

        // 2. 전부 통과했을 때만 산출물 갱신
        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var tablesGenDir = Path.Combine(projectRoot, "tables_gen");
        CleanSqlDir(tablesGenDir);
        foreach (var artifact in artifacts)
        {
            File.WriteAllText(Path.Combine(tablesGenDir, artifact.TableName + ".sql"), artifact.Sql);
        }
    }

    private string ResolveProjectRoot(string workingDirectory)
    {
        if (Path.IsPathRooted(_options.ProjectPath))
        {
            return Path.GetFullPath(_options.ProjectPath);
        }
        return Path.GetFullPath(Path.Combine(workingDirectory, _options.ProjectPath));
    }

    private static void CleanSqlDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.sql"))
                File.Delete(file);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
    }
}
