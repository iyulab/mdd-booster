using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Core.Naming;

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
            // PG 방언 Sql 타깃(PostgresSqlGenerator)은 Lookup/Rollup 파생 필드와 soft-delete는
            // 방출하지만 Computed 파생 필드·@indexed Rollup은 아직 방출하지 않는다 — 그 두
            // 경우만 여기서도 경고한다. 체이닝된 Lookup/Rollup이 *다른* 모델의 미지원 파생
            // 컬럼을 거치는 전이 케이스는 이 판정(모델 자신의 필드만 봄)으로는 잡히지 않지만,
            // PostgresSqlGenerator 쪽이 그 경로에서 이미 경고한다.
            foreach (var model in context.Models)
            {
                var unsupported = model.Fields
                    .Where(f => !EntitySurface.IsFieldInternal(f))
                    .Any(f => f.Kind == FieldKind.Computed
                        || (f.Kind == FieldKind.Rollup && FieldAttributes.Has(f, "indexed")));
                if (unsupported)
                {
                    Console.Error.WriteLine(
                        $"[model] 경고: 모델 '{model.Name}'의 Ext 읽기 모델은 Computed 파생 필드 또는 " +
                        "@indexed Rollup을 갖는데 PG 방언은 아직 이를 방출하지 않는다 — 해당 뷰를 직접 " +
                        "만들기 전까지 Ext 질의는 실패한다");
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

        var sqlRoot = ConfiguredPathResolver.Resolve(workingDirectory, _options.SqlProjectPath, "sqlProjectPath");

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
        var pascalName = NameCasing.ToPascalCase(model.Name);
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
        => ConfiguredPathResolver.Resolve(workingDirectory, _options.ProjectPath, "projectPath");

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
