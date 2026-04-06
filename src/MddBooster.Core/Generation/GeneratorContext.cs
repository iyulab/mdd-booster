using M3L.Native;
using MddBooster.Core.Semantic;

namespace MddBooster.Core.Generation;

public sealed class GeneratorContext
{
    public required IReadOnlyList<ResolvedModel> Models { get; init; }
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Top-level enum declarations collected from every M3L source. Referenced
    /// by entity fields via their <c>Type</c> string (e.g. a field typed
    /// <c>OrderStatus</c> refers to the enum of the same name). Generators use
    /// this list to emit C# enum files, SQL CHECK constraints, and to route
    /// enum-typed fields in the type mappers.
    /// </summary>
    public IReadOnlyList<EnumNode> Enums { get; init; } = Array.Empty<EnumNode>();
}
