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

        // Enum CHECK 제약은 여기(inline)가 아니라 TableRenderer의 table-level 경로에서
        // opt-in(EmitEnumCheckConstraints, 기본 off)으로 방출된다. inline CHECK는 SSDT
        // dacpac이 서버 표현식과 매칭하지 못해 매번 Drop→Create를 유발하므로 금지.

        // 기본값 처리 — `= value` 구문 우선, 없으면 @default(value) 속성 (스펙 §10.8.1).
        var defaultValue = MddBooster.Core.Ast.FieldAttributes.EffectiveDefault(field);
        if (!string.IsNullOrEmpty(defaultValue))
        {
            var def = MapDefaultValue(defaultValue!, m3lType, enumLookup);
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
        => MddBooster.Core.Ast.FieldAttributes.Has(field, name);

    private static string? GetAttributeFirstParam(FieldNode field, string name)
        => MddBooster.Core.Ast.FieldAttributes.FirstArg(field, name);

    private static IReadOnlyList<string>? ExtractStringParams(List<JsonElement>? paramsList)
        => MddBooster.Core.Ast.FieldAttributes.StringArgs(paramsList);

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
