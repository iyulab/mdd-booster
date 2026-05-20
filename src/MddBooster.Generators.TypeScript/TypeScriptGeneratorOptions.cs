namespace MddBooster.Generators.TypeScript;

public sealed class TypeScriptGeneratorOptions
{
    /// <summary>
    /// Absolute or relative path to the output directory where the generated
    /// <c>entities_gen.ts</c>, <c>enums_gen.ts</c>, and <c>entity_names_gen.ts</c>
    /// files will be written.
    /// </summary>
    public required string OutputPath { get; init; }
}
