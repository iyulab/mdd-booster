using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>
/// Resolves an <c>@lookup(fk.column)</c> or multi-hop <c>@lookup(fk.fk.column)</c> declaration to the field it points at.
/// </summary>
/// <remarks>
/// The walk itself is <see cref="LookupPath.Walk"/>, the same one <see cref="SemanticAnalyzer"/>
/// reads while emitting MDD003–MDD006. This type is the convenience for a consumer that needs only
/// the *target field* rather than a diagnostic.
///
/// It reports nothing. Every malformed shape returns <c>null</c>, because the analyzer already
/// owns the job of telling the user which part was wrong — duplicating the diagnostics here would
/// double every message.
/// </remarks>
public static class LookupTarget
{
    /// <summary>
    /// The field <paramref name="lookupField"/> reads, or <c>null</c> when the declaration is not a
    /// resolvable lookup (wrong kind, malformed path, missing FK, no <c>@reference</c>, unknown
    /// target model, or unknown target column).
    /// </summary>
    public static FieldNode? Resolve(
        ResolvedModel owner,
        FieldNode lookupField,
        IReadOnlyList<ResolvedModel> allModels)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(lookupField);
        ArgumentNullException.ThrowIfNull(allModels);

        if (lookupField.Kind != FieldKind.Lookup || lookupField.Lookup is null) return null;
        return LookupPath.Walk(owner, lookupField.Lookup.Path, allModels).Column;
    }
}
