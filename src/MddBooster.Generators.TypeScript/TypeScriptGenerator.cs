using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

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

        // Checked before anything is written: a half-written target is worse than none.
        if (_options.FormsOutputPath is not null && _options.FormModules is null)
        {
            throw new InvalidOperationException(
                "FormsOutputPath is set but FormModules is not — a generated form imports its "
                + "layout, controls and option helper from modules this generator does not write, "
                + "so the caller must name all three.");
        }

        var outDir = ConfiguredPathResolver.Resolve(context.WorkingDirectory, _options.OutputPath, "outputPath");

        Directory.CreateDirectory(outDir);

        var enumNames = new HashSet<string>(context.Enums.Select(e => e.Name), StringComparer.Ordinal);

        // 타깃별 방출 범위 — 엔티티 파생 산출물에만 적용한다.
        var models = _options.SurfaceFilter.Apply(context.GeneratedModels);
        // 읽기 모델로 서빙되는 뷰 — 같은 필터(이름 · 소유자). 읽기 타입과 읽기 전용 셋 이름만 낸다:
        // 필드 스키마와 폼은 쓰기 축이고, 뷰에는 쓰기가 없다.
        var readViews = context.GeneratedReadViews.Where(_options.SurfaceFilter.Admits).ToList();

        // enums_gen.ts — 엔티티 필터(이름 목록)로는 가지치지 않는다: enum 은 엔티티 파생물이 아니고, 가지치면
        // 소비자가 그 파일에서 가져오던 enum 이 사라진다. 소유자 축이 있으면 enum 도 그 축으로 가른다(PartitionEnums).
        var (declaredEnums, sharedEnums) = PartitionEnums(context.Enums, models, enumNames);
        var enumsContent = TsEnumRenderer.RenderAll(declaredEnums, sharedEnums, _options.SharedTypesImport);
        File.WriteAllText(Path.Combine(outDir, "enums_gen.ts"), enumsContent);

        // entities_gen.ts — 두 결의 @internal 을 다르게 다룬다. 필드 단위는 **존중한다**: 이 인터페이스는
        // OData wire 모양의 미러인데, 그 칸은 읽기 타입에 없어 응답에도 쓰기 본문에도 실리지 않는다 —
        // 남겨 두면 어떤 응답도 싣지 않는 값을 약속한다. 엔티티 단위는 존중하지 않는다: 데이터 API 셋이
        // 아예 없는 엔티티의 타입은 OData 미러가 아니라 전용 엔드포인트가 쓰는 모양일 수 있다.
        var entitiesContent = TsInterfaceRenderer.RenderAll(models, enumNames, _options.SharedTypesImport, readViews);
        File.WriteAllText(Path.Combine(outDir, "entities_gen.ts"), entitiesContent);

        // entity_names_gen.ts — @internal 을 **존중한다**. 이 목록은 OData entity set 이름의
        // 미러이고 소비자가 이걸로 OData URL 을 만든다. @internal 엔티티는 AddEntityPair 가
        // 등록하지 않으므로, 목록에 남겨 두면 타입세이프하게 404 경로를 광고하는 셈이 된다.
        var namesContent = TsEntityNamesRenderer.RenderAll(
            [.. models.Where(m => !EntitySurface.IsInternal(m))], readViews);
        File.WriteAllText(Path.Combine(outDir, "entity_names_gen.ts"), namesContent);

        // enum_labels_gen.ts — enums_gen.ts 와 같은 분할.
        var enumLabelsContent = TsEnumLabelsRenderer.RenderAll(declaredEnums, sharedEnums, _options.SharedTypesImport);
        File.WriteAllText(Path.Combine(outDir, "enum_labels_gen.ts"), enumLabelsContent);

        // field_schema_gen.ts — entities_gen.ts 와 같은 규칙(필드 단위 @internal 제외).
        var fieldSchemaContent = TsFieldSchemaRenderer.RenderAll(models);
        File.WriteAllText(Path.Combine(outDir, "field_schema_gen.ts"), fieldSchemaContent);

        // {Entity}Form_gen.tsx (optional) — 필드 단위 @internal 칸을 내지 않는다: 폼은 OData 쓰기 본문을
        // 만드는데, 읽기 타입에 없는 칸은 EDM 에도 없어 서버가 모르는 속성으로 거절한다. «저장 칸을 폼에서
        // 읽기 전용으로»는 별개의 쓰기 축(@immutable·@generated 등)이 맡는다.
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
                Modules = _options.FormModules!,
            };

            // 폼 전용 입도 — 이미 타깃 필터를 통과한 `models` 위에 얹는다. 그래서 폼 집합이
            // 타깃 집합의 부분집합이라는 것이 별도 검사 없이 성립한다(임포트 정합성).
            var formModels = _options.FormsSurfaceFilter.Apply(models);

            var formFiles = TsFormRenderer.RenderAll(formModels, context.Enums, imports);
            // The file system's own rule: on a case-insensitive one, a form renamed only in case is
            // written into the existing file, which keeps its old spelling — comparing ordinally
            // would then delete the form just written.
            var written = new HashSet<string>(
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
            foreach (var (entityName, content) in formFiles)
            {
                var fileName = $"{entityName}Form_gen.tsx";
                File.WriteAllText(Path.Combine(formsDir, fileName), content);
                written.Add(fileName);
            }

            RemovedStaleForms = RemoveStaleForms(formsDir, written);
            if (RemovedStaleForms.Count > 0)
                Console.WriteLine(
                    $"[{Name}] 이번 빌드가 내지 않은 생성 폼 {RemovedStaleForms.Count}개 삭제: "
                    + string.Join(", ", RemovedStaleForms));
        }
    }

    /// <summary>
    /// Which enums this target declares, and which it re-exports from the shared source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without an owner filter nothing changes: every enum is declared, as before. With one, an enum
    /// is this target's own when its file's owner passes the filter, like a model's; an enum of
    /// another owner is carried only when one of this target's models references it — a module
    /// target no longer repeats the whole base vocabulary, and a base target no longer carries a
    /// module's enums that none of its models use.
    /// </para>
    /// <para>
    /// A referenced enum of another owner is declared here unless the target names a shared source,
    /// in which case it is re-exported from there — one declaration of each type across the targets.
    /// The files keep their names and exports either way, so generated forms import from them
    /// unchanged.
    /// </para>
    /// </remarks>
    private (IReadOnlyList<EnumNode> Declared, IReadOnlyList<EnumNode> Shared) PartitionEnums(
        IReadOnlyList<EnumNode> all, IReadOnlyList<ResolvedModel> models, IReadOnlySet<string> enumNames)
    {
        var filter = _options.SurfaceFilter;
        if (!filter.HasOwnerFilter) return (all, []);

        var referenced = new HashSet<string>(
            models.SelectMany(m => m.Fields)
                .Select(f => f.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t) && enumNames.Contains(t!))
                .Select(t => t!),
            StringComparer.Ordinal);

        var own = all.Where(e => filter.AdmitsOwner(e.Prefix ?? "")).ToList();
        var foreign = all.Where(e => !filter.AdmitsOwner(e.Prefix ?? "") && referenced.Contains(e.Name)).ToList();

        return _options.SharedTypesImport is null
            ? ([.. all.Where(e => own.Contains(e) || foreign.Contains(e))], [])
            : (own, foreign);
    }

    /// <summary>
    /// Forms the last <see cref="Generate"/> deleted because this build no longer emits them —
    /// an entity renamed, removed, or filtered out of the target since the previous build.
    /// </summary>
    public IReadOnlyList<string> RemovedStaleForms { get; private set; } = [];

    /// <summary>
    /// Deletes the forms in <paramref name="formsDir"/> this generator wrote on an earlier build
    /// but did not write on this one.
    /// </summary>
    /// <remarks>
    /// The other targets own a dedicated directory and empty it before writing. The forms directory
    /// is the consumer's own source tree, so it cannot be emptied: a file is removed only when its
    /// name has the form shape and its first line is the header this generator writes. A hand-written
    /// file keeps its place whatever it is called. Without this a renamed entity leaves its old form
    /// behind — a file marked generated that no build produces, importing a type that no longer
    /// exists, and invisible to a regenerate-and-diff check because regeneration never touches it.
    /// </remarks>
    private static IReadOnlyList<string> RemoveStaleForms(string formsDir, IReadOnlySet<string> written)
    {
        var removed = new List<string>();
        foreach (var path in Directory.GetFiles(formsDir, "*Form_gen.tsx"))
        {
            var fileName = Path.GetFileName(path);
            if (written.Contains(fileName) || !StartsWithGeneratedHeader(path)) continue;
            File.Delete(path);
            removed.Add(fileName);
        }
        removed.Sort(StringComparer.Ordinal);
        return removed;
    }

    private static bool StartsWithGeneratedHeader(string path)
    {
        using var reader = new StreamReader(path);
        return string.Equals(reader.ReadLine(), TsFormRenderer.Header, StringComparison.Ordinal);
    }
}
