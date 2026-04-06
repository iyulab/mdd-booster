namespace MddBooster.Core.Generation;

public interface IArtifactGenerator
{
    string Name { get; }
    void Generate(GeneratorContext context);
}
