using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// M3L 의 «관계 표기» 필드를 식별한다 — 이 생성기가 읽지 않는 형태다.
/// </summary>
/// <remarks>
/// <para>
/// M3L 명세는 관계를 선언하는 방법을 여럿 제시한다: 필드 수준 표기(<c>- &gt;category</c> ·
/// <c>- &lt;posts: one-to-many</c> · <c>- &lt;&gt;tags: many-to-many</c>, 명세 §3.2.2·§3.2.4),
/// <c>### Relations</c> 섹션(§3.2.3), 그리고 <c>@reference(Target)</c> 속성.
/// <b>이 생성기가 읽는 것은 <c>@reference(Target)</c> 하나뿐이다</b> — 외래 키·내비게이션
/// 속성·lookup 리다이렉트가 전부 그것에서 나온다. 이 사실은
/// <c>docs/M3L.md</c> §3.2 말미의 구현 경계 노트가 정본으로 적고 있다.
/// </para>
/// <para>
/// <b>왜 이 판별자가 필요한가</b>(2026-09-09): 파서는 이 표기를 관계로 모델링하지 않고
/// 줄 전체를 «필드 이름»으로 남긴다(<c>name = "&lt;&gt;tags: many-to-many"</c>,
/// <c>type = null</c>). 그대로 흘리면 렌더러가 <i>「필드 '…'에 타입이 없습니다」</i>로
/// 죽는다 — 문서는 경계를 정확히 알고 있는데 <b>진단은 다른 층위를 가리킨다</b>.
/// 명세대로 쓴 사람이 「관계는 <c>@reference</c> 로만 읽는다」는 한마디를 못 듣는다.
/// ⇒ 여기서 식별해 <see cref="AstAccounting"/> 이 경고로 가시화하고, 해석 단계에서
/// 떨군다. <c>### Relations</c> 섹션이 이미 받는 대우와 같다.
/// </para>
/// <para>
/// 판별은 «타입 없는 필드 중 이름이 관계 기호로 시작하는 것»으로 좁힌다. 타입이 있는
/// 필드는 관계 표기가 아니고, 관계 기호로 시작하지 않는 타입-없는 필드는 진짜 결함이라
/// 종전대로 렌더러의 가드가 잡아야 한다 — 이 판별자가 그 가드를 삼키면 안 된다.
/// </para>
/// </remarks>
public static class M3lRelationNotation
{
    /// <summary>이 생성기가 관계를 읽는 유일한 형태.</summary>
    public const string ReadForm = "@reference(Target)";

    /// <summary>관계 기호 — <c>&gt;</c>(to) · <c>&lt;</c>(from) · <c>&lt;&gt;</c>(many-to-many).</summary>
    private static bool IsRelationSigil(char c) => c is '>' or '<';

    /// <summary>
    /// 이 필드가 «이 생성기가 읽지 않는» M3L 관계 표기인가.
    /// </summary>
    public static bool IsRelationNotation(FieldNode? field) =>
        field is not null
        && field.Type is null
        && !string.IsNullOrEmpty(field.Name)
        && IsRelationSigil(field.Name[0]);

    /// <summary>
    /// 경고 목록에 실을 표시명. 「무엇이」에 더해 «무엇을 쓰면 되는지»까지 담는다 —
    /// 경계를 아는 문서를 읽지 않은 사람이 만나는 문장이 이것이기 때문이다.
    /// </summary>
    public static string Describe(string modelName, FieldNode field) =>
        $"{modelName}: 관계 표기 '{field.Name}' (관계는 {ReadForm} 로만 읽는다)";
}
