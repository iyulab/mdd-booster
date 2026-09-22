using M3L.Native;

namespace MddBooster.Core.Semantic;

public sealed class ResolvedModel
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<FieldNode> Fields { get; init; }
    public required ModelNode Source { get; init; }

    /// <summary>
    /// 이 모델이 <c>::aspect(Base)</c> 라면 그 Base 의 이름. 아니면 <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Fields"/> 의 맨 앞에는 이때 <b>합성된 키 필드</b>가 들어 있다
    /// (<see cref="AspectNaming.KeyFieldName"/>). 작성자가 쓴 것이 아니므로 「작성자가 무엇을
    /// 선언했는가」를 묻는 검사는 <see cref="Source"/> 쪽 필드를 본다.
    /// </remarks>
    public string? AspectBase { get; init; }
}
