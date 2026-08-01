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

    /// <summary>
    /// The length ceiling a semantic type carries without one being written down.
    /// <c>email</c> is bounded even though no <c>(n)</c> appears in the
    /// declaration, because the language defines it as a bounded string.
    /// Types absent from this table are unbounded or carry their bound as an
    /// explicit type parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These numbers are not this generator's to choose.</b> The language
    /// specification fixes them (§10.4.2 Semantic Types), and the parser does
    /// not perform the expansion — it reports the semantic type name unchanged,
    /// leaving every consumer to carry the table itself. So this is a mirror of
    /// a value defined elsewhere, and each entry is checked against the
    /// specification rather than chosen here.
    /// </para>
    /// <para>
    /// <b>Why one table and not one per target.</b> The bound has to reach the
    /// column, the entity's validation attribute, and the generated form. Three
    /// copies of a number that must agree is the shape of a defect that only
    /// appears once one copy is edited — which is exactly how the explicit and
    /// implicit cases came to disagree in the first place: the column got its
    /// bound from the type, the entity got its bound from the type
    /// <em>parameter</em>, and nothing carried an implicit bound to the entity
    /// at all.
    /// </para>
    /// <para>
    /// <b>Deviation — <c>phone</c>.</b> The specification says 20; this table
    /// says 30, which is the value already in use. Narrowing a column is not a
    /// change a generator can make safely on a consumer's behalf: stored values
    /// longer than the new bound make the migration fail or truncate, and the
    /// generator cannot see the stored data. The deviation is recorded rather
    /// than silently kept, and it is wider than the specification, so a value
    /// the specification permits is never rejected.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, int> ImplicitMaxLength =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["email"] = 320,
            ["url"] = 2048,
            ["phone"] = 30,   // specification says 20 — see remarks
        };

    /// <summary>
    /// The implicit length ceiling for <paramref name="typeName"/>, or null when
    /// the type carries no bound of its own.
    /// </summary>
    public static int? ImplicitMaxLengthOf(string? typeName) =>
        typeName is not null && ImplicitMaxLength.TryGetValue(typeName, out var n) ? n : null;
}
