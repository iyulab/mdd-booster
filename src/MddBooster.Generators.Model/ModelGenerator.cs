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

        // Emit C# enum files first so entity renderers can reference them
        // by bare type name (same namespace).
        foreach (var enumNode in context.Enums)
        {
            var rendered = EnumRenderer.Render(enumNode, _options.Namespace);
            File.WriteAllText(Path.Combine(enumDir, $"{enumNode.Name}.cs"), rendered);
        }

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        foreach (var model in context.Models)
        {
            // Route the Ext class to the matching SQL layer:
            // rollup/computed → _ext view, lookup only → _full view,
            // neither → base table. Mirrors ViewPlanner classification.
            var hasRollupOrComputed = model.Fields.Any(f =>
                f.Kind is FieldKind.Rollup or FieldKind.Computed);
            var hasLookup = model.Fields.Any(f => f.Kind is FieldKind.Lookup);
            var backing = hasRollupOrComputed
                ? EntityPairRenderer.ExtBacking.Ext
                : hasLookup
                    ? EntityPairRenderer.ExtBacking.Full
                    : EntityPairRenderer.ExtBacking.None;
            var rendered = EntityPairRenderer.Render(model, _options.Namespace, enumNames, backing);
            var baseName = model.Name;
            File.WriteAllText(Path.Combine(entityDir, $"I{baseName}.cs"), rendered.Interface);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}.cs"), rendered.Write);
            File.WriteAllText(Path.Combine(entityDir, $"{baseName}Ext.cs"), rendered.Read);
        }

        var dbContext = DbContextRenderer.Render(
            context.Models.ToList(),
            _options.DbContextName,
            _options.Namespace);
        File.WriteAllText(Path.Combine(contextDir, $"{_options.DbContextName}.cs"), dbContext);
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
}

public sealed class ModelGeneratorOptions
{
    public required string ProjectPath { get; init; }
    public required string Namespace { get; init; }
    public required string DbContextName { get; init; }
}
