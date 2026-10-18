namespace MddBooster.Generators.Api;

/// <summary>
/// Which namespace each entity type the Api target names lives in. One namespace for all of them (the Model target's,
/// or the Api target's <c>entitiesNamespace</c>), except where a type lives elsewhere: a module of a Model target split
/// into <c>modules</c>, or a referenced build's namespace (<c>references</c>).
/// </summary>
public sealed class EntityNamespaces(string? @default, IReadOnlyDictionary<string, string>? byEntity = null)
{
    public static readonly EntityNamespaces None = new(null);

    private readonly IReadOnlyDictionary<string, string> _byEntity = byEntity ?? new Dictionary<string, string>();

    /// <summary>The namespace of <paramref name="entityType"/> (the PascalCase name of a model or a view), or null when unknown.</summary>
    public string? Of(string entityType) => _byEntity.TryGetValue(entityType, out var ns) ? ns : @default;

    /// <summary>
    /// The qualifying prefix of <paramref name="entityType"/> in a file declaring <paramref name="fileNamespace"/>
    /// (<see cref="EntityTypeRef.PrefixFor"/>).
    /// </summary>
    public string PrefixFor(string fileNamespace, string entityType) => EntityTypeRef.PrefixFor(fileNamespace, Of(entityType));
}
