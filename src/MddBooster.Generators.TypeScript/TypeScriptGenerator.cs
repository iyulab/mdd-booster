using MddBooster.Core.Generation;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// End-to-end generator that produces four TypeScript files from M3L models:
/// <list type="bullet">
///   <item><c>enums_gen.ts</c> — Union literal types for every M3L enum</item>
///   <item><c>entities_gen.ts</c> — TypeScript interfaces for every M3L entity</item>
///   <item><c>entity_names_gen.ts</c> — <c>ENTITY_NAMES</c> const array + <c>EntitySetName</c> type</item>
///   <item><c>enum_labels_gen.ts</c> — <c>{Enum}Labels</c> const map from enum value to display label</item>
/// </list>
/// </summary>
public sealed class TypeScriptGenerator(TypeScriptGeneratorOptions options) : IArtifactGenerator
{
    private readonly TypeScriptGeneratorOptions _options = options
        ?? throw new ArgumentNullException(nameof(options));

    public string Name => "ts";

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outDir = Path.IsPathRooted(_options.OutputPath)
            ? Path.GetFullPath(_options.OutputPath)
            : Path.GetFullPath(Path.Combine(context.WorkingDirectory, _options.OutputPath));

        Directory.CreateDirectory(outDir);

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        // enums_gen.ts
        var enumsContent = TsEnumRenderer.RenderAll(context.Enums);
        File.WriteAllText(Path.Combine(outDir, "enums_gen.ts"), enumsContent);

        // entities_gen.ts
        var entitiesContent = TsInterfaceRenderer.RenderAll(context.Models, enumNames);
        File.WriteAllText(Path.Combine(outDir, "entities_gen.ts"), entitiesContent);

        // entity_names_gen.ts
        var namesContent = TsEntityNamesRenderer.RenderAll(context.Models);
        File.WriteAllText(Path.Combine(outDir, "entity_names_gen.ts"), namesContent);

        // enum_labels_gen.ts
        var enumLabelsContent = TsEnumLabelsRenderer.RenderAll(context.Enums);
        File.WriteAllText(Path.Combine(outDir, "enum_labels_gen.ts"), enumLabelsContent);
    }
}
