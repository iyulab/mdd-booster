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

        if (result is null)
        {
            throw new M3lLoadException("M3L 네이티브 파서가 null을 반환했습니다.", sourceFile: path);
        }

        if (!result.Success || result.Data is not M3lAst ast)
        {
            throw new M3lLoadException(
                result.Error ?? "M3L 파싱 실패 (상세 정보 없음)",
                sourceFile: path);
        }

        if (ast.Errors.Count > 0)
        {
            var messages = ast.Errors.Select(e => $"[{e.Code}] {e.File}:{e.Line}:{e.Col} {e.Message}").ToList();
            throw new M3lLoadException(
                $"M3L 파싱 에러 {ast.Errors.Count}건: {string.Join(" | ", messages)}",
                sourceFile: path,
                diagnostics: messages);
        }

        return ast;
    }
}
