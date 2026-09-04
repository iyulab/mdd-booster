using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql.Postgres;

public sealed class PostgresSqlGeneratorOptions
{
    /// <summary>desired-state 스키마 디렉터리 루트. 상대 경로면 WorkingDirectory 기준.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>PG 스키마 이름. 기본 <c>public</c>. 식별자 게이트를 통과해야 한다.</summary>
    public string Schema { get; init; } = "public";

    /// <summary>enum 컬럼 CHECK 제약(<c>ck_{table}_{column}</c>) 방출 여부.</summary>
    public bool EmitEnumCheckConstraints { get; init; }

    /// <summary>
    /// 외래 키 컬럼에 인덱스(<c>ix_{table}_{column}</c>)를 자동 생성할지. 기본값 <c>false</c>.
    /// 판정은 <see cref="ForeignKeyIndexPlanner"/>가 방언과 무관하게 내린다.
    /// </summary>
    public bool EmitForeignKeyIndexes { get; init; }
}

/// <summary>
/// PostgreSQL 방언 Sql 타깃 — 테이블은 <c>{projectPath}/tables_gen/{table}.sql</c>,
/// Lookup/Rollup 파생 필드가 있는 모델의 <c>{table}_full_view.sql</c>(및 soft-delete가
/// 있으면 <c>{table}_ud_view.sql</c>)은 <c>{projectPath}/views_gen/</c>로 방출한다
/// (Schemorph desired-state 관례: 객체당 한 파일. §desired-state-format.md 상 이 레이아웃
/// 자체는 규약이지 강제는 아니다). 게이트 위반은 **전 모델에 걸쳐 모아** 한 번에 실패시키고,
/// 실패 시 기존 산출물을 지우지 않는다(부분 출력 금지). Computed 파생 필드·<c>@indexed</c>
/// Rollup이 있는 모델(및 그런 모델을 체이닝으로 딛는 모델)은 아직 방출하지 않고 stderr
/// 경고로 표면화한다 — 무음 탈락 금지.
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

        var violations = new List<string>();

        // 1. 테이블 — 위반은 모델 단위로 멈추지 않고 전부 수집한 뒤 한 번에 실패
        var tableArtifacts = new List<PgTableArtifact>();
        foreach (var model in context.Models)
        {
            try
            {
                var artifact = PgTableRenderer.Render(
                    model, _options.Schema, tableNames, modelLookup, enumLookup,
                    _options.EmitEnumCheckConstraints,
                    _options.EmitForeignKeyIndexes);
                foreach (var warning in artifact.Warnings)
                {
                    Console.Error.WriteLine($"[sql-pg] 경고 ({model.Name}): {warning}");
                }
                tableArtifacts.Add(artifact);
            }
            catch (PostgresNamingException ex)
            {
                violations.AddRange(ex.Violations);
            }
        }

        // 2. 뷰 — 대상 판정 + 순환 검출은 T-SQL 타깃(SqlGenerator)과 동일한 AST 레벨 로직
        // (FullViewCycleDetector/FullViewRenderer의 dialect-무관 정적 헬퍼)을 그대로 재사용한다.
        var allPlans = context.Models.Select(planner.Plan).ToList();
        var derivedFieldsByModel = allPlans
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

        var cycle = FullViewCycleDetector.Detect(allPlans, derivedFieldsByModel);
        if (cycle != null)
        {
            var path = string.Join(" -> ", cycle.Select(step =>
                step.Via is null ? step.Model : $"{step.Model} (via {step.Via})"));
            throw new InvalidOperationException(
                $"순환 FullView 의존성 발견: {path}. 두 모델의 뷰가 서로를 참조하면 배포할 수 없다. " +
                "체이닝된 Lookup이 다른 모델의 파생(Lookup/Rollup/Computed) 컬럼을 거치는데, 그 대상 " +
                "모델도 원 모델의 파생 컬럼을 Rollup으로 되읽을 때 발생한다. 체이닝된 Lookup을 원본 " +
                "컬럼으로 바꾸거나, Rollup의 집계 대상을 원본 컬럼으로 바꿔 순환을 끊어야 한다.");
        }

        // 이번 렌더러가 직접 지원하는 것은 Lookup/Rollup 파생 컬럼뿐이다 — Computed(표현식 문법이
        // 방언마다 달라 안전한 자동 변환 불가) · @indexed Rollup(구체화 뷰 갱신 전략은 별도 결정)이
        // 있는 모델은 스킵하고 경고한다. 부분 뷰(일부 파생 컬럼만 담은 FullView)는 만들지 않는다 —
        // Model 타깃이 여전히 존재하지 않는 컬럼을 가리키는 이 이슈와 같은 결함 클래스가 재발한다.
        var selfUnsupported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in allPlans.Where(p => p.NeedsFullView))
        {
            var nonInternalComputeds = plan.Computeds.Where(f => !EntitySurface.IsFieldInternal(f)).ToList();
            if (nonInternalComputeds.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[sql-pg] 경고: 모델 '{plan.Model.Name}'의 Computed 파생 필드는 PG 방언이 아직 " +
                    "방출하지 않는다(표현식 문법 차이로 안전한 자동 변환 불가) — 해당 뷰를 직접 만들기 전까지 Ext 질의는 실패한다.");
                selfUnsupported.Add(plan.Model.Name);
                continue;
            }

            var indexedRollups = plan.Rollups
                .Where(f => !EntitySurface.IsFieldInternal(f))
                .Where(f => FieldAttributes.Has(f, "indexed"))
                .ToList();
            if (indexedRollups.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[sql-pg] 경고: 모델 '{plan.Model.Name}'의 @indexed Rollup은 PG 방언이 아직 " +
                    "방출하지 않는다(구체화 뷰 갱신 전략이 별도 결정 필요) — 해당 뷰를 직접 만들기 전까지 Ext 질의는 실패한다.");
                selfUnsupported.Add(plan.Model.Name);
            }
        }

        // 체이닝 전파 — A의 Lookup/Rollup이 B의 파생 컬럼을 거쳐 B의 FullView를 딛어야 하는데
        // B가 (스스로든, 전파로든) 미지원이면 A도 존재하지 않을 뷰를 참조하게 된다.
        var hardTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var plan in allPlans.Where(p => p.NeedsFullView))
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var lookup in plan.Lookups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var path = lookup.Lookup?.Path;
                if (path is null) continue;
                var (fkField, targetColumn) = FullViewRenderer.ParsePath(path);
                var target = FullViewRenderer.ResolveReferenceTarget(plan.Model, fkField);
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, target, NameCasing.ToPascalCase(targetColumn)))
                    targets.Add(target);
            }
            foreach (var rollup in plan.Rollups.Where(f => !EntitySurface.IsFieldInternal(f)))
            {
                var def = rollup.Rollup;
                if (def is null || string.IsNullOrEmpty(def.Field)) continue;
                if (FullViewRenderer.IsDerivedColumn(derivedFieldsByModel, def.Target, NameCasing.ToPascalCase(def.Field)))
                    targets.Add(def.Target);
            }
            hardTargets[plan.Model.Name] = targets;
        }

        var unsupported = new HashSet<string>(selfUnsupported, StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var (modelName, targets) in hardTargets)
            {
                if (unsupported.Contains(modelName)) continue;
                if (targets.Any(unsupported.Contains))
                {
                    unsupported.Add(modelName);
                    changed = true;
                    if (!selfUnsupported.Contains(modelName))
                    {
                        Console.Error.WriteLine(
                            $"[sql-pg] 경고: 모델 '{modelName}'의 FullView는 아직 미지원인 다른 모델의 " +
                            "파생 컬럼을 체이닝으로 딛고 있어 함께 보류한다 — 해당 뷰를 직접 만들기 전까지 Ext 질의는 실패한다.");
                    }
                }
            }
        } while (changed);

        var renderablePlans = allPlans
            .Where(p => p.NeedsAnyView && !unsupported.Contains(p.Model.Name))
            .ToList();

        var fullViewNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plan in renderablePlans.Where(p => p.NeedsFullView))
        {
            var table = tableNames[plan.Model.Name];
            var fullView = PgFullViewRenderer.FullViewNameOf(table);
            var v = PostgresIdentifiers.Check(fullView);
            if (v is not null)
            {
                violations.Add($"모델 '{plan.Model.Name}' FullView명: {v}");
            }
            else
            {
                fullViewNames[plan.Model.Name] = fullView;
            }
        }

        // 테이블명은 이미 게이트를 통과했지만 "_ud_view"/"_full_view" 접미사를 더하면 63바이트
        // 한계를 새로 넘을 수 있다 — 조용한 절단 없이 여기서도 모아서 실패시킨다(위 FullView와 동형).
        var udViewNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plan in renderablePlans.Where(p => p.NeedsUdView))
        {
            var table = tableNames[plan.Model.Name];
            var udView = PgFullViewRenderer.UdViewNameOf(table);
            var v = PostgresIdentifiers.Check(udView);
            if (v is not null)
            {
                violations.Add($"모델 '{plan.Model.Name}' UdView명: {v}");
            }
            else
            {
                udViewNames[plan.Model.Name] = udView;
            }
        }

        if (violations.Count > 0)
        {
            throw new PostgresNamingException(violations);
        }

        // 3. 전부 통과했을 때만 산출물 갱신
        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var tablesGenDir = Path.Combine(projectRoot, "tables_gen");
        var viewsGenDir = Path.Combine(projectRoot, "views_gen");
        CleanSqlDir(tablesGenDir);
        CleanSqlDir(viewsGenDir);

        foreach (var artifact in tableArtifacts)
        {
            File.WriteAllText(Path.Combine(tablesGenDir, artifact.TableName + ".sql"), artifact.Sql);
        }

        foreach (var plan in renderablePlans)
        {
            var table = tableNames[plan.Model.Name];

            if (plan.NeedsUdView)
            {
                var udView = udViewNames[plan.Model.Name];
                var sql = PgUdViewRenderer.Render(plan.Model, _options.Schema, table, udView);
                File.WriteAllText(Path.Combine(viewsGenDir, udView + ".sql"), sql);
            }

            if (plan.NeedsFullView)
            {
                var sql = PgFullViewRenderer.Render(
                    plan, _options.Schema, tableNames, fullViewNames, modelLookup, derivedFieldsByModel);
                File.WriteAllText(Path.Combine(viewsGenDir, fullViewNames[plan.Model.Name] + ".sql"), sql);
            }
        }
    }

    private string ResolveProjectRoot(string workingDirectory)
        => ConfiguredPathResolver.Resolve(workingDirectory, _options.ProjectPath, "projectPath");

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
