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
    /// 이 타깃의 엔티티 부분집합 필터 (<c>includeEntities</c>/<c>excludeEntities</c>).
    /// 기본값은 전량 통과. 엔티티 파생 산출물에만 적용되고 <c>enums_gen.ts</c>·
    /// <c>enum_labels_gen.ts</c>는 필터하지 않는다 — 가지치기하면 임포트가 깨진다.
    /// </summary>
    public EntitySurfaceFilter SurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;
}
