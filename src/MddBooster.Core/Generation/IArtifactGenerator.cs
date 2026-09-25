namespace MddBooster.Core.Generation;

public interface IArtifactGenerator
{
    string Name { get; }

    /// <summary>
    /// Refuses a model this target cannot generate, before any target writes a file. The build
    /// runs every target's check first and generates only when all pass, so a model that one
    /// target rejects leaves every target's previous output as it was — rather than the targets
    /// ahead of it rewritten from the new model and the rest untouched from the old one.
    /// Checks that need the rendered output itself stay in <see cref="Generate"/>; the default
    /// checks nothing.
    /// </summary>
    void Validate(GeneratorContext context) { }

    void Generate(GeneratorContext context);
}
