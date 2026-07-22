using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>
/// 모델 PK 필드 판정의 정본. 공유 PK 1:1 확장 테이블(PK 필드명이 `id`가 아닌 모델 —
/// 예: `asset_id: identifier @pk @reference(Asset)`)을 생성기들이 일관되게 다루도록 한다.
/// </summary>
public static class ModelPrimaryKey
{
    /// <summary>모델의 PK 필드(`@pk`/`@primary`). 없으면 null.</summary>
    public static FieldNode? Find(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Fields.FirstOrDefault(f => Ast.FieldAttributes.Has(f, "pk"));
    }

    /// <summary>PK 필드명이 `id`가 아닌가 (공유/명명 PK — 컬럼 재매핑 대상).</summary>
    public static bool IsNonIdPk(FieldNode pkField)
    {
        ArgumentNullException.ThrowIfNull(pkField);
        return !string.Equals(pkField.Name, "id", StringComparison.OrdinalIgnoreCase);
    }
}
