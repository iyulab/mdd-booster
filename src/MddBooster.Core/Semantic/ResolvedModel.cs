using M3L.Native;

namespace MddBooster.Core.Semantic;

public sealed class ResolvedModel
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<FieldNode> Fields { get; init; }
    public required ModelNode Source { get; init; }
}
