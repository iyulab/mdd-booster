using MddBooster.Core.Naming;

namespace MddBooster.Core.Semantic;

/// <summary>
/// <c>::aspect</c> 이름 규칙의 정본. base 쪽에 생기는 navigation 의 이름이 여기서 하나로
/// 정해진다.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModelPrimaryKey"/> 와 같은 자리에 있는 이유도 같다 — 이 답을 필요로 하는 곳이
/// 하나가 아니다. 진단(이름 약속 경고·충돌 오류) · EF 구성 · 테이블 주석 · API 표면이 전부
/// 같은 이름을 말해야 하고, 각자 계산하면 어긋나는 순간 그것을 말해 주는 것이 없다.
/// </para>
/// <para>
/// 규칙: 모델명이 <c>&lt;Base&gt;</c> 또는 <c>&lt;Prefix&gt;&lt;Base&gt;</c> 로 시작하면 그
/// 뒤의 나머지가 navigation 이름이고 prefix 는 보존된다. 그렇지 않으면 <b>모델명 전체</b>다 —
/// 줄일 근거가 없는 이름을 임의로 줄이면 서로 다른 모델이 같은 이름으로 무너진다.
/// </para>
/// </remarks>
public static class AspectNaming
{
    /// <summary>
    /// base 쪽 navigation 이름과, 그것이 이름 약속에서 나온 것인지.
    /// </summary>
    /// <remarks>
    /// 🔴 이름 «자체»는 <see cref="Generation.OneToOneRelationships.ReverseNameFor"/> 가 정한다 —
    /// 여기서 다시 계산하지 않는다. 같은 질문을 진단과 렌더러가 각자 답하면 어느 한쪽만 바뀌는
    /// 날 어긋나고, 그것을 말해 주는 것이 없다(이 메서드는 실제로 한동안 그 두 번째 구현이었다).
    /// 여기가 더하는 것은 <b>약속에서 나온 이름인가</b> 하나뿐이고, 그것도 따로 계산하지 않는다:
    /// 줄일 것이 없으면 모델명이 그대로 돌아오므로 <b>결과가 모델명과 같다</b>는 사실이 곧
    /// 약속에 맞지 않았다는 뜻이다.
    /// </remarks>
    public static (string Name, bool MatchesConvention) ReverseNavigation(ResolvedModel model, string baseName)
    {
        var name = Generation.OneToOneRelationships.ReverseNameFor(model.Name, baseName, PascalPrefix(model));
        return (name, !string.Equals(name, model.Name, StringComparison.Ordinal));
    }

    /// <summary>파일이 선언한 <c># Prefix:</c> 를 PascalCase 로. 없으면 빈 문자열.</summary>
    public static string PascalPrefix(ResolvedModel model)
        => model.Source.Prefix is { Length: > 0 } p ? NameCasing.ToPascalCase(p) : "";

    /// <summary>
    /// <c>::aspect(Base)</c> 가 만드는 키 필드의 이름 — <c>&lt;base&gt;_id</c>.
    /// </summary>
    /// <remarks>
    /// 이 생성기의 필드명은 snake 이고 물리 컬럼명은 방언이 정한다(T-SQL 은 PascalCase, PG 는
    /// 그대로). 작성자가 쓰지 않은 필드이므로 이름을 <b>한 곳에서</b> 정해야 한다 — 합성하는
    /// 쪽과 「이것이 그 키인가」를 묻는 쪽이 각자 만들면 어긋나는 순간 FK 가 조용히 사라진다.
    /// </remarks>
    public static string KeyFieldName(string baseName)
        => PostgresIdentifiers.ToSnakeCase(baseName) + "_id";
}
