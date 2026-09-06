using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// 로더 회계 — "파싱된 모든 요소는 소비되거나 경고된다". 생성 파이프라인은
/// Models/Enums/Interfaces만 소비하므로, 그 밖의 AST 요소(standalone <c>::view</c>,
/// <c>::flow</c>, extension, 그리고 어떤 타깃도 읽지 않는 <c>### 섹션</c>)는 여기서 열거해
/// 호출자가 경고로 가시화한다. 섹션 중 소비되는 것은 <c>### Indexes</c> 하나뿐이다.
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

        // A `### Name` the language does not define lands in Sections.Custom as
        // unstructured entries, and no target can read it — the reader gets the
        // same silence the standalone view used to get. The sections the language
        // does define are left out of this: whether a target *should* consume one
        // is a feature question, not a name nobody can act on.
        foreach (var model in ast.Models)
        {
            var sections = model.Sections;
            if (sections is null) continue;

            // Indexes is the one section a target reads. The other three the
            // language defines are read by nothing — the same model generated with
            // and without all three produces byte-identical output across every
            // target (Sql in both dialects, Model, Api, TypeScript; measured).
            if (sections.Relations?.Count > 0) unconsumed.Add($"{model.Name}: ### Relations");
            if (sections.Behaviors?.Count > 0) unconsumed.Add($"{model.Name}: ### Behaviors");
            if (sections.Metadata?.Count > 0) unconsumed.Add($"{model.Name}: ### Metadata");

            if (sections.Custom is null) continue;
            unconsumed.AddRange(sections.Custom.Keys.Select(k => $"{model.Name}: ### {k}"));
        }

        return unconsumed;
    }
}
