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
    /// <param name="ColumnNames">
    /// For a table step, every name each stored field of the model has had or has — the column tokens the names
    /// generated for the table's constraints and indexes can carry when the step runs (a column renamed in the same
    /// change is still at an older name then). Empty for a column step.
    /// </param>
    /// <param name="Composites">
    /// The model's unnamed multi-column unique constraints and indexes — the ones whose names this generator derives from
    /// the table and <em>every</em> column (<c>UK_Order_CustomerId_Number</c>). A step that renames the table or one of the
    /// columns changes a token in each such name. Empty when the model has none.
    /// </param>
    public sealed record Step(Kind Kind, string Model, string From, string To, IReadOnlyList<string> ColumnNames,
        IReadOnlyList<CompositeKey>? Composites = null)
    {
        public IReadOnlyList<CompositeKey> Composites { get; } = Composites ?? [];
    }

    /// <summary>An unnamed multi-column unique constraint or index of a model.</summary>
    /// <param name="Unique">Unique constraint (<see langword="true"/>) or plain index.</param>
    /// <param name="Columns">
    /// One list per column, in the entry's order: every name that column has had or has, oldest first (a column renamed
    /// in a change that has not run everywhere yet is still at an older name in some databases).
    /// </param>
    public sealed record CompositeKey(bool Unique, IReadOnlyList<IReadOnlyList<string>> Columns);

    /// <summary>Every step <paramref name="models"/> declares, models in their given order.</summary>
    public static IReadOnlyList<Step> Of(IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var steps = new List<Step>();
        foreach (var model in models)
        {
            var stored = model.Fields.Where(IsStored).ToList();
            var columnNames = stored
                .SelectMany(f => (f.RenamedFrom ?? []).Append(f.Name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var composites = CompositeKeys(model, stored);
            foreach (var (from, to) in History(model.Source.RenamedFrom, model.Name))
                steps.Add(new Step(Kind.Table, model.Name, from, to, columnNames, composites));
            foreach (var field in stored)
                foreach (var (from, to) in History(field.RenamedFrom, field.Name))
                    steps.Add(new Step(Kind.Column, model.Name, from, to, [], composites));
        }
        return steps;
    }

    private static List<CompositeKey> CompositeKeys(ResolvedModel model, IReadOnlyList<FieldNode> stored)
    {
        IReadOnlyList<string> NamesOf(string column)
        {
            var field = stored.FirstOrDefault(f => string.Equals(f.Name, column, StringComparison.Ordinal))
                        ?? stored.FirstOrDefault(f => string.Equals(f.Name, column, StringComparison.OrdinalIgnoreCase));
            return field is null ? [column] : (field.RenamedFrom ?? []).Append(field.Name).Distinct(StringComparer.Ordinal).ToList();
        }

        return SectionIndexParser.Parse(model)
            .Where(e => e.Name is null && e.Columns.Count > 1)
            .Select(e => new CompositeKey(e.IsUnique, e.Columns.Select(NamesOf).ToList()))
            .ToList();
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
