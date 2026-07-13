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

        // 1. M3L 소스 로드 + 병합
        var loader = new M3lLoader();
        var allModels = new List<ResolvedModel>();
        var allEnums = new List<M3L.Native.EnumNode>();

        foreach (var srcRel in cfg.Sources)
        {
            var srcAbs = Path.GetFullPath(Path.Combine(configDirectory, srcRel));
            Console.WriteLine($"[m3l] 로딩: {srcAbs}");
            var ast = loader.LoadFile(srcAbs);
            var resolver = new InterfaceResolver(ast);
            allModels.AddRange(resolver.ResolveAll());
            allEnums.AddRange(ast.Enums);
        }

        Console.WriteLine($"[m3l] 모델 {allModels.Count}개, enum {allEnums.Count}개 로드됨: {string.Join(", ", allModels.Select(m => m.Name))}");

        // 1.5. 의미 분석
        var diagnostics = new SemanticAnalyzer(allModels, allEnums).Analyze();
        if (diagnostics.Count > 0)
        {
            Console.Error.WriteLine($"[semantic] 에러 {diagnostics.Count}건:");
            foreach (var d in diagnostics)
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
