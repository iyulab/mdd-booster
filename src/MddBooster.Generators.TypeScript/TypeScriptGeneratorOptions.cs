using MddBooster.Core.Generation;

namespace MddBooster.Generators.TypeScript;

public sealed class TypeScriptGeneratorOptions
{
    /// <summary>
    /// Absolute or relative path to the output directory for the five
    /// standard TypeScript files (entities_gen.ts, enums_gen.ts, etc.).
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Optional path for generated {Entity}Form_gen.tsx files.
    /// When null, form generation is skipped.
    /// </summary>
    public string? FormsOutputPath { get; init; }

    /// <summary>
    /// Where generated forms import the modules this generator does not write.
    /// Defaults reproduce the historical output, so leaving this unset keeps
    /// existing consumers byte-identical.
    /// </summary>
    /// <remarks>
    /// Deliberately does not cover the generator's own output — that specifier
    /// is derived from <see cref="OutputPath"/> and <see cref="FormsOutputPath"/>,
    /// because this class already knows it. See <see cref="TsFormImports"/>.
    /// </remarks>
    public TsFormModuleImports FormModules { get; init; } = new();

    /// <summary>
    /// 이 타깃의 엔티티 부분집합 필터 (<c>includeEntities</c>/<c>excludeEntities</c>).
    /// 기본값은 전량 통과. 엔티티 파생 산출물에만 적용되고 <c>enums_gen.ts</c>·
    /// <c>enum_labels_gen.ts</c>는 필터하지 않는다 — 가지치기하면 임포트가 깨진다.
    /// </summary>
    public EntitySurfaceFilter SurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;

    /// <summary>
    /// 폼 전용 부분집합 필터 (<c>formsInclude</c>/<c>formsExclude</c>). 기본값은 전량 통과.
    /// <see cref="SurfaceFilter"/>가 이미 좁힌 결과 <b>위에</b> 적용되므로 폼 집합은 언제나
    /// 타깃 집합의 부분집합이다 — 생성 폼이 자기 타입을 <c>entities_gen.ts</c> 에서 임포트하기
    /// 때문에 그 포함관계는 취향이 아니라 정합성이다.
    /// </summary>
    public EntitySurfaceFilter FormsSurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;
}
