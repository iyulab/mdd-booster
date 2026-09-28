namespace MddBooster.Core.Generation;

/// <summary>What a generator refused, which decides what the user has to change.</summary>
public enum GeneratorRefusalKind
{
    /// <summary>The model: this target cannot express it as written.</summary>
    Model,

    /// <summary>The target's configuration, or the files it points at.</summary>
    Configuration,
}

/// <summary>
/// A generator declining to generate — during <see cref="IArtifactGenerator.Validate"/>, before
/// anything is written — as opposed to failing unexpectedly.
/// </summary>
/// <remarks>
/// The kind carries to the build's exit code: a model refusal stops it like a semantic error, a
/// configuration refusal like any other configuration error. Derives from
/// <see cref="InvalidOperationException"/>, which is what these refusals were thrown as before.
/// </remarks>
public sealed class GeneratorRefusalException(GeneratorRefusalKind kind, string message)
    : InvalidOperationException(message)
{
    public GeneratorRefusalKind Kind { get; } = kind;
}
