namespace MddBooster.Generators.Api;

/// <summary>
/// 생성 Api 코드가 엔티티 타입을 어떻게 «가리킬지»를 정하는 한 곳.
/// </summary>
/// <remarks>
/// <para>
/// 종전에는 두 렌더러가 각각 <c>using {entitiesNamespace};</c> 를 방출하고 본문에서는 엔티티를
/// <b>단순 이름</b>으로 썼다. 그 형태는 소비자의 <c>ImplicitUsings</c> 설정에 의존한다 —
/// 두 using 이 같은 단순 이름을 제공하면 C# 은 어느 쪽도 고르지 않고 <c>CS0104</c> 로 거절한다.
/// 모델명이 <c>Task</c> 이기만 하면 <c>System.Threading.Tasks</c> 와 충돌하고, 방출 파일은
/// <c>DO NOT EDIT</c> 이라 소비자가 그 자리를 고칠 수도 없다.
/// </para>
/// <para>
/// 그래서 <b>한정해서 방출한다</b>. 별칭(<c>using Task = …;</c>)이 아니라 완전 한정을 택한 이유는
/// <b>유지할 목록이 없다</b>는 것이다 — 별칭은 「어떤 이름이 BCL 과 충돌하는가」를 생성기가 알아야
/// 성립하고, 그 목록은 BCL 이 커질 때마다 낡는다. 무조건 한정하면 그 지식 자체가 필요 없다.
/// </para>
/// <para>
/// 🔴 한정하는 것은 <b>소비자 자신의</b> 네임스페이스다. 생성기가 이미 설정으로 받아 갖고 있는
/// 값이며, 런타임 라이브러리의 네임스페이스를 방출하는 것과는 다르다 — 그쪽은 여전히 금지고,
/// 이 파일이 내는 <c>global::</c> 도 없다.
/// </para>
/// </remarks>
internal static class EntityTypeRef
{
    /// <summary>
    /// 엔티티 타입 이름 앞에 붙일 한정 접두사. 한정이 불필요하거나 불가능하면 빈 문자열.
    /// </summary>
    /// <param name="fileNamespace">생성 파일이 선언하는 네임스페이스.</param>
    /// <param name="entitiesNamespace">엔티티 타입이 사는 네임스페이스(미지정일 수 있다).</param>
    /// <remarks>
    /// 미지정이거나 파일 자신의 네임스페이스와 같으면 한정하지 않는다 — 그때 엔티티는 using
    /// 임포트가 아니라 <b>둘러싼 네임스페이스</b>에서 찾아지고, C# 의 이름 해석은 둘러싼
    /// 네임스페이스를 임포트보다 «먼저» 본다. 즉 그 경우에는 애초에 충돌이 성립하지 않는다.
    /// </remarks>
    public static string PrefixFor(string fileNamespace, string? entitiesNamespace)
        => string.IsNullOrWhiteSpace(entitiesNamespace)
           || string.Equals(entitiesNamespace, fileNamespace, StringComparison.Ordinal)
            ? string.Empty
            : entitiesNamespace + ".";
}
