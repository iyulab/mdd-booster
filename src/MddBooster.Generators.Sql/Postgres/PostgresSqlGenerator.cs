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
/// (객체당 한 파일 — 파일 단위로 읽는 선언형 스키마 도구가 객체와 파일을 일대일로 대응시킬 수 있게). 게이트 위반은 **전 모델에 걸쳐 모아** 한 번에 실패시키고,
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

    /// <summary>
    /// Refuses what this dialect cannot carry out, before any target writes a file.
    /// </summary>
    public void Validate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var violations = context.Models.SelectMany(PgTableRenderer.RowVersionIndexViolations).ToList();
        violations.AddRange(ExpressionRefusals.Violations(context, SqlDialect.PostgreSql));
        if (violations.Count > 0)
        {
            throw new GeneratorRefusalException(GeneratorRefusalKind.Model, string.Join(Environment.NewLine, violations));
        }
    }

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExpressionRefusals.Refuse(context, SqlDialect.PostgreSql);

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
        var derivedFieldsByModel = StandaloneViewSources.DerivedFieldsByModel(allPlans);

        var cycle = FullViewCycleDetector.Detect(allPlans, derivedFieldsByModel);
        if (cycle != null)
        {
            var path = string.Join(" -> ", cycle.Select(step =>
                step.Via is null ? step.Model : $"{step.Model} (via {step.Via})"));
            throw new InvalidOperationException(
                $"순환 FullView 의존성 발견: {path}. 두 모델의 뷰가 서로를 참조하면 배포할 수 없다. " +
                "체이닝된 Lookup이 다른 모델의 파생(Lookup/Rollup/Computed) 컬럼을 거치는데, 그 대상 " +
                "모델도 원 모델의 파생 컬럼을 Rollup으로 되읽을 때 발생한다. 파생 컬럼 대신 저장된 키를 " +
                "따라 읽도록 바꾸거나(예: @lookup(order_id.customer_name) 대신 " +
                "@lookup(order_id.customer_id.name) — 다단 lookup 의 각 hop 은 FullView 가 아니라 테이블을 " +
                "JOIN 한다), Rollup의 집계 대상을 원본 컬럼으로 바꿔 순환을 끊어야 한다.");
        }

        var unsupported = UnsupportedModels(allPlans, derivedFieldsByModel, Console.Error.WriteLine);

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

        // standalone ::view — 이 방언이 생성하는 것만(식 있는 뷰 등은 빌드가 MDD029 로 이미 알렸다).
        var skippedViews = StandaloneViewSources.PostgresSkipped(context.Views, context.Models);
        var standaloneViews = context.Views.Where(v => !skippedViews.ContainsKey(v.Name)).ToList();
        var standaloneViewNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var takenNames = new HashSet<string>(
            tableNames.Values.Concat(fullViewNames.Values).Concat(udViewNames.Values), StringComparer.Ordinal);
        foreach (var view in standaloneViews)
        {
            var name = PostgresIdentifiers.ToSnakeCase(view.Name);
            if (PostgresIdentifiers.Check(name) is { } v)
                violations.Add($"뷰 '{view.Name}' 이름: {v}");
            else if (!takenNames.Add(name))
                violations.Add($"뷰 '{view.Name}' 이름 '{name}' 이(가) 다른 테이블·뷰와 겹친다");
            else
                standaloneViewNames[view.Name] = name;
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

        foreach (var view in standaloneViews)
        {
            var sql = PgStandaloneViewRenderer.Render(
                view, _options.Schema, tableNames, fullViewNames, standaloneViewNames, derivedFieldsByModel);
            File.WriteAllText(Path.Combine(viewsGenDir, standaloneViewNames[view.Name] + ".sql"), sql);
        }
    }

    /// <summary>
    /// The models whose views (UdView and FullView) this dialect does not render — a non-internal
    /// <c>@computed</c> or an <c>@indexed</c> rollup, or a FullView chained onto one of those. Shared with
    /// <see cref="StandaloneViewSources.PostgresSkipped"/>, so a standalone view never reads a view that is not there.
    /// </summary>
    internal static HashSet<string> UnsupportedModels(
        IReadOnlyList<ViewPlan> allPlans,
        IReadOnlyDictionary<string, IReadOnlySet<string>> derivedFieldsByModel,
        Action<string>? warn)
    {
            // 이 렌더러가 방출하지 않는 것은 @indexed Rollup(구체화 뷰 갱신 전략은 별도 결정)뿐이다 — Computed 는
            // M3L 0.20.0 의 이식 가능한 식을 번역해 낸다(PostgresExpressions; 번역 불가는 Validate 가 거절).
            // 부분 뷰(일부 파생 컬럼만 담은 FullView)는 만들지 않는다 — Model 타깃이 존재하지 않는 컬럼을 가리키게 된다.
            var selfUnsupported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var plan in allPlans.Where(p => p.NeedsFullView))
            {
                var indexedRollups = plan.Rollups
                    .Where(f => !EntitySurface.IsFieldInternal(f))
                    .Where(f => FieldAttributes.Has(f, "indexed"))
                    .ToList();
                if (indexedRollups.Count > 0)
                {
                    warn?.Invoke(
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
                var lookups = plan.Lookups.Where(f => !EntitySurface.IsFieldInternal(f) && f.Lookup?.Path is not null);
                foreach (var (target, _) in LookupJoinPlanner.FullViewDependencies(
                             plan.Model, lookups, name => allPlans.FirstOrDefault(p => p.Model.Name == name)?.Model, derivedFieldsByModel))
                    targets.Add(target);
                foreach (var rollup in plan.Rollups.Where(f => !EntitySurface.IsFieldInternal(f)))
                {
                    var def = rollup.Rollup;
                    if (def is not null && FullViewRenderer.RollupReadsDerivedColumn(def, derivedFieldsByModel))
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
                            warn?.Invoke(
                                $"[sql-pg] 경고: 모델 '{modelName}'의 FullView는 아직 미지원인 다른 모델의 " +
                                "파생 컬럼을 체이닝으로 딛고 있어 함께 보류한다 — 해당 뷰를 직접 만들기 전까지 Ext 질의는 실패한다.");
                        }
                    }
                }
            } while (changed);
        return unsupported;
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
