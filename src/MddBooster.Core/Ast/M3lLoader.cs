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

        var result = M3lNative.ParseMultiToAst(BuildFilesJson(paths));
        return Validate(result, sourceFile: string.Join(";", paths));
    }

    /// <summary>
    /// 같은 소스 집합을 m3l 검증기에 통과시켜 진단을 돌려준다 — 파서가 내는 진단
    /// (<see cref="M3lAst.Warnings"/>)과는 다른 층이다: 등록된 <c>::attribute</c> 오용
    /// (M3L-W005~W008) 같은 규칙은 검증기에만 있다.
    /// </summary>
    /// <remarks>
    /// <see cref="LoadFiles"/> 와 <b>같은 파일 집합을 한 단위로</b> 넘긴다. 파일별로 따로
    /// 검증하면 크로스파일 개념(다른 파일에 선언된 <c>::attribute</c> 레지스트리, 상속,
    /// 인터페이스 참조)을 그 파일 혼자서는 알 수 없어 경고를 놓치거나 오탐한다 — 파싱을
    /// 병합 단위로 하는 이유(§2.1 Rule 3)가 검증에도 그대로 적용된다.
    /// </remarks>
    public ValidateResult ValidateFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            throw new M3lLoadException("M3L 소스가 비어 있습니다.", sourceFile: "");
        }

        var sourceFile = string.Join(";", paths);
        var result = M3lNative.ValidateMultiToResult(BuildFilesJson(paths), "{}");

        if (result is null)
        {
            throw new M3lLoadException("M3L 네이티브 검증기가 null을 반환했습니다.", sourceFile: sourceFile);
        }
        if (!result.Success || result.Data is not ValidateResult diagnostics)
        {
            throw new M3lLoadException(
                result.Error ?? "M3L 검증 실패 (상세 정보 없음)",
                sourceFile: sourceFile);
        }

        return diagnostics;
    }

    /// <summary>
    /// 네이티브가 받는 <c>{ content, filename }</c> 배열. 파싱과 검증이 <b>같은 입력</b>을
    /// 보게 하려고 한 곳에서만 만든다 — 두 곳에서 따로 만들면 한쪽만 바뀌는 날이 온다.
    /// </summary>
    private static string BuildFilesJson(IReadOnlyList<string> paths)
    {
        var files = new List<object>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new M3lLoadException($"M3L 파일을 찾을 수 없습니다: {path}", sourceFile: path);
            }
            files.Add(new { content = File.ReadAllText(path), filename = path });
        }
        return JsonSerializer.Serialize(files);
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
