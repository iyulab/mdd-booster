using System.Text.Json;

namespace MddBooster.Core.Ast;

public sealed class M3lLoader
{
    public M3lAst LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new M3lLoadException($"M3L 파일을 찾을 수 없습니다: {path}", sourceFile: path);
        }

        var content = File.ReadAllText(path);
        var result = M3lNative.ParseToAst(content, path);
        return Validate(result, path);
    }

    /// <summary>
    /// 여러 M3L 소스를 한 번에 파싱·해석해 병합 AST를 반환한다.
    /// cross-file 상속·인터페이스 참조는 전체 파일 집합을 하나의 resolve 단위로
    /// 넘겨야 해석된다 (파일별 독립 파싱은 M3L-E007 오탐 — 스펙 §2.1 Rule 3).
    /// </summary>
    public M3lAst LoadFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            throw new M3lLoadException("M3L 소스가 비어 있습니다.", sourceFile: "");
        }

        var files = new List<object>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new M3lLoadException($"M3L 파일을 찾을 수 없습니다: {path}", sourceFile: path);
            }
            files.Add(new { content = File.ReadAllText(path), filename = path });
        }

        var filesJson = JsonSerializer.Serialize(files);
        var result = M3lNative.ParseMultiToAst(filesJson);
        return Validate(result, sourceFile: string.Join(";", paths));
    }

    private static M3lAst Validate(M3lResult<M3lAst>? result, string sourceFile)
    {
        if (result is null)
        {
            throw new M3lLoadException("M3L 네이티브 파서가 null을 반환했습니다.", sourceFile: sourceFile);
        }

        if (!result.Success || result.Data is not M3lAst ast)
        {
            throw new M3lLoadException(
                result.Error ?? "M3L 파싱 실패 (상세 정보 없음)",
                sourceFile: sourceFile);
        }

        if (ast.Errors.Count > 0)
        {
            var messages = ast.Errors.Select(e => $"[{e.Code}] {e.File}:{e.Line}:{e.Col} {e.Message}").ToList();
            throw new M3lLoadException(
                $"M3L 파싱 에러 {ast.Errors.Count}건: {string.Join(" | ", messages)}",
                sourceFile: sourceFile,
                diagnostics: messages);
        }

        return ast;
    }
}
