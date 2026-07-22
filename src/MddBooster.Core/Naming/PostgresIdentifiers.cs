using System.Text;
using System.Text.RegularExpressions;

namespace MddBooster.Core.Naming;

/// <summary>
/// PostgreSQL 방언의 식별자 정책 (ADR-0001 §2.1, U-Platform 소비자 확정).
/// 모델명 PascalCase → 테이블명 snake_case 결정적 변환과 오류 게이트 3종
/// (패턴 · 63바이트 NAMEDATALEN · fold 충돌)의 정본. 조용한 보정(절단·치환)은 하지 않는다 —
/// 게이트 위반은 전부 모아 한 번에 <see cref="PostgresNamingException"/>으로 실패시킨다.
/// Sql(DDL)과 Model(EF <c>ToTable</c>/<c>HasColumnName</c>) 두 타깃이 같은 변환을 소비한다.
/// </summary>
public static partial class PostgresIdentifiers
{
    private const int MaxIdentifierBytes = 63; // PG NAMEDATALEN(64) - NUL

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex ValidPattern();

    /// <summary>
    /// 테이블/컬럼명으로 비인용 사용이 불가한 PG 키워드 — RESERVED_KEYWORD 전체와
    /// TYPE_FUNC_NAME_KEYWORD(함수/타입명으로만 허용) 전체.
    /// 출처: postgresql.org/docs/current/sql-keywords-appendix.html (2026-07-23 검증).
    /// non-reserved 계열(between, bigint 등)은 테이블/컬럼명이 허용되므로 포함하지 않는다.
    /// </summary>
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        // RESERVED_KEYWORD
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric",
        "both", "case", "cast", "check", "collate", "column", "constraint", "create",
        "current_catalog", "current_date", "current_role", "current_time",
        "current_timestamp", "current_user", "default", "deferrable", "desc",
        "distinct", "do", "else", "end", "except", "false", "fetch", "for", "foreign",
        "from", "grant", "group", "having", "in", "initially", "intersect", "into",
        "lateral", "leading", "limit", "localtime", "localtimestamp", "not", "null",
        "offset", "on", "only", "or", "order", "placing", "primary", "references",
        "returning", "select", "session_user", "some", "symmetric", "system_user",
        "table", "then", "to", "trailing", "true", "union", "unique", "user", "using",
        "variadic", "when", "where", "window", "with",
        // TYPE_FUNC_NAME_KEYWORD
        "authorization", "binary", "collation", "concurrently", "cross", "freeze",
        "full", "ilike", "inner", "is", "join", "left", "like", "natural", "outer",
        "overlaps", "right", "similar", "tablesample", "verbose",
    };

    /// <summary>
    /// PascalCase → snake_case 결정적 변환. 분리 규칙(ADR §2.1):
    /// 소문자/숫자→대문자 전이에서 분리, 대문자 연속(약어)은 한 단어로 두되
    /// 뒤에 소문자가 이어지면 마지막 대문자 앞에서 분리, 숫자는 직전 단어에 붙는다.
    /// </summary>
    public static string ToSnakeCase(string pascalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pascalName);

        var sb = new StringBuilder(pascalName.Length + 8);
        for (var i = 0; i < pascalName.Length; i++)
        {
            var c = pascalName[i];
            if (char.IsUpper(c))
            {
                var startsNewWord =
                    i > 0 &&
                    (char.IsLower(pascalName[i - 1]) || char.IsDigit(pascalName[i - 1])
                     || (char.IsUpper(pascalName[i - 1])
                         && i + 1 < pascalName.Length && char.IsLower(pascalName[i + 1])));
                if (startsNewWord)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 단일 식별자 유효성 게이트. 위반이면 설명 문자열, 유효하면 null.
    /// 필드명(이미 snake_case 관용)과 변환 결과 양쪽에 같은 게이트를 적용한다.
    /// </summary>
    public static string? Check(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "식별자가 비어 있습니다";
        }
        if (!ValidPattern().IsMatch(identifier))
        {
            return $"'{identifier}' — 패턴 ^[a-z][a-z0-9_]*$ 을 만족하지 않습니다";
        }
        var bytes = Encoding.UTF8.GetByteCount(identifier);
        if (bytes > MaxIdentifierBytes)
        {
            return $"'{identifier}' — {bytes}바이트로 PG NAMEDATALEN 한계 63바이트를 초과합니다 (자동 절단하지 않음)";
        }
        if (ReservedKeywords.Contains(identifier))
        {
            return $"'{identifier}' — PG 예약어라 비인용 식별자로 쓸 수 없습니다 (인용 식별자는 생성하지 않음 — 모델/필드명을 바꿔야 합니다)";
        }
        return null;
    }

    /// <summary>
    /// 전 모델명 일괄 변환 + 게이트. 패턴·길이 위반과 fold 충돌을 **전부 모아**
    /// 한 번의 <see cref="PostgresNamingException"/>으로 실패시킨다 — 한 건씩 고쳐가며
    /// 재실행하게 만들지 않는다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildTableNameMap(IEnumerable<string> modelNames)
    {
        ArgumentNullException.ThrowIfNull(modelNames);

        var violations = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal); // 테이블명 → 최초 모델

        foreach (var model in modelNames)
        {
            var table = ToSnakeCase(model);

            var violation = Check(table);
            if (violation is not null)
            {
                violations.Add($"모델 '{model}': {violation}");
            }

            if (owners.TryGetValue(table, out var prior))
            {
                violations.Add($"모델 '{prior}'와(과) '{model}'이(가) 같은 테이블명 '{table}'로 fold됩니다");
            }
            else
            {
                owners[table] = model;
                map[model] = table;
            }
        }

        if (violations.Count > 0)
        {
            throw new PostgresNamingException(violations);
        }
        return map;
    }
}

/// <summary>PG 식별자 게이트 위반. 위반 전체가 <see cref="Violations"/>에 담긴다.</summary>
public sealed class PostgresNamingException : Exception
{
    public IReadOnlyList<string> Violations { get; }

    public PostgresNamingException(IReadOnlyList<string> violations)
        : base("PostgreSQL 식별자 게이트 위반:\n" + string.Join("\n", violations))
    {
        Violations = violations;
    }
}
