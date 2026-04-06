namespace MddBooster.Core.Ast;

public sealed class M3lLoadException : Exception
{
    public string? SourceFile { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    public M3lLoadException(string message, string? sourceFile = null, IReadOnlyList<string>? diagnostics = null)
        : base(message)
    {
        SourceFile = sourceFile;
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }
}
