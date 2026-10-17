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

    /// <summary>The runtime contract gate (<see cref="ModelTargetValidator"/>) — nothing is written.</summary>
    public void Validate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ModelTargetValidator.Validate(context.Models, context.ReadViews, context.Enums, _options.DbContextName);
    }

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var entityDir = Path.Combine(projectRoot, "Entity_gen");
        var contextDir = Path.Combine(projectRoot, "DbContext_gen");
        var enumDir = Path.Combine(projectRoot, "Enum_gen");

        // Before the directories are emptied: a model this target refuses must not cost the
        // consumer the entities it generated last time.
        ModelTargetValidator.Validate(context.Models, context.ReadViews, context.Enums, _options.DbContextName);

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

        // Computed once for the whole set, not per model: the navigation a read type gains on the
        // target side comes from a declaration in some other model, so no model can derive its own.
        var oneToOne = OneToOneRelationships.Describe(context.Models);
        var manyToOne = ManyToOneNavigations.Describe(context.Models, oneToOne);
        var collections = ReverseCollections.Describe(context.Models, oneToOne, manyToOne);

        foreach (var model in context.Models)
        {
            var backing = DetermineExtBacking(model, customExtViewModels);
            var rendered = EntityPairRenderer.Render(
                model, _options.Namespace, enumNames, backing, oneToOne, context.Models, _options.PostgresNaming,
                manyToOne, collections);
            var baseName = model.Name;
            File.WriteAllText(Path.Combine(entityDir, $"I{baseName}.cs"), rendered.Interface);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}.cs"), rendered.Write);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}Ext.cs"), rendered.Read);
        }

        // A standalone view served as a read model: one read entity beside the pairs.
        var viewEnumNames = context.Enums.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var view in context.ReadViews)
        {
            File.WriteAllText(
                Path.Combine(entityDir, $"{NameCasing.ToPascalCase(view.Name)}.cs"),
                ViewEntityRenderer.Render(view, _options.Namespace, viewEnumNames, _options.PostgresNaming));
        }

        var mapping = ModelMappingRenderer.Render(
            context.Models.ToList(),
            _options.DbContextName,
            _options.Namespace,
            customExtViewModels,
            _options.PostgresNaming,
            oneToOne,
            manyToOne,
            context.ReadViews,
            collections);
        // A type's configuration sits beside the type: whichever project compiles the entities compiles their mapping.
        foreach (var configuration in mapping.Configurations)
            File.WriteAllText(Path.Combine(entityDir, $"{configuration.ClassName}.cs"), configuration.Source);
        File.WriteAllText(Path.Combine(contextDir, $"{_options.DbContextName}.cs"), mapping.Context);
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
        if (SoftDelete.IsEnabled(model))
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
