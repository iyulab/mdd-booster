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

    /// <summary>
    /// Whether this model's name is expected to begin with its owner's prefix (default <see langword="true"/>).
    /// <see langword="false"/> for an owner the build declares a layer (<c>mdd.json</c> <c>owners.&lt;p&gt;.namePrefix</c>).
    /// </summary>
    public bool NameCarriesPrefix { get; init; } = true;

    /// <summary>
    /// The prefix this model's name carries — its owner (<c># Prefix:</c>), or the empty string when it has none or
    /// its owner's names do not carry it. The one answer the naming rules (<c>MDD017</c>, <c>MDD019</c>) and the
    /// generated <c>::aspect</c> navigation share.
    /// </summary>
    public string NamePrefix => NameCarriesPrefix ? Source.Prefix ?? "" : "";
}
