using M3L.Native;
using MddBooster.Core.Naming;

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
    /// 속성 인자를 문자열 목록으로 정규화. M3L.Native는 정수 인자를 double로 직렬화하므로
    /// (예: 30.0) 정수면 정수 표기로 되돌린다. 렌더러들이 각자 사본을 두지 않도록 정본을 여기 둔다.
    /// </summary>
    public static IReadOnlyList<string>? StringArgs(List<System.Text.Json.JsonElement>? args)
    {
        if (args is null || args.Count == 0) return null;

        return args
            .Select(p => p.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when p.TryGetDouble(out var d)
                    && d == Math.Floor(d) && !double.IsInfinity(d) => ((long)d).ToString(),
                System.Text.Json.JsonValueKind.String => p.GetString() ?? string.Empty,
                _ => p.GetRawText(),
            })
            .ToList();
    }

    /// <summary>
    /// 필드의 <b>타입</b> 파라미터 — <c>decimal(18,4)</c>의 <c>["18","4"]</c>,
    /// <c>string(200)</c>의 <c>["200"]</c>. 파라미터가 없으면 null.
    /// </summary>
    /// <remarks>
    /// 정규화 규칙(M3L.Native가 정수 인자도 double로 직렬화하는 문제)은 속성 인자와
    /// 완전히 같으므로 <see cref="StringArgs"/>에 위임한다 — 사본을 두지 않는다.
    /// 이름을 따로 두는 이유는 호출부에서 "타입 파라미터"와 "속성 인자"가 서로 다른
    /// 개념이라는 점이 드러나야 하기 때문이다.
    /// </remarks>
    public static IReadOnlyList<string>? TypeParams(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return StringArgs(field.Params);
    }

    /// <summary>
    /// <c>string(n)</c>이 선언한 길이 상한 <c>n</c>. 상한이 없으면 null —
    /// 파라미터 없는 <c>string</c>과 <c>text</c>는 SQL이 <c>NVARCHAR(MAX)</c>로 방출하므로
    /// 상한이 존재하지 않는다. 정수가 아닌 파라미터도 null(상한으로 쓸 수 없다).
    /// </summary>
    /// <remarks>
    /// TypeScript 타깃의 <c>FieldSchema</c>와 생성 폼의 <c>maxlength</c>가 같은 값을 써야 하므로
    /// 정본을 여기 둔다 — 각자 뽑으면 한쪽만 고쳐질 때 소비자에게 서로 다른 상한이 보인다.
    /// </remarks>
    public static int? StringMaxLength(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!string.Equals(field.Type, "string", StringComparison.OrdinalIgnoreCase)) return null;

        var ps = TypeParams(field);
        if (ps is not { Count: > 0 }) return null;

        return int.TryParse(ps[0], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }

    /// <summary>
    /// 필드가 실제로 지는 길이 상한 — 선언된 <c>string(n)</c>이 있으면 그것,
    /// 없으면 타입 자체가 함의하는 상한(<c>email</c>·<c>phone</c>·<c>url</c>).
    /// 둘 다 없으면 null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StringMaxLength"/>와 <b>의미가 다르므로 분리해 둔다</b>.
    /// 전자는 "선언에 적힌 상한"이고 이것은 "필드에 실제로 걸리는 상한"이다.
    /// 컬럼 폭과 맞춰야 하는 쪽은 언제나 후자다 — 컬럼은 <c>email</c>에도 폭을 준다.
    /// 전자를 넓히지 않고 새로 두는 이유는, 명시 상한만 물어야 하는 자리가 생겼을 때
    /// 되돌아갈 곳이 남아 있어야 하고, 이름이 말하는 것과 하는 일이 어긋난 판별자는
    /// 다음 사람에게 같은 실수를 시키기 때문이다.
    /// </para>
    /// <para>
    /// 이 함수가 도입되기 전에는 세 타깃이 전부 <see cref="StringMaxLength"/>를 읽었고,
    /// 그래서 <c>(n)</c>이 적히지 않은 필드의 상한이 컬럼에만 존재했다 — API 표면과
    /// 생성 폼은 컬럼이 거부할 값을 받아들였다.
    /// </para>
    /// </remarks>
    public static int? EffectiveMaxLength(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return StringMaxLength(field) ?? Types.M3lPrimitives.ImplicitMaxLengthOf(field.Type);
    }

    /// <summary>속성의 첫 문자열 인자 — 예: <c>@reference(Target)</c>의 Target. 없으면 null.</summary>
    public static string? FirstArg(FieldNode field, string name)
    {
        var attr = Find(field, name);
        if (attr is null) return null;
        var args = StringArgs(attr.Args);
        return args is { Count: > 0 } ? args[0] : null;
    }

    /// <summary>
    /// 필드를 사람에게 짧게 이름 붙이는 텍스트의 정본 — Model 타깃의 <c>[Display(Name)]</c>,
    /// TypeScript 필드 스키마의 <c>label</c>, 생성 폼의 <c>label</c> prop이 전부 이걸 읽는다.
    /// </summary>
    /// <remarks>
    /// 우선순위: <c>@label(text)</c>(명시 오버라이드) → 필드 <c>description</c>(기존 관용,
    /// 하위호환 유지) → <c>PascalCase(필드명)</c>(전에 없던 fallback을 새로 발명하지 않는다 —
    /// <c>TsEnumLabelsRenderer</c>가 enum 값에 이미 쓰는 것과 같은 모양).
    /// </remarks>
    public static string EffectiveLabel(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return FirstArg(field, "label")
            ?? field.Description
            ?? NameCasing.ToPascalCase(field.Name);
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
