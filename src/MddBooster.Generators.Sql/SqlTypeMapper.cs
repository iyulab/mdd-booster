using M3L.Native;

namespace MddBooster.Generators.Sql;

public static class SqlTypeMapper
{
    /// <summary>
    /// Resolves the SQL column type for a field. If <paramref name="m3lType"/>
    /// matches a known enum's name, returns an <c>NVARCHAR(n)</c> sized by
    /// <see cref="EnumSqlConvention.ColumnLength"/> (최장 멤버, 하한 20);
    /// otherwise delegates to the primitive map.
    /// </summary>
    public static string MapFieldType(
        string m3lType,
        IReadOnlyList<string>? parameters,
        IReadOnlyDictionary<string, EnumNode>? enumLookup)
    {
        if (enumLookup is not null && enumLookup.TryGetValue(m3lType, out var enumNode))
        {
            return $"NVARCHAR({EnumSqlConvention.ColumnLength(enumNode)})";
        }

        return Map(m3lType, parameters);
    }

    public static string Map(string m3lType, IReadOnlyList<string>? parameters)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
        {
            throw new ArgumentException("m3lType이 비어 있습니다.", nameof(m3lType));
        }

        // A semantic type carries a length ceiling the declaration never spells
        // out. The number belongs to the language, not to this mapper, so it is
        // read from the one table that also feeds the other targets — a column
        // and the entity attribute validating writes to it cannot be allowed to
        // disagree about the same limit.
        if (MddBooster.Core.Types.M3lPrimitives.ImplicitMaxLengthOf(m3lType) is { } implicitMax)
        {
            return $"NVARCHAR({implicitMax})";
        }

        var p0 = parameters is { Count: > 0 } ? parameters[0] : null;
        var p1 = parameters is { Count: > 1 } ? parameters[1] : null;

        return m3lType switch
        {
            "identifier" => "UNIQUEIDENTIFIER",
            "boolean" => "BIT",
            "integer" => "INT",
            "long" => "BIGINT",
            "short" => "SMALLINT",
            "byte" => "TINYINT",
            "float" => "REAL",
            "double" => "FLOAT",
            "decimal" when p0 is not null && p1 is not null => $"DECIMAL({p0},{p1})",
            "decimal" when p0 is not null => $"DECIMAL({p0},0)",
            "decimal" => "DECIMAL(18,2)",
            "string" when p0 is not null => $"NVARCHAR({p0})",
            "string" => "NVARCHAR(MAX)",
            "text" => "NVARCHAR(MAX)",
            "date" => "DATE",
            "time" => "TIME",
            "timestamp" => "DATETIMEOFFSET",
            "datetime" => "DATETIMEOFFSET",
            // phone / email / url are handled above — their bound is implicit.
            "json" => "NVARCHAR(MAX)",
            "binary" when p0 is not null => $"VARBINARY({p0})",
            "binary" => "VARBINARY(MAX)",
            _ => throw new NotSupportedException($"지원하지 않는 M3L 타입: '{m3lType}'"),
        };
    }
}
