using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>
/// Resolves an <c>@lookup(fk.column)</c> declaration to the field it points at.
/// </summary>
/// <remarks>
/// The walk — split the path, find the FK field on the owning model, read its
/// <c>@reference(Target)</c>, find that model, find the column — is the same one
/// <see cref="SemanticAnalyzer"/> performs while emitting MDD003–MDD006. This type exists so a
/// consumer that needs the *target* rather than a diagnostic does not re-implement it: two copies
/// of a path walk are two things that drift apart the first time the path syntax gains a case.
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

        var path = lookupField.Lookup.Path;
        if (string.IsNullOrWhiteSpace(path)) return null;

        var dot = path.IndexOf('.');
        if (dot <= 0 || dot >= path.Length - 1) return null;

        var fkFieldName = path[..dot];
        var targetColumn = path[(dot + 1)..];

        var fkField = owner.Fields.FirstOrDefault(f => f.Name == fkFieldName);
        if (fkField is null) return null;

        var refAttr = Ast.FieldAttributes.Find(fkField, "reference");
        if (refAttr?.Args is null || refAttr.Args.Count == 0) return null;

        var targetName = refAttr.Args[0].ValueKind == System.Text.Json.JsonValueKind.String
            ? refAttr.Args[0].GetString()
            : refAttr.Args[0].GetRawText();
        if (string.IsNullOrEmpty(targetName)) return null;

        var targetModel = allModels.FirstOrDefault(m => m.Name == targetName);
        return targetModel?.Fields.FirstOrDefault(f => f.Name == targetColumn);
    }
}
