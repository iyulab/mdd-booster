using MddBooster.Core.Generation;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// End-to-end generator that produces five TypeScript files from M3L models:
/// <list type="bullet">
///   <item><c>enums_gen.ts</c> — Union literal types for every M3L enum</item>
///   <item><c>entities_gen.ts</c> — TypeScript interfaces for every M3L entity</item>
///   <item><c>entity_names_gen.ts</c> — <c>ENTITY_NAMES</c> const array + <c>EntitySetName</c> type</item>
///   <item><c>enum_labels_gen.ts</c> — <c>{Enum}Labels</c> const map from enum value to display label</item>
///   <item><c>field_schema_gen.ts</c> — <c>FieldSchema</c> const map of field validation constraints</item>
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

        var outDir = ConfiguredPathResolver.Resolve(context.WorkingDirectory, _options.OutputPath, "outputPath");

        Directory.CreateDirectory(outDir);

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        // 타깃별 방출 범위 — 엔티티 파생 산출물에만 적용한다.
        var models = _options.SurfaceFilter.Apply(context.Models);

        // enums_gen.ts — 필터하지 않는다. enum 은 엔티티 파생물이 아니고, 가지치기하면
        // 남은 인터페이스/폼의 임포트가 깨진다.
        var enumsContent = TsEnumRenderer.RenderAll(context.Enums);
        File.WriteAllText(Path.Combine(outDir, "enums_gen.ts"), enumsContent);

        // entities_gen.ts — @internal 은 존중하지 않는다(엔티티·필드 레벨 둘 다). 타입은
        // 데이터 API 전용이 아니며 전용 엔드포인트로 관리되는 인프라 엔티티에도 타입은 유용하다.
        var entitiesContent = TsInterfaceRenderer.RenderAll(models, enumNames);
        File.WriteAllText(Path.Combine(outDir, "entities_gen.ts"), entitiesContent);

        // entity_names_gen.ts — @internal 을 **존중한다**. 이 목록은 OData entity set 이름의
        // 미러이고 소비자가 이걸로 OData URL 을 만든다. @internal 엔티티는 AddEntityPair 가
        // 등록하지 않으므로, 목록에 남겨 두면 타입세이프하게 404 경로를 광고하는 셈이 된다.
        var namesContent = TsEntityNamesRenderer.RenderAll(
            [.. models.Where(m => !EntitySurface.IsInternal(m))]);
        File.WriteAllText(Path.Combine(outDir, "entity_names_gen.ts"), namesContent);

        // enum_labels_gen.ts — enums_gen.ts 와 같은 이유로 필터하지 않는다.
        var enumLabelsContent = TsEnumLabelsRenderer.RenderAll(context.Enums);
        File.WriteAllText(Path.Combine(outDir, "enum_labels_gen.ts"), enumLabelsContent);

        // field_schema_gen.ts — @internal 필드도 존중하지 않는다, entities_gen.ts 와 같은 이유.
        var fieldSchemaContent = TsFieldSchemaRenderer.RenderAll(models);
        File.WriteAllText(Path.Combine(outDir, "field_schema_gen.ts"), fieldSchemaContent);

        // {Entity}Form_gen.tsx (optional) — @internal 필드도 존중하지 않는다, entities_gen.ts 와
        // 같은 이유. 필드를 폼에서 편집 못 하게 막는 것은 쓰기(Write) 축 관심사이고 @internal은
        // 읽기(Read) 축 어휘라 이 형태로는 표현되지 않는다 — 별개 축, 별개 어휘가 필요하다.
        if (_options.FormsOutputPath is not null)
        {
            var formsDir = ConfiguredPathResolver.Resolve(
                context.WorkingDirectory, _options.FormsOutputPath, "formsOutputPath");

            Directory.CreateDirectory(formsDir);

            // The forms import the five files written above. Both directories are
            // independent user input, so the specifier between them is derived —
            // assuming a layout is what made a wrong pair emit files that this
            // generator reported as written and the consumer's compiler rejected.
            var imports = new TsFormImports
            {
                GeneratedTypesBase = TsModuleSpecifier.RelativeBase(
                    formsDir, outDir, "formsOutputPath", "outputPath"),
                Modules = _options.FormModules,
            };

            var formFiles = TsFormRenderer.RenderAll(models, context.Enums, imports);
            foreach (var (entityName, content) in formFiles)
            {
                File.WriteAllText(Path.Combine(formsDir, $"{entityName}Form_gen.tsx"), content);
            }
        }
    }
}
