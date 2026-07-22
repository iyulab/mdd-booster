using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// 로더 회계 — "파싱된 모든 요소는 소비되거나 경고된다". 생성 파이프라인은
/// Models/Enums/Interfaces만 소비하므로, 그 밖의 AST 요소(standalone <c>::view</c>,
/// <c>::flow</c>, extension)는 여기서 열거해 호출자가 경고로 가시화한다.
/// (2026-07-22 이슈: standalone ::view가 무경고로 산출물에서 탈락)
/// </summary>
public static class AstAccounting
{
    /// <summary>생성 파이프라인이 소비하지 않는 요소들의 표시명 목록.</summary>
    public static IReadOnlyList<string> ListUnconsumed(M3lAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var unconsumed = new List<string>();
        unconsumed.AddRange(ast.Views.Select(v => $"::view {v.Name}"));
        unconsumed.AddRange(ast.Flows.Select(f => $"::flow {f.Name}"));
        unconsumed.AddRange(ast.Extensions.Keys.Select(k => $"extension {k}"));
        return unconsumed;
    }
}
