using System.Text.Json;
using M3L.Native;

namespace MddBooster.Generators.Sql;

public static class ColumnRenderer
{
    public static string Render(FieldNode field, IReadOnlyDictionary<string, EnumNode>? enumLookup = null)
    {
        ArgumentNullException.ThrowIfNull(field);

        var columnName = ToPascalCase(field.Name);
        var m3lType = field.Type ?? throw new InvalidOperationException(
            $"필드 '{field.Name}'에 타입이 없습니다.");
        var parameters = ExtractStringParams(field.Params);
        var sqlType = SqlTypeMapper.MapFieldType(m3lType, parameters, enumLookup);

        var nullability = field.Nullable ? "NULL" : "NOT NULL";

        var suffix = BuildSuffix(field, m3lType, columnName, enumLookup);

        var core = $"[{columnName}] {sqlType} {nullability}";
        return string.IsNullOrEmpty(suffix) ? core : $"{core} {suffix}";
    }

    private static string BuildSuffix(FieldNode field, string m3lType, string columnName, IReadOnlyDictionary<string, EnumNode>? enumLookup)
    {
        var parts = new List<string>();

        // PK + @generated 처리 (identifier 전제)
        var isPk = HasAttribute(field, "pk");
        var isGenerated = HasAttribute(field, "generated");
        if (isPk)
        {
            parts.Add("PRIMARY KEY");
            if (isGenerated && m3lType == "identifier")
            {
                parts.Add("DEFAULT NEWSEQUENTIALID()");
                return string.Join(" ", parts);
            }
        }

        // @reference(Target) → FOREIGN KEY REFERENCES
        var referenceTarget = GetAttributeFirstParam(field, "reference");
        if (!string.IsNullOrEmpty(referenceTarget))
        {
            parts.Add($"REFERENCES [dbo].[{referenceTarget}]([Id])");
        }

        // Enum CHECK 제약은 table-level로 분리 (BuildCheckConstraint 메서드).
        // inline CHECK는 SSDT dacpac이 서버 표현식과 매칭하지 못해 매번
        // Drop→Create 스크립트를 생성하는 문제가 있음.

        // 기본값 처리
        if (!string.IsNullOrEmpty(field.DefaultValue))
        {
            var def = MapDefaultValue(field.DefaultValue!, m3lType, enumLookup);
            if (def is not null)
            {
                parts.Add($"DEFAULT {def}");
            }
        }

        return string.Join(" ", parts);
    }

    private static string? MapDefaultValue(
        string raw, string m3lType,
        IReadOnlyDictionary<string, EnumNode>? enumLookup)
    {
        var trimmed = raw.Trim();

        // boolean
        if (m3lType == "boolean")
        {
            return trimmed switch
            {
                "true" => "1",
                "false" => "0",
                _ => null,
            };
        }

        // now() / current_timestamp → SYSDATETIMEOFFSET() (matches DateTimeOffset in C#)
        if (string.Equals(trimmed, "now()", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "current_timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return "SYSDATETIMEOFFSET()";
        }

        // 숫자 리터럴 (decimal/integer 등)
        if (double.TryParse(trimmed, out _))
        {
            return trimmed;
        }

        // 문자열 리터럴 ("..." 형식 — m3l 파서가 quote를 보존한 경우)
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            var inner = trimmed.Substring(1, trimmed.Length - 2).Replace("'", "''");
            return $"N'{inner}'";
        }

        // enum 타입 default — m3l 파서가 quote를 제거한 채 raw value를 반환하므로
        // (예: `row_type: OrderItemRowType = "product"` → DefaultValue="product"),
        // enum 컬럼은 별도 분기로 N'value' 직 emit. SSDT가 NOT NULL 컬럼 추가 시
        // 기존 행에 default를 채울 수 있게 함 (Msg 515 회피).
        if (enumLookup is not null && enumLookup.ContainsKey(m3lType))
        {
            var inner = trimmed.Replace("'", "''");
            return $"N'{inner}'";
        }

        // string/text 타입의 quote 없는 raw default도 동일 정책
        if (m3lType == "string" || m3lType == "text"
            || m3lType.StartsWith("string(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = trimmed.Replace("'", "''");
            return $"N'{inner}'";
        }

        return null;
    }

    private static bool HasAttribute(FieldNode field, string name)
    {
        return field.Attributes.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetAttributeFirstParam(FieldNode field, string name)
    {
        var attr = field.Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (attr is null) return null;
        var parameters = ExtractStringParams(attr.Args);
        return parameters is { Count: > 0 } ? parameters[0] : null;
    }

    private static IReadOnlyList<string>? ExtractStringParams(List<JsonElement>? paramsList)
    {
        if (paramsList is null || paramsList.Count == 0)
        {
            return null;
        }

        return paramsList
            .Select(p => p.ValueKind switch
            {
                JsonValueKind.Number => NumberToString(p),
                JsonValueKind.String => p.GetString() ?? string.Empty,
                _ => p.GetRawText(),
            })
            .ToList();
    }

    private static string NumberToString(JsonElement element)
    {
        // M3L.Native serializes integer params as doubles (e.g. 30.0).
        // Emit as integer string when there is no fractional part.
        if (element.TryGetDouble(out var d) && d == Math.Floor(d) && !double.IsInfinity(d))
        {
            return ((long)d).ToString();
        }

        return element.GetRawText();
    }

    private static string ToPascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake))
        {
            return snake;
        }

        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
