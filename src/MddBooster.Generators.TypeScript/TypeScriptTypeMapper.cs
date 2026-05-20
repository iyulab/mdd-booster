namespace MddBooster.Generators.TypeScript;

/// <summary>
/// Maps M3L primitive type names to their TypeScript equivalents.
/// Parallels <c>CSharpTypeMapper</c> in the Model generator but targets the
/// TypeScript type system — e.g. <c>integer</c> → <c>number</c>,
/// <c>identifier</c> → <c>string</c> (UUID).
/// </summary>
public static class TypeScriptTypeMapper
{
    /// <summary>
    /// Returns the TypeScript type for a field. If the M3L type name matches a
    /// known enum, returns the PascalCase enum type name (imported from enums_gen).
    /// Otherwise delegates to <see cref="Map"/> for primitives.
    /// </summary>
    public static string MapFieldType(string m3lType, IReadOnlySet<string>? knownEnumNames)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
            throw new ArgumentException("m3lType is empty.", nameof(m3lType));

        if (knownEnumNames is not null && knownEnumNames.Contains(m3lType))
            return PascalCase(m3lType);

        return Map(m3lType);
    }

    public static string Map(string m3lType)
    {
        if (string.IsNullOrWhiteSpace(m3lType))
            throw new ArgumentException("m3lType is empty.", nameof(m3lType));

        // strip params — e.g. "string(30)" → "string"
        var baseType = m3lType.Contains('(') ? m3lType[..m3lType.IndexOf('(')] : m3lType;

        return baseType switch
        {
            "identifier" => "string",
            "boolean" => "boolean",
            "integer" => "number",
            "long" => "number",
            "short" => "number",
            "byte" => "number",
            "float" => "number",
            "double" => "number",
            "decimal" => "number",
            "string" => "string",
            "text" => "string",
            "date" => "string",
            "time" => "string",
            "timestamp" => "string",
            "datetime" => "string",
            "phone" => "string",
            "email" => "string",
            "url" => "string",
            "json" => "string",
            "binary" => "string",
            _ => throw new NotSupportedException($"Unsupported M3L type: '{m3lType}'"),
        };
    }

    internal static string PascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
