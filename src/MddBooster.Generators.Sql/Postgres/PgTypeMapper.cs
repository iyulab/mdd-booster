using M3L.Native;

namespace MddBooster.Generators.Sql.Postgres;

/// <summary>
/// M3L 타입 → PostgreSQL 타입 매핑 (T-SQL <see cref="SqlTypeMapper"/>의 PG 대응).
/// 타입 이름은 pg_catalog 별칭 관용(소문자, <c>varchar</c>/<c>timestamptz</c>)을 따른다 —
/// Schemorph PG 비교는 카탈로그 정규화를 거치므로 별칭 표기가 diff를 흔들지 않는다.
/// </summary>
public static class PgTypeMapper
{
    /// <summary>
    /// 필드 타입 해석. enum이면 <see cref="EnumSqlConvention.ColumnLength"/> 폭의
    /// <c>varchar(n)</c>(저장값은 T-SQL 타깃과 동일한 snake_case 원문).
    /// </summary>
    public static string MapFieldType(
        string m3lType,
        IReadOnlyList<string>? parameters,
        IReadOnlyDictionary<string, EnumNode>? enumLookup)
    {
        if (enumLookup is not null && enumLookup.TryGetValue(m3lType, out var enumNode))
        {
            return $"varchar({EnumSqlConvention.ColumnLength(enumNode)})";
        }

        return Map(m3lType, parameters);
    }

    public static string Map(string m3lType, IReadOnlyList<string>? parameters)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
        {
            throw new ArgumentException("m3lType이 비어 있습니다.", nameof(m3lType));
        }

        var p0 = parameters is { Count: > 0 } ? parameters[0] : null;
        var p1 = parameters is { Count: > 1 } ? parameters[1] : null;

        return m3lType switch
        {
            "identifier" => "uuid",
            "boolean" => "boolean",
            "integer" => "integer",
            "long" => "bigint",
            "short" => "smallint",
            // PG에는 1바이트 정수 타입이 없다 — smallint로 승격 (값 범위는 보존)
            "byte" => "smallint",
            "float" => "real",
            "double" => "double precision",
            "decimal" when p0 is not null && p1 is not null => $"numeric({p0},{p1})",
            "decimal" when p0 is not null => $"numeric({p0},0)",
            "decimal" => "numeric(18,2)",
            "string" when p0 is not null => $"varchar({p0})",
            "string" => "text",
            "text" => "text",
            "date" => "date",
            "time" => "time",
            "timestamp" => "timestamptz",
            "datetime" => "timestamptz",
            "phone" => "varchar(30)",
            "email" => "varchar(200)",
            "url" => "varchar(500)",
            "json" => "jsonb",
            // bytea는 길이 상한 개념이 없다 — binary(n)의 길이 인자는 DDL에서 소실 (문서화된 완화)
            "binary" => "bytea",
            _ => throw new NotSupportedException($"지원하지 않는 M3L 타입: '{m3lType}'"),
        };
    }
}
