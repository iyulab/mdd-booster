using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// 표면 타깃(Api·TypeScript)이 "어떤 엔티티를, 어떤 필드를 방출하는가"를 결정하는 정본.
/// </summary>
/// <remarks>
/// <para>
/// 두 개념을 구분한다. <see cref="IsInternal"/>은 <c>@internal</c> 모델 속성 판정으로
/// **모델 전역**이며, 이를 존중할지는 산출물마다 다르다(데이터 API는 존중, TS 인터페이스는 아님 —
/// 각 렌더러의 판단). <see cref="EntitySurfaceFilter"/>는 <c>mdd.json</c> 타깃의
/// <c>includeEntities</c>/<c>excludeEntities</c>로 **타깃별**로 범위를 좁히며, 산출물 종류와
/// 무관하게 균일하게 적용된다.
/// </para>
/// <para>
/// 왜 타깃별 축이 필요한가: 한 모델 정본을 여러 서버가 소비할 때 <c>@internal</c>은
/// "타깃 A에서는 빠지고 타깃 B에는 남는다"를 표현할 수 없다. 배포 토폴로지는 소비자 설정
/// (<c>mdd.json</c>)에 살아야 하고 공유 정본(m3l)에 심으면 안 된다.
/// </para>
/// <para>
/// <see cref="IsFieldInternal"/>은 같은 어휘(<c>@internal</c>)를 **필드** 결에 적용한다 —
/// 엔티티 자체는 데이터 API에 남지만 그 필드 하나만 읽기 표면(뷰 SELECT·Ext 클래스)에서
/// 빠진다. 기반 테이블·기반(Write) 엔티티에는 그대로 남는다 — 저장은 하되 노출하지 않는다.
/// </para>
/// </remarks>
public static class EntitySurface
{
    /// <summary>모델에 <c>@internal</c>이 붙어 있는지.</summary>
    public static bool IsInternal(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return (model.Source.Attributes ?? [])
            .Any(a => string.Equals(a.Name, "internal", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>필드에 <c>@internal</c>이 붙어 있는지 — 읽기 표면(뷰 SELECT·Ext 클래스)에서 제외 대상.</summary>
    public static bool IsFieldInternal(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return FieldAttributes.Has(field, "internal");
    }
}

/// <summary>
/// 타깃별 엔티티 부분집합 필터. <c>includeEntities</c>(화이트리스트) 또는
/// <c>excludeEntities</c>(블랙리스트) 중 하나만 쓸 수 있고, 둘 다 없으면 전량 통과한다
/// (완전 하위호환).
/// </summary>
/// <remarks>
/// 검증 위반은 <see cref="Validate"/>가 **전부 모아** 문자열 목록으로 돌려준다 —
/// 한 건씩 고쳐가며 재실행하게 만들지 않는다(<c>PostgresIdentifiers.BuildTableNameMap</c>과 같은 관용).
/// 조용한 드롭·조용한 무시는 하지 않는다: 오타로 표면이 텅 비는 것이 가장 나쁜 실패다.
/// </remarks>
public sealed class EntitySurfaceFilter
{
    /// <summary>필터가 없는 상태 — 전량 통과.</summary>
    public static EntitySurfaceFilter PassAll { get; } = new(null, null);

    private readonly HashSet<string>? _include;
    private readonly HashSet<string>? _exclude;

    private EntitySurfaceFilter(IEnumerable<string>? include, IEnumerable<string>? exclude)
    {
        _include = include is null ? null : new HashSet<string>(include, StringComparer.Ordinal);
        _exclude = exclude is null ? null : new HashSet<string>(exclude, StringComparer.Ordinal);
    }

    /// <summary>어떤 필터도 설정되지 않았는지 (콘솔 회계 출력 생략 판단용).</summary>
    public bool IsPassAll => _include is null && _exclude is null;

    /// <summary>
    /// 필터를 만들고 동시에 검증한다. 반환된 <paramref name="violations"/>가 비어 있지 않으면
    /// 호출부가 빌드를 실패시켜야 한다 — 필터 자체는 그 경우에도 사용 가능한 상태로 돌려주지 않는다.
    /// </summary>
    /// <param name="include">타깃의 <c>includeEntities</c> (없으면 null/빈 목록).</param>
    /// <param name="exclude">타깃의 <c>excludeEntities</c> (없으면 null/빈 목록).</param>
    /// <param name="allModels">해상된 전체 모델 — 미지 이름 판정과 제안에 쓴다.</param>
    /// <param name="targetLabel">위반 메시지에 넣을 타깃 식별 문자열 (예: <c>Api → ../MesServer</c>).</param>
    public static EntitySurfaceFilter Validate(
        IReadOnlyList<string>? include,
        IReadOnlyList<string>? exclude,
        IReadOnlyList<ResolvedModel> allModels,
        string targetLabel,
        out IReadOnlyList<string> violations)
    {
        ArgumentNullException.ThrowIfNull(allModels);

        var inc = Normalize(include);
        var exc = Normalize(exclude);
        var found = new List<string>();

        if (inc is not null && exc is not null)
        {
            found.Add($"{targetLabel}: includeEntities 와 excludeEntities 를 함께 지정할 수 없습니다 — 하나만 쓰세요.");
        }

        var known = allModels.Select(m => m.Name).ToList();
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);

        foreach (var (listName, list) in new[] { ("includeEntities", inc), ("excludeEntities", exc) })
        {
            if (list is null) continue;
            foreach (var name in list.Where(n => !knownSet.Contains(n)))
            {
                var suggestion = Suggest(name, known);
                found.Add($"{targetLabel}: {listName} 의 '{name}' 은(는) 모델에 없습니다"
                          + (suggestion is null ? "." : $" — '{suggestion}' 를 의도했나요?"));
            }
        }

        // include 에 @internal 엔티티를 적는 것은 모순이다. 조용히 드롭하면 소비자는 자기가 적은
        // 이름이 왜 표면에 없는지 알 수 없다 — 어느 쪽을 원하는지 말하게 한다.
        if (inc is not null)
        {
            foreach (var m in allModels.Where(m => inc.Contains(m.Name) && EntitySurface.IsInternal(m)))
            {
                found.Add($"{targetLabel}: includeEntities 에 '{m.Name}' 이(가) 있지만 이 모델은 @internal 입니다 "
                          + "— 데이터 API 노출 제외 대상이라 포함할 수 없습니다. 모델의 @internal 을 지우거나 목록에서 빼세요.");
            }
        }

        violations = found;
        return found.Count > 0 ? PassAll : new EntitySurfaceFilter(inc, exc);
    }

    /// <summary>필터를 적용한 모델 목록. 순서는 입력 순서를 보존한다.</summary>
    public IReadOnlyList<ResolvedModel> Apply(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (IsPassAll) return models;

        return [.. models.Where(m =>
            (_include is null || _include.Contains(m.Name)) &&
            (_exclude is null || !_exclude.Contains(m.Name)))];
    }

    /// <summary>
    /// 콘솔 회계 한 줄 — 포함 수·제외 수·제외된 이름. 화이트리스트는 정본에 새 엔티티가 추가돼도
    /// 조용히 빠지므로(drift), 무엇이 빠졌는지 매 빌드에서 보이게 한다.
    /// </summary>
    public string DescribeCoverage(IReadOnlyList<ResolvedModel> allModels)
    {
        ArgumentNullException.ThrowIfNull(allModels);
        var kept = Apply(allModels);
        var dropped = allModels.Where(m => !kept.Contains(m)).Select(m => m.Name).ToList();
        var mode = _include is not null ? "includeEntities" : "excludeEntities";
        return $"{mode}: 포함 {kept.Count}개 / 제외 {dropped.Count}개"
               + (dropped.Count > 0 ? $" — {string.Join(", ", dropped)}" : "");
    }

    private static HashSet<string>? Normalize(IReadOnlyList<string>? list)
        => list is null || list.Count == 0
            ? null
            : new HashSet<string>(list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.Ordinal);

    /// <summary>가장 가까운 알려진 이름 제안 (오타 구제). 거리가 이름 길이의 절반을 넘으면 제안하지 않는다.</summary>
    private static string? Suggest(string name, IReadOnlyList<string> known)
    {
        if (known.Count == 0) return null;
        var best = known
            .Select(k => (k, d: Levenshtein(name.ToLowerInvariant(), k.ToLowerInvariant())))
            .OrderBy(t => t.d)
            .First();
        return best.d <= Math.Max(2, name.Length / 2) ? best.k : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
