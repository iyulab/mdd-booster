using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The model sources a build reads to check against but does not generate — <c>references</c> in <c>mdd.json</c>. Their
/// declarations are in the model set (a reference, a lookup, an aspect may name them, and the build checks keys,
/// inheritance and direction against them), but no target emits them: they are generated, and published, elsewhere.
/// Each reference source carries the C# namespace its generated types live in.
/// </summary>
/// <remarks>
/// A reference is the layer below everything this build generates: a referenced declaration may name only other
/// referenced declarations. Whether one does otherwise is <c>LayerDirection</c>'s check.
/// </remarks>
public sealed class ReferenceSet
{
    public static readonly ReferenceSet None = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _namespaceOfSource;

    /// <param name="namespaceOfSource">Each reference source's full path → the namespace of its generated types.</param>
    public ReferenceSet(IReadOnlyDictionary<string, string> namespaceOfSource)
    {
        ArgumentNullException.ThrowIfNull(namespaceOfSource);
        _namespaceOfSource = namespaceOfSource.ToDictionary(p => Normalize(p.Key), p => p.Value, PathComparer);
    }

    public bool IsEmpty => _namespaceOfSource.Count == 0;

    /// <summary>The namespaces of the referenced types, each once.</summary>
    public IReadOnlyList<string> Namespaces => _namespaceOfSource.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public bool Contains(ResolvedModel model) => ContainsSource(model.Source.Source);

    public bool Contains(EnumNode e) => ContainsSource(e.Source);

    public bool Contains(ResolvedView view) => ContainsSource(view.Source.Source);

    /// <summary>The namespace of a referenced model's generated types, or null when this build generates the model.</summary>
    public string? NamespaceOf(ResolvedModel model) => NamespaceOfSource(model.Source.Source);

    /// <summary>The namespace of a referenced view's generated type, or null when this build generates the view.</summary>
    public string? NamespaceOf(ResolvedView view) => NamespaceOfSource(view.Source.Source);

    private string? NamespaceOfSource(string source) =>
        !string.IsNullOrEmpty(source) && _namespaceOfSource.TryGetValue(Normalize(source), out var ns) ? ns : null;

    public bool ContainsSource(string source) => !string.IsNullOrEmpty(source) && _namespaceOfSource.ContainsKey(Normalize(source));

    private static string Normalize(string path) => Path.GetFullPath(path);

    // Paths of one file differ only in case on the file systems where case does not matter.
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
