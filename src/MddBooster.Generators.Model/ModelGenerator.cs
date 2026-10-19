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
        ValidateAll(context);
    }

    private ModelModules? ValidateAll(GeneratorContext context)
    {
        ModelTargetValidator.Validate(context.Models, context.ReadViews, context.Enums, _options.DbContextName);
        if (_options.Modules is not { } modules) return null;
        // The referenced declarations are in no module — they are the layer below every module.
        var assigned = ModelModules.Assign(modules, context.GeneratedModels, context.GeneratedEnums, context.GeneratedReadViews);
        ModuleDirection.Validate(assigned, context.References, context.Models, context.Enums, context.ReadViews);
        ValidateDataInterfaceNames(assigned, context);
        return assigned;
    }

    /// <summary>A data interface's name must not be one a generated type already has — the two would be one name twice.</summary>
    private void ValidateDataInterfaceNames(ModelModules modules, GeneratorContext context)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { _options.DbContextName };
        foreach (var model in context.Models)
        {
            var name = NameCasing.ToPascalCase(model.Name);
            taken.UnionWith([name, "I" + name, name + "Ext"]);
        }
        foreach (var view in context.ReadViews) taken.Add(NameCasing.ToPascalCase(view.Name));
        foreach (var e in context.Enums) taken.Add(e.Name);
        taken.UnionWith(EntityConfigurationRenderer.ConfiguredTypes(context.Models, context.ReadViews).Select(EntityConfiguration.ConfigurationClassName));

        var clashes = modules.Modules
            .Select((m, i) => (m.DataInterface, i))
            .Where(x => x.DataInterface is { } n && taken.Contains(n))
            .Select(x => $"modules[{x.i}]: dataInterface '{x.DataInterface}' 는 생성되는 형의 이름입니다 — 다른 이름을 고르세요.")
            .ToList();
        if (clashes.Count > 0)
            throw new GeneratorRefusalException(GeneratorRefusalKind.Configuration, "Model 타깃의 modules:\n  " + string.Join("\n  ", clashes));
    }

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var contextDir = Path.Combine(projectRoot, "DbContext_gen");

        // Before the directories are emptied: a model this target refuses must not cost the
        // consumer the entities it generated last time.
        var modules = ValidateAll(context);

        // Where each declaration's files go. One project unless the target is split into modules; the target's own
        // Entity_gen/Enum_gen are emptied either way — a target that became modular must not keep its old entities.
        // Every file may name a referenced type, so every file imports the references' namespaces.
        var referenced = context.References.Namespaces;
        var placements = (modules?.Modules ?? []).Select((m, i) => new Placement(
            Path.Combine(ConfiguredPathResolver.Resolve(context.WorkingDirectory, m.ProjectPath, $"modules[{i}].projectPath"), "Entity_gen"),
            Path.Combine(ConfiguredPathResolver.Resolve(context.WorkingDirectory, m.ProjectPath, $"modules[{i}].projectPath"), "Enum_gen"),
            m.Namespace,
            [.. referenced, .. modules!.ImportsOf(i)])).ToList();
        var single = new Placement(Path.Combine(projectRoot, "Entity_gen"), Path.Combine(projectRoot, "Enum_gen"), _options.Namespace, referenced);
        Placement Of(int? module) => module is { } i ? placements[i] : single;

        CleanDir(single.EntityDir);
        CleanDir(single.EnumDir);
        CleanDir(contextDir);
        foreach (var p in placements)
        {
            CleanDir(p.EntityDir);
            CleanDir(p.EnumDir);
        }

        foreach (var enumNode in context.GeneratedEnums)
        {
            var at = Of(modules?.IndexOf(enumNode));
            var rendered = EnumRenderer.Render(enumNode, _options.Namespace);
            File.WriteAllText(Path.Combine(at.EnumDir, $"{enumNode.Name}.cs"), at.Place(rendered));
        }

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        // Scan dbo/Views/ in the SQL project for user-maintained {Name}ExtView.sql files.
        var customExtViewModels = ScanCustomExtViews(context.WorkingDirectory);

        // Computed once for the whole set, not per model: the navigation a read type gains on the
        // target side comes from a declaration in some other model, so no model can derive its own.
        // A lower layer (an earlier module, a reference) does not get the inverse of a one-to-one from a higher one.
        var layers = modules is not null ? ModuleDirection.For(modules, context.References)
            : context.References.IsEmpty ? null : LayerDirection.Of(context.References);
        var oneToOne = OneToOneRelationships.Describe(context.Models, layers is null ? null : layers.HasInverse);
        var manyToOne = ManyToOneNavigations.Describe(context.Models, oneToOne);
        var collections = ReverseCollections.Describe(context.Models, oneToOne, manyToOne);

        foreach (var model in context.GeneratedModels)
        {
            var backing = DetermineExtBacking(model, customExtViewModels);
            var rendered = EntityPairRenderer.Render(
                model, _options.Namespace, enumNames, backing, oneToOne, context.Models, _options.PostgresNaming,
                manyToOne, collections);
            var baseName = model.Name;
            var at = Of(modules?.IndexOf(model));
            File.WriteAllText(Path.Combine(at.EntityDir, $"I{baseName}.cs"), at.Place(rendered.Interface));
            File.WriteAllText(Path.Combine(at.EntityDir, $"{baseName}.cs"), at.Place(rendered.Write));
            File.WriteAllText(Path.Combine(at.EntityDir, $"{baseName}Ext.cs"), at.Place(rendered.Read));
        }

        // A standalone view served as a read model: one read entity beside the pairs.
        var viewEnumNames = context.Enums.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var view in context.GeneratedReadViews)
        {
            var at = Of(modules?.IndexOf(view));
            File.WriteAllText(
                Path.Combine(at.EntityDir, $"{NameCasing.ToPascalCase(view.Name)}.cs"),
                at.Place(ViewEntityRenderer.Render(view, _options.Namespace, viewEnumNames, _options.PostgresNaming)));
        }

        // The layers' data interfaces, lowest first. A declared one holds the sets of its own module and of the modules
        // since the declared one beneath it (an undeclared module's types belong to the next interface up); the lowest
        // also holds the referenced types' — the layer below every module — and the context's own surface. Each extends
        // the one beneath, and the context implements the topmost, so a layer's code never needs the context's type.
        string? topInterface = null;
        if (modules is not null)
        {
            var referencedModels = context.Models.Where(context.References.Contains).ToList();
            var referencedViews = context.ReadViews.Where(context.References.Contains).ToList();
            var from = 0;
            for (var i = 0; i < modules.Modules.Count; i++)
            {
                if (modules.Modules[i].DataInterface is not { } name) continue;
                var layer = Enumerable.Range(from, i - from + 1).ToHashSet();
                var layerModels = context.GeneratedModels.Where(m => layer.Contains(modules.IndexOf(m)));
                var layerViews = context.GeneratedReadViews.Where(v => layer.Contains(modules.IndexOf(v)));
                if (topInterface is null)
                {
                    layerModels = referencedModels.Concat(layerModels);
                    layerViews = referencedViews.Concat(layerViews);
                }
                var sets = DbContextRenderer.Sets(layerModels, layerViews).ToList();
                var source = DataInterfaceRenderer.Render(name, _options.Namespace, sets, topInterface);
                File.WriteAllText(Path.Combine(placements[i].EntityDir, $"{name}.cs"), placements[i].Place(source));
                topInterface = name;
                from = i + 1;
            }
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
            collections,
            topInterface);
        // A type's configuration sits beside the type: whichever project compiles the entities compiles their mapping.
        // A referenced type's configuration is in the referenced assembly — the context applies it, nothing writes it.
        var moduleOfType = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var model in context.GeneratedModels)
        {
            var name = NameCasing.ToPascalCase(model.Name);
            moduleOfType[name] = moduleOfType[name + "Ext"] = modules?.IndexOf(model);
        }
        foreach (var view in context.GeneratedReadViews)
            moduleOfType[NameCasing.ToPascalCase(view.Name)] = modules?.IndexOf(view);
        foreach (var configuration in mapping.Configurations)
        {
            if (!moduleOfType.TryGetValue(configuration.EntityType, out var module)) continue;
            var at = Of(module);
            File.WriteAllText(Path.Combine(at.EntityDir, $"{configuration.ClassName}.cs"), at.Place(configuration.Source));
        }
        // The context serves every module and every referenced type, so it imports all their namespaces.
        var contextImports = referenced.Concat(placements.Select(p => p.Namespace)).Distinct(StringComparer.Ordinal).ToList();
        File.WriteAllText(
            Path.Combine(contextDir, $"{_options.DbContextName}.cs"),
            GeneratedFilePlacement.Place(mapping.Context, _options.Namespace, contextImports));
    }

    /// <summary>Where one module's (or the single project's) files go, and the namespace they are placed in.</summary>
    private sealed record Placement(string EntityDir, string EnumDir, string Namespace, IReadOnlyList<string> Imports)
    {
        public string Place(string source) => GeneratedFilePlacement.Place(source, Namespace, Imports);
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

    /// <summary>
    /// The modules the entities are split into, lowest layer first (<see cref="ModelModules"/>). Null: one project —
    /// <see cref="ProjectPath"/> and <see cref="Namespace"/> hold everything. Set: those two hold the context only.
    /// </summary>
    public IReadOnlyList<ModelModule>? Modules { get; init; }
}
