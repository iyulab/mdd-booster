using M3L.Native;

namespace MddBooster.Generators.TypeScript;

/// <summary>
/// How an entity's fields fall into sections — the one rule the generated form and the entity description both read, so a
/// section heading is the same in both because it comes from the same place, not because two copies were kept alike.
/// </summary>
/// <remarks>
/// Fields are grouped by their effective group (<see cref="MddBooster.Core.Ast.FieldAttributes.EffectiveGroup"/>), the groups
/// kept in the order they first appear. A field with no group falls into <see cref="DefaultSection"/>; an explicit
/// <c>@group("기타")</c> merges into the same bucket.
/// </remarks>
internal static class EntitySections
{
    /// <summary>The section that holds the fields with no group of their own.</summary>
    internal const string DefaultSection = "기타";

    /// <summary>One section: the bucket's name and its fields in declaration order.</summary>
    internal sealed record Section(string Name, IReadOnlyList<FieldNode> Fields);

    /// <summary>
    /// The sections of <paramref name="fields"/>, in the order their names first appear, and whether the heading is dropped.
    /// </summary>
    /// <remarks>
    /// The heading is dropped when every field fell into the implicit default section: with one section and nothing to tell it
    /// apart from, a name for "the rest" reads as noise. An author-written group — even <c>@group("기타")</c> — is a name
    /// someone chose, and stays. The bucket keeps its name either way: a consumer keys per-section props by it.
    /// </remarks>
    internal static (IReadOnlyList<Section> Sections, bool UntitledDefault) Group(IReadOnlyList<FieldNode> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var order = new List<string>();
        var byName = new Dictionary<string, List<FieldNode>>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var name = MddBooster.Core.Ast.FieldAttributes.EffectiveGroup(field) ?? DefaultSection;
            if (!byName.TryGetValue(name, out var bucket))
            {
                bucket = [];
                byName[name] = bucket;
                order.Add(name);
            }
            bucket.Add(field);
        }

        var untitledDefault = order.Count == 1
            && fields.All(f => MddBooster.Core.Ast.FieldAttributes.EffectiveGroup(f) is null);

        return ([.. order.Select(name => new Section(name, byName[name]))], untitledDefault);
    }
}
