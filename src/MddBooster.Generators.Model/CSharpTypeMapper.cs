namespace MddBooster.Generators.Model;

/// <summary>
/// Maps M3L primitive type names to their C# equivalents used in generated
/// entity code. Parallels <c>SqlTypeMapper</c> in the SQL generator but
/// targets the CLR type system — e.g. <c>string(30)</c> in M3L becomes the SQL
/// <c>NVARCHAR(30)</c> and the C# <c>string</c>.
/// </summary>
/// <remarks>
/// The M3L "value object" primitives (<c>phone</c>/<c>email</c>/<c>url</c>) are
/// intentionally mapped to plain <c>string</c> — <em>not</em> to
/// <c>Iyu.Core.ValueObjects</c> types. <c>ODataConventionModelBuilder</c> cannot
/// register value-object structs as EDM complex types, which breaks OData
/// serialization (connection reset). The field still emits its
/// <c>NVARCHAR(30/200/500)</c> SQL shape; format validation lives at the
/// application layer. (<c>Iyu.Data</c> ships value-object <c>ValueConverter</c>s,
/// so the blocked boundary is specifically the OData serialization layer.)
/// </remarks>
public static class CSharpTypeMapper
{
    /// <summary>
    /// Returns the C# type name for an M3L primitive. Parameters (e.g.
    /// string length) are intentionally ignored — they influence SQL shape
    /// but not the C# type.
    /// </summary>
    /// <param name="m3lType">The M3L primitive name (e.g. "string", "integer").</param>
    /// <returns>A C# type literal suitable for emission into a field declaration.</returns>
    /// <summary>
    /// Resolves the C# type for a field. If the M3L type name matches a
    /// known enum, returns the PascalCase enum type name (unqualified — the
    /// caller ensures the generated enum lives in the same namespace).
    /// Otherwise delegates to <see cref="Map"/> for primitives.
    /// </summary>
    public static string MapFieldType(string m3lType, IReadOnlySet<string>? knownEnumNames)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
            throw new ArgumentException("m3lType is empty.", nameof(m3lType));

        if (knownEnumNames is not null && knownEnumNames.Contains(m3lType))
        {
            return PascalCase(m3lType);
        }

        return Map(m3lType);
    }

    public static string Map(string m3lType)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
            throw new ArgumentException("m3lType is empty.", nameof(m3lType));

        return m3lType switch
        {
            "identifier" => "Guid",
            "boolean" => "bool",
            "integer" => "int",
            "long" => "long",
            "short" => "short",
            "byte" => "byte",
            "float" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "string" => "string",
            "text" => "string",
            "date" => "DateOnly",
            "time" => "TimeOnly",
            "timestamp" => "DateTimeOffset",
            "datetime" => "DateTimeOffset",
            // phone/email/url → plain string: ODataConventionModelBuilder cannot register
            // value object structs as EDM complex types, causing connection-reset on serialization.
            "phone" => "string",
            "email" => "string",
            "url" => "string",
            "json" => "string",
            "binary" => "byte[]",
            _ => throw new NotSupportedException($"Unsupported M3L type: '{m3lType}'"),
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the M3L type maps to a value type (including
    /// structs and value object records). Used by the renderer to decide
    /// whether a non-nullable field needs an explicit <c>= default!</c>
    /// initializer.
    /// </summary>
    public static bool IsValueType(string m3lType) => m3lType switch
    {
        "identifier" or "boolean" or "integer" or "long" or "short" or "byte"
            or "float" or "double" or "decimal"
            or "date" or "time" or "timestamp" or "datetime" => true,
        // phone/email/url now map to string (reference type)
        "phone" or "email" or "url" => false,
        _ => false,
    };

    /// <summary>
    /// Returns the initializer suffix to append immediately after a property's
    /// <c>{ get; set; }</c> block. For non-nullable reference types we emit
    /// a default expression to satisfy nullable-reference analysis; value
    /// types produce an empty string because the auto-property block closes
    /// itself and appending <c>;</c> would be a syntax error.
    /// </summary>
    public static string DefaultInitializer(string m3lType) => m3lType switch
    {
        "string" or "text" or "json" or "phone" or "email" or "url" => " = string.Empty;",
        "binary" => " = Array.Empty<byte>();",
        _ => string.Empty,
    };

    private static string PascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
