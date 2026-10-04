namespace MddBooster.Generators.Api;

/// <summary>
/// The runtime surfaces a generated registration wires. Which surfaces a deployment serves is
/// deployment topology, so it is a consumer setting (<c>mdd.json</c>), never part of the shared
/// model — the same placement as the entity axis (<c>includeEntities</c>/<c>excludeEntities</c>).
/// </summary>
[Flags]
public enum ApiSurfaces
{
    None = 0,

    /// <summary>OData entity sets, their controllers and shared-key declarations — the only write path.</summary>
    OData = 1,

    /// <summary>GraphQL query fields (read only).</summary>
    GraphQL = 2,

    All = OData | GraphQL,
}

/// <summary>The <c>surfaces</c> spelling of <see cref="ApiSurfaces"/>, and its validation.</summary>
public static class ApiSurfaceNames
{
    public const string OData = "odata";
    public const string GraphQL = "graphql";

    public static IReadOnlyList<string> All { get; } = [OData, GraphQL];

    /// <summary>
    /// Every problem with a configured <c>surfaces</c> list, collected rather than stopping at the
    /// first. Empty selects nothing — a registration that registers nothing is a typo, not a
    /// configuration. Values are exact (lower case), and a repeated value is named rather than folded.
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var problems = new List<string>();
        if (values.Count == 0)
            problems.Add($"비어 있습니다 — 적어도 하나를 고르세요({string.Join(", ", All)}). 생략하면 둘 다입니다.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!All.Contains(value, StringComparer.Ordinal))
                problems.Add($"'{value}' 은(는) 알 수 없는 표면입니다 — 쓸 수 있는 것: {string.Join(", ", All)}.");
            else if (!seen.Add(value))
                problems.Add($"'{value}' 이(가) 두 번 적혀 있습니다.");
        }
        return problems;
    }

    /// <summary>The surfaces a valid list names; <see cref="ApiSurfaces.All"/> when the option is omitted.</summary>
    public static ApiSurfaces Parse(IReadOnlyList<string>? values)
    {
        if (values is null) return ApiSurfaces.All;
        var surfaces = ApiSurfaces.None;
        foreach (var value in values)
        {
            surfaces |= value switch
            {
                OData => ApiSurfaces.OData,
                GraphQL => ApiSurfaces.GraphQL,
                _ => throw new ArgumentException($"Unknown surface '{value}' — validate before parsing.", nameof(values)),
            };
        }
        return surfaces;
    }
}
