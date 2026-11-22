using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// The label a field carries into a generated TypeScript file — the one place that decides it, so the field schema and the entity
/// description cannot disagree about what a field is called.
/// </summary>
internal static class FieldLabels
{
    /// <summary>
    /// The text the author wrote to name the field, or <see langword="null"/> when they wrote none (nothing is made up from the
    /// field's name).
    /// </summary>
    /// <remarks>
    /// A lookup with no label of its own borrows the one on the field it reads: the target already names this value, and making
    /// the author repeat that string is how the two drift. An explicit label always wins. A rollup is an aggregate and a computed
    /// is an expression, so neither has a single target field whose name would apply.
    /// </remarks>
    internal static string? Authored(FieldNode field, ResolvedModel owner, IReadOnlyList<ResolvedModel> allModels)
    {
        var label = MddBooster.Core.Ast.FieldAttributes.DisplayLabel(field);
        if (label is null && field.Kind == FieldKind.Lookup)
        {
            var target = LookupTarget.Resolve(owner, field, allModels);
            if (target is not null)
                label = MddBooster.Core.Ast.FieldAttributes.DisplayLabel(target);
        }
        return label;
    }
}
