using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.TestSupport;

/// <summary>
/// Writes each (name, content) pair to a temp directory and loads them through the real
/// parser as one cross-file unit, resolving every model. Test-only helper for scenarios
/// that need more than one source file (e.g. a base model in one file, an extend block in
/// another).
/// </summary>
internal static class TwoFileModel
{
    public static (M3lAst Ast, List<ResolvedModel> Models) Load(params (string Name, string Content)[] files)
    {
        var dir = Directory.CreateTempSubdirectory("mdd-ext-").FullName;
        var paths = new List<string>();
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            paths.Add(path);
        }
        var ast = new M3lLoader().LoadFiles(paths);
        return (ast, new InterfaceResolver(ast).ResolveAll().ToList());
    }
}
