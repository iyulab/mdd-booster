using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// Model 타깃 파이프라인 진입 게이트. 생성은 성공하지만 런타임에 파탄나는
/// 모델(조용한 실패)을 생성 시점의 명시적 오류로 끌어올린다.
/// 렌더러 내부 정합 게이트(EntityPairRenderer의 PK 게이트)와 달리, 여기는
/// IyuEntity/IyuDbContext 런타임 계약과의 정합을 검증하는 자리다.
/// </summary>
public static class ModelTargetValidator
{
    /// <summary>
    /// <c>IyuEntity</c>가 항상 매핑하는 감사 타임스탬프.
    /// 타임스탬프 인터셉터가 모든 엔티티에서 CreatedAt/UpdatedAt 속성에 무조건 접근하므로,
    /// 모델이 이 필드들을 선언하지 않으면 존재하지 않는 컬럼 매핑으로 첫 쿼리/저장에서 실패한다.
    /// </summary>
    private static readonly string[] RequiredTimestamps = ["created_at", "updated_at"];

    public static void Validate(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        foreach (var model in models)
        {
            ValidateTimestampContract(model);
        }
    }

    private static void ValidateTimestampContract(ResolvedModel model)
    {
        foreach (var required in RequiredTimestamps)
        {
            var declared = model.Fields.Any(f =>
                f.Kind == FieldKind.Stored &&
                string.Equals(f.Name, required, StringComparison.OrdinalIgnoreCase));
            if (!declared)
            {
                throw new InvalidOperationException(
                    $"Model '{model.Name}': '{required}' 필드가 없습니다. IyuEntity는 CreatedAt/UpdatedAt을 " +
                    "항상 매핑하므로 미선언 모델은 존재하지 않는 컬럼 매핑으로 런타임에 실패합니다. " +
                    "Timestampable 인터페이스를 상속하거나 두 필드를 직접 선언하세요.");
            }
        }
    }
}
