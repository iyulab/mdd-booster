using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>One FK step of a lookup path: the key on <see cref="Owner"/> and the model it references.</summary>
public sealed record LookupHop(ResolvedModel Owner, FieldNode FkField, ResolvedModel Target);

/// <summary>Why a lookup path did not resolve.</summary>
public enum LookupPathFailure
{
    /// <summary>The path resolved.</summary>
    None,
    /// <summary>Not <c>fk.column</c> or <c>fk.fk….column</c> — too few segments, or an empty one.</summary>
    Malformed,
    /// <summary>A hop names a field its model does not have.</summary>
    MissingFk,
    /// <summary>A hop's field has no <c>@reference(Target)</c>.</summary>
    NoReference,
    /// <summary>A hop references a model outside the resolved set.</summary>
    UnknownTarget,
    /// <summary>The last segment names a field the final model does not have.</summary>
    MissingColumn,
}

/// <summary>
/// The result of walking a lookup path. On success <see cref="Hops"/> holds every step and
/// <see cref="Column"/> the field read at the end; on failure <see cref="Failure"/> says why and
/// <see cref="FailedAt"/>/<see cref="FailedSegment"/> say where.
/// </summary>
public sealed record LookupPathWalk(
    IReadOnlyList<LookupHop> Hops,
    FieldNode? Column,
    LookupPathFailure Failure,
    ResolvedModel? FailedAt,
    string? FailedSegment)
{
    /// <summary>The model the last hop lands on, or <c>null</c> when the walk failed before any hop.</summary>
    public ResolvedModel? Target => Hops.Count == 0 ? null : Hops[^1].Target;
}

/// <summary>
/// The one reading of an <c>@lookup</c> path. A path is one or more FK hops followed by a column:
/// <c>customer_id.name</c> is one hop, <c>order_id.customer_id.name</c> two (M3L §4.5.1). Each hop's
/// field must carry <c>@reference(Target)</c>, and the walk steps into that model before reading
/// the next segment.
/// </summary>
/// <remarks>
/// Every consumer of a lookup path — the analyzer's diagnostics, SQL view rendering, the view-cycle
/// detector, nullability on the generated types — reads it through here. Each used to split at the
/// first <c>.</c> on its own, which is how a two-hop path came to be read as a one-hop path to a
/// column literally named <c>customer_id.name</c>.
/// </remarks>
public static class LookupPath
{
    /// <summary>
    /// Splits <paramref name="path"/> into its FK segments and final column, or returns
    /// <c>false</c> for anything that is not at least <c>fk.column</c> with no empty segment.
    /// </summary>
    public static bool TrySplit(string? path, out IReadOnlyList<string> fkSegments, out string column)
    {
        fkSegments = [];
        column = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var segments = path.Split('.');
        if (segments.Length < 2 || segments.Any(string.IsNullOrEmpty)) return false;

        fkSegments = segments[..^1];
        column = segments[^1];
        return true;
    }

    /// <summary>Walks <paramref name="path"/> from <paramref name="owner"/> through <paramref name="allModels"/>.</summary>
    public static LookupPathWalk Walk(ResolvedModel owner, string? path, IReadOnlyList<ResolvedModel> allModels)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(allModels);

        if (!TrySplit(path, out var fkSegments, out var columnName))
            return new LookupPathWalk([], null, LookupPathFailure.Malformed, owner, path);

        var hops = new List<LookupHop>(fkSegments.Count);
        var current = owner;
        foreach (var segment in fkSegments)
        {
            var fkField = current.Fields.FirstOrDefault(f => f.Name == segment);
            if (fkField is null)
                return new LookupPathWalk(hops, null, LookupPathFailure.MissingFk, current, segment);

            var targetName = ReferenceTarget(fkField);
            if (targetName is null)
                return new LookupPathWalk(hops, null, LookupPathFailure.NoReference, current, segment);

            var target = allModels.FirstOrDefault(m => m.Name == targetName);
            if (target is null)
                return new LookupPathWalk(hops, null, LookupPathFailure.UnknownTarget, current, targetName);

            hops.Add(new LookupHop(current, fkField, target));
            current = target;
        }

        var column = current.Fields.FirstOrDefault(f => f.Name == columnName);
        return column is null
            ? new LookupPathWalk(hops, null, LookupPathFailure.MissingColumn, current, columnName)
            : new LookupPathWalk(hops, column, LookupPathFailure.None, null, null);
    }

    /// <summary>
    /// Whether <paramref name="lookupField"/>'s value can be absent: declared optional, or read
    /// through an optional key at any hop (M3L §4.5.4 nullable propagation — the view joins each hop
    /// with <c>LEFT JOIN</c>, so a missing link anywhere yields NULL). Walks as far as the path
    /// resolves: a hop that does not resolve is the analyzer's to report, and the keys before it
    /// still decide.
    /// </summary>
    public static bool ResultNullable(ResolvedModel owner, FieldNode lookupField, IReadOnlyList<ResolvedModel> allModels)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(lookupField);
        ArgumentNullException.ThrowIfNull(allModels);

        if (lookupField.Nullable) return true;
        if (lookupField.Kind != FieldKind.Lookup || !TrySplit(lookupField.Lookup?.Path, out var fkSegments, out _))
            return false;

        var current = owner;
        foreach (var segment in fkSegments)
        {
            var fkField = current.Fields.FirstOrDefault(f => f.Name == segment);
            if (fkField is null) return false;
            if (fkField.Nullable) return true;

            var targetName = ReferenceTarget(fkField);
            var target = targetName is null ? null : allModels.FirstOrDefault(m => m.Name == targetName);
            if (target is null) return false;
            current = target;
        }
        return false;
    }

    /// <summary>The model named by <paramref name="field"/>'s <c>@reference(Target)</c>, or <c>null</c>.</summary>
    public static string? ReferenceTarget(FieldNode field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var refAttr = Ast.FieldAttributes.Find(field, "reference");
        if (refAttr?.Args is null || refAttr.Args.Count == 0) return null;

        var first = refAttr.Args[0];
        var name = first.ValueKind == System.Text.Json.JsonValueKind.String ? first.GetString() : first.GetRawText();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
