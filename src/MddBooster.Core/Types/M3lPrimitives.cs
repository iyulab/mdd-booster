namespace MddBooster.Core.Types;

/// <summary>
/// Canonical list of M3L primitive type names shared across every generator.
/// Adding a new primitive requires touching this list once and then teaching
/// each generator's type mapper how to render it.
/// </summary>
/// <remarks>
/// Keep this in lock-step with <c>SqlTypeMapper.Map</c> in
/// <c>MddBooster.Generators.Sql</c> and <c>CSharpTypeMapper.Map</c> in
/// <c>MddBooster.Generators.Model</c>. Any primitive present in those mappers
/// but missing here will be mis-classified as an unknown type (enum/model
/// reference) by the semantic analyzer.
/// </remarks>
public static class M3lPrimitives
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "identifier",
        "boolean",
        "integer", "long", "short", "byte",
        "float", "double", "decimal",
        "string", "text",
        "date", "time", "timestamp", "datetime",
        "phone", "email", "url",
        "json", "binary",
    };

    public static bool Contains(string? typeName) =>
        typeName is not null && All.Contains(typeName);
}
