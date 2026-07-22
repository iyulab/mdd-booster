using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// 필드 속성 조회의 정본. 스펙(§10.8.1)이 선언한 별칭(`@pk` ↔ `@primary`)을
/// 여기서 해소하므로, 생성기는 속성을 직접 문자열 비교하지 말고 이 클래스를 거친다.
/// 파서 AST는 원문 표기를 보존하므로(별칭 정규화 없음) 조회 시점에 해소한다.
/// </summary>
public static class FieldAttributes
{
    // 별칭 → 정규명. 정규명은 mdd-booster 내부에서 통용되는 짧은 표기를 따른다.
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = "pk",
        };

    public static bool Has(FieldNode field, string name)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Attributes.Any(a => Matches(a.Name, name));
    }

    public static FieldAttribute? Find(FieldNode field, string name)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Attributes.FirstOrDefault(a => Matches(a.Name, name));
    }

    private static bool Matches(string declared, string queried) =>
        string.Equals(Canonical(declared), Canonical(queried), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string name) =>
        Aliases.TryGetValue(name, out var canonical) ? canonical : name;

    /// <summary>
    /// 필드의 유효 기본값 — `= value` 구문(<c>FieldNode.DefaultValue</c>)이 우선하고,
    /// 없으면 스펙 §10.8.1의 대안 표기 <c>@default(value)</c> 속성을 해소한다.
    /// (2026-07-22 이전에는 속성 형태가 무경고로 탈락했다.)
    /// </summary>
    public static string? EffectiveDefault(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!string.IsNullOrEmpty(field.DefaultValue)) return field.DefaultValue;

        var attr = Find(field, "default");
        if (attr?.Args is not { Count: > 0 }) return null;

        var arg = attr.Args[0];
        return arg.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => arg.GetString(),
            // M3L.Native는 정수 인자도 double로 직렬화(예: 30.0) — 정수면 정수 표기로 정규화.
            System.Text.Json.JsonValueKind.Number when arg.TryGetDouble(out var d)
                && d == Math.Floor(d) && !double.IsInfinity(d) => ((long)d).ToString(),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => arg.GetRawText(),
        };
    }

    /// <summary>
    /// 알려진 속성 어휘 — 스펙 §10.8 표준 카탈로그 + mdd-booster가 소비하는 확장 속성.
    /// 카탈로그 밖 속성은 스펙상 합법 custom이므로 이 집합은 "오타 의심" 판정
    /// (SemanticAnalyzer MDD006)의 기준으로만 쓰이고, 미포함이 오류를 뜻하지 않는다.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 스펙 §10.8 표준 카탈로그
        "pk", "primary", "unique", "not_null", "index", "generated", "immutable", "default",
        "reference", "fk", "on_delete", "on_update",
        "searchable", "visibility",
        "min", "max", "validate",
        "computed", "computed_raw", "lookup", "rollup", "from",
        // mdd-booster 확장 어휘 (생성기가 소비)
        "indexed", "display_labels", "slot", "group", "help", "label",
        "implements", "inherits", "internal", "system", "binding",
    };
}
