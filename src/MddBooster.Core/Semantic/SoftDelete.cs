using M3L.Native;

namespace MddBooster.Core.Semantic;

/// <summary>
/// The one definition of «this model soft-deletes»: it has a stored <c>deleted_at</c> field.
/// Every generator that treats such a model differently — the UdView layer, the entity's
/// backing view, a rollup that must not count deleted rows — asks this, so they cannot
/// disagree about which models are soft-deleting.
/// </summary>
public static class SoftDelete
{
    /// <summary>The stored field whose presence turns soft-delete on, as written in the model.</summary>
    public const string FieldName = "deleted_at";

    public static bool IsEnabled(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Fields.Any(f =>
            f.Kind == FieldKind.Stored && string.Equals(f.Name, FieldName, StringComparison.Ordinal));
    }
}
