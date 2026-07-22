using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// End-to-end generator that produces the C# entity pair files and the
/// partial DbContext file for a set of resolved models. Writes outputs into
/// <c>Entity_gen/</c> and <c>DbContext_gen/</c> subfolders beneath the
/// configured project root.
/// </summary>
public sealed class ModelGenerator(ModelGeneratorOptions options) : IArtifactGenerator
{
    private readonly ModelGeneratorOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string Name => "model";

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var entityDir = Path.Combine(projectRoot, "Entity_gen");
        var contextDir = Path.Combine(projectRoot, "DbContext_gen");
        var enumDir = Path.Combine(projectRoot, "Enum_gen");

        CleanDir(entityDir);
        CleanDir(contextDir);
        CleanDir(enumDir);

        foreach (var enumNode in context.Enums)
        {
            var rendered = EnumRenderer.Render(enumNode, _options.Namespace);
            File.WriteAllText(Path.Combine(enumDir, $"{enumNode.Name}.cs"), rendered);
        }

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        // Scan dbo/Views/ in the SQL project for user-maintained {Name}ExtView.sql files.
        var customExtViewModels = ScanCustomExtViews(context.WorkingDirectory);

        // 런타임 계약 게이트 — 생성은 되지만 런타임에 파탄나는 모델을 여기서 명시적으로 거른다.
        ModelTargetValidator.Validate(context.Models);

        foreach (var model in context.Models)
        {
            var backing = DetermineExtBacking(model, customExtViewModels);
            var rendered = EntityPairRenderer.Render(model, _options.Namespace, enumNames, backing);
            var baseName = model.Name;
            File.WriteAllText(Path.Combine(entityDir, $"I{baseName}.cs"), rendered.Interface);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}.cs"), rendered.Write);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}Ext.cs"), rendered.Read);
        }

        if (_options.PostgresNaming)
        {
            // PG 방언은 뷰를 방출하지 않는다 — 뷰 backing이 필요한 Ext 읽기 모델은
            // 존재하지 않는 뷰에 매핑되므로 무음 함정이 되기 전에 경고로 표면화한다.
            foreach (var model in context.Models)
            {
                if (DetermineExtBacking(model, customExtViewModels) != EntityPairRenderer.ExtBacking.None)
                {
                    Console.Error.WriteLine(
                        $"[model] 경고: 모델 '{model.Name}'의 Ext 읽기 모델은 뷰 backing이 필요한데 " +
                        "PG 방언은 뷰를 방출하지 않는다 (Schemorph P3 이후) — 해당 뷰를 직접 만들기 전까지 Ext 질의는 실패한다");
                }
            }
        }

        var dbContext = DbContextRenderer.Render(
            context.Models.ToList(),
            _options.DbContextName,
            _options.Namespace,
            customExtViewModels,
            _options.PostgresNaming);
        File.WriteAllText(Path.Combine(contextDir, $"{_options.DbContextName}.cs"), dbContext);
    }

    private HashSet<string> ScanCustomExtViews(string workingDirectory)
    {
        if (string.IsNullOrEmpty(_options.SqlProjectPath))
            return [];

        var sqlRoot = Path.IsPathRooted(_options.SqlProjectPath)
            ? Path.GetFullPath(_options.SqlProjectPath)
            : Path.GetFullPath(Path.Combine(workingDirectory, _options.SqlProjectPath));

        var viewsDir = Path.Combine(sqlRoot, "dbo", "Views");
        if (!Directory.Exists(viewsDir))
            return [];

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(viewsDir, "*ExtView.sql"))
        {
            var stem = Path.GetFileNameWithoutExtension(file); // e.g. "OrderExtView"
            if (stem.EndsWith("ExtView", StringComparison.Ordinal))
                result.Add(stem[..^"ExtView".Length]); // "Order"
        }
        return result;
    }

    private static EntityPairRenderer.ExtBacking DetermineExtBacking(
        ResolvedModel model,
        HashSet<string> customExtViewModels)
    {
        var pascalName = PascalCase(model.Name);
        if (customExtViewModels.Contains(pascalName))
            return EntityPairRenderer.ExtBacking.Ext;
        if (model.Fields.Any(f => f.Kind is FieldKind.Lookup or FieldKind.Rollup or FieldKind.Computed))
            return EntityPairRenderer.ExtBacking.Full;
        if (model.Fields.Any(f => f.Kind == FieldKind.Stored &&
            string.Equals(f.Name, "deleted_at", StringComparison.Ordinal)))
            return EntityPairRenderer.ExtBacking.Ud;
        return EntityPairRenderer.ExtBacking.None;
    }

    private string ResolveProjectRoot(string workingDirectory)
    {
        if (Path.IsPathRooted(_options.ProjectPath))
            return Path.GetFullPath(_options.ProjectPath);
        return Path.GetFullPath(Path.Combine(workingDirectory, _options.ProjectPath));
    }

    private static void CleanDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.cs"))
                File.Delete(f);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string PascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}

public sealed class ModelGeneratorOptions
{
    public required string ProjectPath { get; init; }
    public required string Namespace { get; init; }
    public required string DbContextName { get; init; }

    /// <summary>
    /// Optional path to the SSDT SQL project root. When provided, the generator
    /// scans <c>dbo/Views/</c> for <c>{Name}ExtView.sql</c> files to determine
    /// which models have a user-maintained ExtView (highest priority backing).
    /// </summary>
    public string? SqlProjectPath { get; init; }

    /// <summary>
    /// PG 방언 명시 매핑(ADR-0001 §2.3): DbContext에 <c>ToTable</c>(snake)/
    /// <c>HasColumnName</c>(M3L 필드명)/json→<c>HasColumnType("jsonb")</c>를 굽는다.
    /// 기본 false — 현행(T-SQL Pascal 렌더와 일치하는 무매핑) 유지.
    /// </summary>
    public bool PostgresNaming { get; init; }
}
