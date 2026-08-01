using MddBooster.Core.Naming;
namespace MddBooster.Generators.Model;

/// <summary>
/// Maps M3L primitive type names to their C# equivalents used in generated
/// entity code. Parallels <c>SqlTypeMapper</c> in the SQL generator but
/// targets the CLR type system — e.g. <c>string(30)</c> in M3L becomes the SQL
/// <c>NVARCHAR(30)</c> and the C# <c>string</c>.
/// </summary>
/// <remarks>
/// The semantic string types (<c>phone</c>/<c>email</c>/<c>url</c>) are
/// intentionally mapped to plain <c>string</c> rather than to a dedicated
/// value-object struct. A struct would be the more expressive C# type, but
/// <c>ODataConventionModelBuilder</c> cannot register one as an EDM complex
/// type, and an entity carrying one fails to serialize — the connection resets
/// rather than returning an error, which is a poor thing to hand a consumer.
/// The bound each type carries still reaches the column and the entity's
/// validation attribute; format validation is left to the application layer.
/// The obstacle is specific to the OData serialization boundary, so a data
/// layer is free to convert these values on the way to storage.
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
            return NameCasing.ToPascalCase(m3lType);
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
        "binary" => " = Array.Empty<byte>();",
        _ when IsReferenceType(m3lType) => " = string.Empty;",
        _ => string.Empty,
    };

    /// <summary>
    /// Whether the M3L type maps to a C# <em>reference</em> type. This is the set
    /// for which a non-nullable declaration needs an explicit initializer — and,
    /// identically, the set for which <c>[Required]</c> carries meaning: value
    /// types (numbers, temporals, <c>Guid</c>, enums) are already non-null in the
    /// CLR, so <c>RequiredAttribute</c> always passes and adds only noise.
    /// Both consumers read this one definition rather than keeping copies.
    /// </summary>
    /// <remarks>
    /// Not the inverse of <see cref="IsValueType"/>: that predicate answers a
    /// narrower question (M3L primitives that are structs) and returns
    /// <c>false</c> for unknown names such as enum types.
    /// </remarks>
    public static bool IsReferenceType(string m3lType) => m3lType switch
    {
        "string" or "text" or "json" or "phone" or "email" or "url" or "binary" => true,
        _ => false,
    };

}
