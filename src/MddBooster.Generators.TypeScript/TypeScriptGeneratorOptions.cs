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
    /// 이 타깃의 엔티티 부분집합 필터 (<c>includeEntities</c>/<c>excludeEntities</c>, <c>includeOwners</c>/<c>excludeOwners</c>).
    /// 기본값은 전량 통과. 엔티티 이름 축은 엔티티 파생 산출물에만 적용된다. 소유자 축이 있으면
    /// <c>enums_gen.ts</c>·<c>enum_labels_gen.ts</c> 도 그 축으로 갈린다(<c>TypeScriptGenerator.PartitionEnums</c>).
    /// </summary>
    public EntitySurfaceFilter SurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;

    /// <summary>
    /// 이 타깃이 선언하지 않고 가져올 공유 타입의 모듈 지정자(<c>sharedTypesImport</c>) — 보통 같은 정본을 내는
    /// 다른 TypeScript 타깃의 출력을 재수출하는 배럴. 지정하면 <c>IyuEntity</c> 와 <b>다른 소유자의 enum</b> 을
    /// 다시 선언하지 않고 그 모듈에서 재수출한다. 소유자 축(<see cref="SurfaceFilter"/>)이 있는 타깃에서만 뜻이
    /// 있다 — «다른 소유자» 를 그것이 정한다.
    /// </summary>
    public string? SharedTypesImport { get; init; }

    /// <summary>
    /// 폼 전용 부분집합 필터 (<c>formsInclude</c>/<c>formsExclude</c>). 기본값은 전량 통과.
    /// <see cref="SurfaceFilter"/>가 이미 좁힌 결과 <b>위에</b> 적용되므로 폼 집합은 언제나
    /// 타깃 집합의 부분집합이다 — 생성 폼이 자기 타입을 <c>entities_gen.ts</c> 에서 임포트하기
    /// 때문에 그 포함관계는 취향이 아니라 정합성이다.
    /// </summary>
    public EntitySurfaceFilter FormsSurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;
}
