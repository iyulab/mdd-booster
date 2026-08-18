using MddBooster.Cli.Config;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Cli.Commands;

public sealed class BuildCommand
{
    public int Run(string configDirectory)
    {
        configDirectory = Path.GetFullPath(configDirectory);
        if (!Directory.Exists(configDirectory))
        {
            Console.Error.WriteLine($"설정 디렉터리를 찾을 수 없습니다: {configDirectory}");
            return 2;
        }

        var cfgPath = Path.Combine(configDirectory, "mdd.json");
        var cfg = ConfigLoader.Load(cfgPath);

        // 1. M3L 소스 로드 — 전체 sources를 하나의 resolve 단위로 병합 파싱한다.
        // 파일별 독립 파싱은 cross-file 상속·인터페이스 참조를 E007로 오탐한다 (스펙 §2.1 Rule 3).
        var loader = new M3lLoader();
        var sourcePaths = cfg.Sources
            .Select(srcRel => Path.GetFullPath(Path.Combine(configDirectory, srcRel)))
            .ToList();
        foreach (var srcAbs in sourcePaths)
        {
            Console.WriteLine($"[m3l] 로딩: {srcAbs}");
        }

        var mergedAst = loader.LoadFiles(sourcePaths);

        // 파서 경고 표면화 — 조용히 삼키지 않는다.
        foreach (var w in mergedAst.Warnings)
        {
            Console.Error.WriteLine($"[m3l] 경고 [{w.Code}] {w.File}:{w.Line}:{w.Col} {w.Message}");
        }

        var allUnconsumed = new List<string>(AstAccounting.ListUnconsumed(mergedAst));

        var allModels = new List<ResolvedModel>(new InterfaceResolver(mergedAst).ResolveAll());
        var allEnums = new List<M3L.Native.EnumNode>(mergedAst.Enums);

        Console.WriteLine($"[m3l] 모델 {allModels.Count}개, enum {allEnums.Count}개 로드됨: {string.Join(", ", allModels.Select(m => m.Name))}");

        // 로더 회계 — 파싱은 되지만 생성 파이프라인이 소비하지 않는 요소를 가시화한다.
        // (standalone ::view / ::flow / extension은 현재 어떤 타깃도 산출하지 않는다.)
        if (allUnconsumed.Count > 0)
        {
            Console.Error.WriteLine(
                $"[m3l] 경고: 소비되지 않는 요소 {allUnconsumed.Count}개 — 어떤 타깃도 산출물을 생성하지 않습니다: " +
                string.Join(", ", allUnconsumed));
        }

        // 1.5. 의미 분석 — Warning은 표면화만 하고 진행, Error는 빌드 중단.
        var diagnostics = new SemanticAnalyzer(allModels, allEnums).Analyze();
        var warnings = diagnostics.Where(d => d.Severity == SemanticSeverity.Warning).ToList();
        var errors = diagnostics.Where(d => d.Severity == SemanticSeverity.Error).ToList();
        foreach (var w in warnings)
            Console.Error.WriteLine("[semantic] 경고 " + w.Format());
        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"[semantic] 에러 {errors.Count}건:");
            foreach (var d in errors)
                Console.Error.WriteLine("  " + d.Format());
            return 3;
        }

        var context = new GeneratorContext
        {
            Models = allModels,
            WorkingDirectory = configDirectory,
            Enums = allEnums,
        };

        // 1.6. 설정 위반 수집 — 전부 모아 한 번에 실패시킨다(한 건씩 고쳐가며 재실행하게 만들지 않는다).
        var configViolations = new List<string>();

        // 같은 종류·같은 경로의 타깃이 둘이면 둘째가 첫째를 **조용히 덮어쓴다**.
        // 같은 종류라도 경로가 다르면 정상(복수 서버 시나리오) — 경로까지 같을 때만 오류다.
        foreach (var dup in cfg.Targets
            .GroupBy(t => (t.Type, Path: ResolveTargetPath(configDirectory, t)))
            .Where(g => g.Count() > 1))
        {
            configViolations.Add(
                $"{dup.Key.Type} 타깃이 같은 경로({dup.Key.Path})에 {dup.Count()}개 있습니다 "
                + "— 나중 것이 앞선 것의 산출물을 덮어씁니다. 경로를 분리하거나 중복을 제거하세요.");
        }

        // Api 타깃이 entity 타입을 참조할 수 있도록 entity namespace를 결정한다.
        // 과거에는 `FirstOrDefault(Model)?.Namespace` 였다 — Model 타깃이 둘이면 둘째의 namespace가
        // **조용히 무시**되고 Api 타깃이 잘못된 `using`을 방출해 소비자 빌드가 깨졌다.
        // 이제: Api 타깃의 명시 `entitiesNamespace`가 최우선, 없으면 Model 타깃이 유일할 때만 추론,
        // 후보가 둘 이상이면 추론하지 않고 **오류로 명시를 요구**한다.
        var modelNamespaces = cfg.Targets
            .Where(t => t.Type == "Model" && !string.IsNullOrWhiteSpace(t.Namespace))
            .Select(t => t.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var api in cfg.Targets.Where(t => t.Type == "Api"
                                                  && string.IsNullOrWhiteSpace(t.EntitiesNamespace)))
        {
            if (modelNamespaces.Count > 1)
            {
                configViolations.Add(
                    $"Api 타깃({TargetPathOf(api)}): Model 타깃의 namespace 후보가 {modelNamespaces.Count}개입니다 "
                    + $"({string.Join(", ", modelNamespaces)}) — 어느 것을 참조할지 추론하지 않습니다. "
                    + "이 Api 타깃에 entitiesNamespace 를 명시하세요.");
            }
        }

        // 방언 정합 검사 — Sql 타깃이 snake DB를 만드는데 Model 타깃이 기본 매핑이면
        // (또는 그 역이면) 런타임 DB 오류가 되어서야 드러난다. 빌드 시점 경고로 표면화.
        var sqlPg = cfg.Targets.Any(t => t.Type == "Sql" && IsPostgresDialect(t));
        foreach (var modelTarget in cfg.Targets.Where(t => t.Type == "Model"))
        {
            if (sqlPg != IsPostgresDialect(modelTarget)
                && cfg.Targets.Any(t => t.Type == "Sql"))
            {
                Console.Error.WriteLine(
                    "[config] 경고: Sql 타깃과 Model 타깃의 dialect가 다릅니다 — 생성 DDL과 " +
                    "EF 매핑이 서로 다른 네이밍을 전제하게 됩니다. 두 타깃에 같은 dialect를 지정하세요.");
            }
        }

        // 1.7. 타깃별 엔티티 부분집합 검증 — 조용한 드롭·조용한 무시 금지:
        // 오타로 표면이 텅 비는 것이 가장 나쁜 실패다.
        var filters = new Dictionary<MddJsonTarget, EntitySurfaceFilter>();
        foreach (var target in cfg.Targets)
        {
            var label = $"{target.Type} 타깃({TargetPathOf(target)})";
            var hasFilter = target.IncludeEntities?.Count > 0 || target.ExcludeEntities?.Count > 0;

            // 표면 타깃 전용 — Sql·Model 에 걸면 FK/상속 무결성이 깨진다. 조용히 무시하지 않는다.
            if (hasFilter && target.Type is not ("Api" or "TypeScript"))
            {
                configViolations.Add(
                    $"{label}: includeEntities/excludeEntities 는 표면 타깃(Api·TypeScript)에만 지정할 수 있습니다 "
                    + "— Sql·Model 을 부분집합으로 만들면 FK/상속 무결성이 깨집니다.");
                continue;
            }

            filters[target] = EntitySurfaceFilter.Validate(
                target.IncludeEntities, target.ExcludeEntities, allModels, label, out var violations);
            configViolations.AddRange(violations);
        }

        // 1.8. TypeScript 타깃의 EntitySetName 이 **어떤 Api 타깃도 등록하지 않는** 셋을 광고하는지.
        // `entity_names_gen.ts` 는 OData entity set 이름의 미러로 계약돼 있고 소비자가 이걸로 URL 을
        // 만든다. 타깃별 필터가 도입되면서 Api 와 TypeScript 의 범위가 어긋날 수 있게 됐는데,
        // 어긋나도 아무 신호가 없다 — @internal 을 목록에서 빼서 없앤 것과 같은 결함 계열이다.
        //
        // 판정은 **전 Api 타깃의 합집합**과 비교한다. 공유 UI 하나가 여러 서버를 담당하는 구성이
        // 정상이기 때문이다(어떤 셋이든 어느 한 서버가 등록하면 그 이름은 유효하다).
        // Api 타깃이 아예 없는 설정에서는 판정 근거가 없으므로 검사하지 않는다.
        // 오류가 아니라 경고다 — 서버가 다른 mdd.json 에 설정돼 있을 수 있다.
        var apiTargets = cfg.Targets.Where(t => t.Type == "Api").ToList();
        if (apiTargets.Count > 0 && configViolations.Count == 0)
        {
            var registered = apiTargets
                .SelectMany(t => (filters.TryGetValue(t, out var af) ? af : EntitySurfaceFilter.PassAll)
                    .Apply(allModels))
                .Where(m => !EntitySurface.IsInternal(m))
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var ts in cfg.Targets.Where(t => t.Type == "TypeScript"))
            {
                var extras = (filters.TryGetValue(ts, out var tf) ? tf : EntitySurfaceFilter.PassAll)
                    .Apply(allModels)
                    .Where(m => !EntitySurface.IsInternal(m) && !registered.Contains(m.Name))
                    .Select(m => m.Name)
                    .ToList();

                if (extras.Count > 0)
                {
                    Console.Error.WriteLine(
                        $"[config] 경고: TypeScript 타깃({TargetPathOf(ts)})의 EntitySetName 이 "
                        + $"어떤 Api 타깃도 등록하지 않는 엔티티 {extras.Count}개를 포함합니다 — "
                        + $"소비자가 그 이름으로 OData URL 을 만들면 존재하지 않는 경로가 됩니다. "
                        + $"이 TypeScript 타깃에 같은 필터를 지정하세요: {string.Join(", ", extras.Take(10))}"
                        + (extras.Count > 10 ? $" … (+{extras.Count - 10})" : ""));
                }
            }
        }

        if (configViolations.Count > 0)
        {
            Console.Error.WriteLine($"[config] 설정 오류 {configViolations.Count}건:");
            foreach (var v in configViolations)
                Console.Error.WriteLine("  " + v);
            return 4;
        }

        // 2. 타깃별 생성기 실행
        foreach (var target in cfg.Targets)
        {
            var filter = filters.TryGetValue(target, out var f) ? f : EntitySurfaceFilter.PassAll;
            // 명시 > 유일 추론 > null. 후보가 둘 이상인 경우는 위에서 이미 오류로 걸렀다.
            var entitiesNamespace = target.EntitiesNamespace
                ?? (modelNamespaces.Count == 1 ? modelNamespaces[0] : null);
            var generator = ResolveGenerator(target, entitiesNamespace, filter);
            var targetPath = TargetPathOf(target);
            Console.WriteLine($"[{generator.Name}] 생성 시작 (target: {targetPath})");
            // 커버리지 회계 — 화이트리스트는 정본에 새 엔티티가 들어와도 조용히 빠지므로
            // 무엇이 제외됐는지 매 빌드에서 보이게 한다.
            if (!filter.IsPassAll)
                Console.WriteLine($"[{generator.Name}] {filter.DescribeCoverage(allModels)}");
            generator.Generate(context);
            Console.WriteLine($"[{generator.Name}] 완료");
        }

        Console.WriteLine("build 완료.");
        return 0;
    }

    /// <summary>로그·오류 메시지에 쓰는 타깃 경로 (TypeScript 는 outputPath, 그 외는 projectPath).</summary>
    private static string TargetPathOf(MddJsonTarget target)
        => !string.IsNullOrEmpty(target.OutputPath) ? target.OutputPath : target.ProjectPath;

    /// <summary>중복 타깃 판정용 정규화 경로. 상대 경로는 mdd.json 위치 기준으로 절대화한다.</summary>
    private static string ResolveTargetPath(string configDirectory, MddJsonTarget target)
    {
        var raw = TargetPathOf(target);
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(configDirectory, raw));
    }

    /// <summary>Model·Sql 타깃 공용 dialect 판정. 미지 값은 각 분기에서 오류.</summary>
    private static bool IsPostgresDialect(MddJsonTarget target)
        => target.Dialect?.ToLowerInvariant() is "postgres" or "postgresql" or "pg";

    /// <summary>
    /// Sql 타깃의 방언 분기. 기본(tsql)은 현행 SSDT/T-SQL 경로 그대로 — 기존 소비자 무영향.
    /// postgres는 SSDT 개념 노브(emitSqlProj/emitRefreshScript)와 양립하지 않으므로
    /// 명시 설정 시 조용히 무시하지 않고 오류로 알린다.
    /// </summary>
    private static IArtifactGenerator CreateSqlGenerator(MddJsonTarget target)
    {
        switch (target.Dialect?.ToLowerInvariant())
        {
            case null or "tsql":
                return new SqlGenerator(new SqlGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Schema = target.Schema ?? "dbo",
                    EmitSqlProj = target.EmitSqlProj ?? true,
                    EmitRefreshScript = target.EmitRefreshScript ?? true,
                    EmitEnumCheckConstraints = target.EmitEnumCheckConstraints ?? false,
                    EmitForeignKeyIndexes = target.EmitForeignKeyIndexes ?? true,
                });

            case "postgres" or "postgresql" or "pg":
                if (target.EmitSqlProj == true)
                {
                    throw new InvalidOperationException(
                        "dialect 'postgres'는 emitSqlProj를 지원하지 않습니다 (.sqlproj는 SSDT/T-SQL 개념) — 옵션을 제거하세요.");
                }
                if (target.EmitRefreshScript == true)
                {
                    throw new InvalidOperationException(
                        "dialect 'postgres'는 emitRefreshScript를 지원하지 않습니다 (sp_refreshview는 T-SQL 개념) — 옵션을 제거하세요.");
                }
                return new MddBooster.Generators.Sql.Postgres.PostgresSqlGenerator(
                    new MddBooster.Generators.Sql.Postgres.PostgresSqlGeneratorOptions
                    {
                        ProjectPath = target.ProjectPath,
                        Schema = target.Schema ?? "public",
                        EmitEnumCheckConstraints = target.EmitEnumCheckConstraints ?? false,
                        EmitForeignKeyIndexes = target.EmitForeignKeyIndexes ?? true,
                    });

            default:
                throw new NotSupportedException(
                    $"지원하지 않는 Sql dialect: '{target.Dialect}' (지원: tsql, postgres)");
        }
    }

    /// <summary>
    /// Applies whichever form-import overrides the target declared, leaving the
    /// rest at the record's defaults.
    /// </summary>
    /// <remarks>
    /// Written as overrides onto a default instance rather than as
    /// <c>value ?? "literal"</c> so the defaults live in exactly one place. A
    /// second copy here would be free to drift from the one the generated files
    /// are actually compared against.
    /// </remarks>
    private static TsFormModuleImports FormModulesFor(MddJsonTarget target)
    {
        var modules = new TsFormModuleImports();

        if (!string.IsNullOrWhiteSpace(target.FormLayoutImport))
            modules = modules with { Layout = target.FormLayoutImport };
        if (!string.IsNullOrWhiteSpace(target.FormControlsImport))
            modules = modules with { Controls = target.FormControlsImport };
        if (!string.IsNullOrWhiteSpace(target.FormSelectOptionsImport))
            modules = modules with { SelectOptions = target.FormSelectOptionsImport };

        return modules;
    }

    private IArtifactGenerator ResolveGenerator(
        MddJsonTarget target, string? modelNamespace, EntitySurfaceFilter surfaceFilter)
    {
        return target.Type switch
        {
            "Sql" => CreateSqlGenerator(target),
            "Model" => new MddBooster.Generators.Model.ModelGenerator(
                new MddBooster.Generators.Model.ModelGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Model target requires 'namespace'."),
                    DbContextName = target.DbContextName
                        ?? throw new InvalidOperationException("Model target requires 'dbContextName'."),
                    SqlProjectPath = target.SqlProjectPath,
                    PostgresNaming = IsPostgresDialect(target),
                }),
            "Api" => new MddBooster.Generators.Api.ApiRegistrationGenerator(
                new MddBooster.Generators.Api.ApiRegistrationGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Api target requires 'namespace'."),
                    EntitiesNamespace = modelNamespace,
                    SurfaceFilter = surfaceFilter,
                }),
            "TypeScript" => new TypeScriptGenerator(
                new TypeScriptGeneratorOptions
                {
                    OutputPath = target.OutputPath
                        ?? throw new InvalidOperationException("TypeScript target requires 'outputPath'."),
                    FormsOutputPath = target.FormsOutputPath,
                    FormModules = FormModulesFor(target),
                    SurfaceFilter = surfaceFilter,
                }),
            _ => throw new NotSupportedException(
                $"지원하지 않는 target type: '{target.Type}' (지원: Sql, Model, Api, TypeScript)"),
        };
    }
}
