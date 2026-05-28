namespace MddBooster.Generators.TypeScript;

public sealed class TypeScriptGeneratorOptions
{
    /// <summary>
    /// Absolute or relative path to the output directory for the five
    /// standard TypeScript files (entities_gen.ts, enums_gen.ts, etc.).
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Optional path for generated {Entity}Form_gen.tsx files.
    /// When null, form generation is skipped.
    /// </summary>
    public string? FormsOutputPath { get; init; }
}
