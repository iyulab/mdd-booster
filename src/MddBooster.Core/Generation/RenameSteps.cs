using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

/// <summary>
/// The renames a model set declares with <c>@renamed_from(...)</c> (M3L specification §2.2.6), as the steps that take
/// storage from each former name to the next — in the order an implementation replays them.
/// </summary>
/// <remarks>
/// <para>
/// A history is oldest first: <c>@renamed_from(level, tier)</c> on <c>tier_code</c> is two steps, <c>level → tier</c>
/// then <c>tier → tier_code</c>. A model's own renames come before its fields', and a field step names the table by its
/// current name — by the time it runs, the table step before it has already moved the table.
/// </para>
/// <para>
/// Only what is stored under a name is renamed: a stored field, or a persisted computed one. The language reports a
/// declaration anywhere else (<c>M3L-E031</c>), so none reaches here; the filter is the generator's own guard.
/// </para>
/// </remarks>
public static class RenameSteps
{
    public enum Kind { Table, Column }

    /// <param name="Model">The model the step belongs to, by its current name.</param>
    /// <param name="From">The name storage is at before the step (a model name, or a field name as written).</param>
    /// <param name="To">The name it is at after.</param>
    public sealed record Step(Kind Kind, string Model, string From, string To);

    /// <summary>Every step <paramref name="models"/> declares, models in their given order.</summary>
    public static IReadOnlyList<Step> Of(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var steps = new List<Step>();
        foreach (var model in models)
        {
            foreach (var (from, to) in History(model.Source.RenamedFrom, model.Name))
                steps.Add(new Step(Kind.Table, model.Name, from, to));
            foreach (var field in model.Fields.Where(IsStored))
                foreach (var (from, to) in History(field.RenamedFrom, field.Name))
                    steps.Add(new Step(Kind.Column, model.Name, from, to));
        }
        return steps;
    }

    private static IEnumerable<(string From, string To)> History(IReadOnlyList<string>? formerNames, string current)
    {
        if (formerNames is not { Count: > 0 }) yield break;
        for (var i = 0; i < formerNames.Count; i++)
            yield return (formerNames[i], i + 1 < formerNames.Count ? formerNames[i + 1] : current);
    }

    private static bool IsStored(FieldNode field)
        => field.Kind == FieldKind.Stored
           || (field.Kind == FieldKind.Computed && FieldAttributes.Has(field, "persisted"));
}
