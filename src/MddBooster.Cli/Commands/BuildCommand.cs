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

        // Api 타깃이 entity 타입을 참조할 수 있도록 Model 타깃의 namespace를 탐지.
        var modelNamespace = cfg.Targets
            .FirstOrDefault(t => t.Type == "Model")?
            .Namespace;

        // 2. 타깃별 생성기 실행
        foreach (var target in cfg.Targets)
        {
            var generator = ResolveGenerator(target, modelNamespace);
            var targetPath = !string.IsNullOrEmpty(target.OutputPath) ? target.OutputPath : target.ProjectPath;
            Console.WriteLine($"[{generator.Name}] 생성 시작 (target: {targetPath})");
            generator.Generate(context);
            Console.WriteLine($"[{generator.Name}] 완료");
        }

        Console.WriteLine("build 완료.");
        return 0;
    }

    private IArtifactGenerator ResolveGenerator(MddJsonTarget target, string? modelNamespace)
    {
        return target.Type switch
        {
            "Sql" => new SqlGenerator(new SqlGeneratorOptions
            {
                ProjectPath = target.ProjectPath,
                Schema = target.Schema ?? "dbo",
                EmitSqlProj = target.EmitSqlProj ?? true,
                EmitRefreshScript = target.EmitRefreshScript ?? true,
                EmitEnumCheckConstraints = target.EmitEnumCheckConstraints ?? false,
            }),
            "Model" => new MddBooster.Generators.Model.ModelGenerator(
                new MddBooster.Generators.Model.ModelGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Model target requires 'namespace'."),
                    DbContextName = target.DbContextName
                        ?? throw new InvalidOperationException("Model target requires 'dbContextName'."),
                    SqlProjectPath = target.SqlProjectPath,
                }),
            "Api" => new MddBooster.Generators.Api.ApiRegistrationGenerator(
                new MddBooster.Generators.Api.ApiRegistrationGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Api target requires 'namespace'."),
                    EntitiesNamespace = modelNamespace,
                }),
            "TypeScript" => new TypeScriptGenerator(
                new TypeScriptGeneratorOptions
                {
                    OutputPath = target.OutputPath
                        ?? throw new InvalidOperationException("TypeScript target requires 'outputPath'."),
                    FormsOutputPath = target.FormsOutputPath,
                }),
            _ => throw new NotSupportedException(
                $"지원하지 않는 target type: '{target.Type}' (지원: Sql, Model, Api, TypeScript)"),
        };
    }
}
