using System.Text.Json;
using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// 로더 회계 — "파싱된 모든 요소는 소비되거나 경고된다". 생성 파이프라인은
/// Models/Enums/Interfaces만 소비하므로, 그 밖의 AST 요소(standalone <c>::view</c>,
/// <c>::flow</c>, extension, 그리고 어떤 타깃도 읽지 않는 <c>### 섹션</c>)는 여기서 열거해
/// 호출자가 경고로 가시화한다. 섹션 중 소비되는 것은 <c>### Indexes</c> 하나뿐이다.
/// (2026-07-22 이슈: standalone ::view가 무경고로 산출물에서 탈락)
/// <para>
/// 회계 대상은 «요소»만이 아니다 — 소비되는 요소가 들고 있는 **읽히지 않는 축**도 같은 침묵을
/// 만든다. enum 헤더의 부모 목록(<c>## X ::enum : Base</c>)이 그 경우이고, 그쪽은 더 나쁘다:
/// 산출물이 «없는» 것이 아니라 «틀린» 채로 나온다. 속성(attribute) 축은 여전히 이 회계 대상이
/// 아니다 — 열린 어휘라 「읽히지 않는 것」의 집합을 열거할 수 없다.
/// </para>
/// </summary>
public static class AstAccounting
{
    /// <summary>이 생성기가 관계를 읽는 유일한 형태.</summary>
    public const string RelationReadForm = "@reference(Target)";

    /// <summary>
    /// enum 상속이 산출물에 닿지 않는다는 사실의 정본 문구. 상수로 두는 이유는 테스트가 문자열을
    /// 다시 적지 않게 하려는 것이고, 그래서 이 문장이 바뀌면 한 곳만 바뀐다.
    /// </summary>
    public const string EnumInheritanceUnreadNote =
        "상속 멤버는 합쳐지지 않는다 — 생성 enum 은 자기 블록의 멤버만 갖는다";

    /// <summary>생성 파이프라인이 소비하지 않는 요소들의 표시명 목록.</summary>
    public static IReadOnlyList<string> ListUnconsumed(M3lAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var unconsumed = new List<string>();
        unconsumed.AddRange(ast.Views.Select(v => $"::view {v.Name}"));
        unconsumed.AddRange(ast.Flows.Select(f => $"::flow {f.Name}"));
        unconsumed.AddRange(ast.Extensions.Keys.Select(k => $"extension {k}"));

        // An enum header's parent list is the worst-behaved unread axis in the language: the
        // specification defines inheritance as joining the parent's *values* (§3.1.6, "Enums can
        // inherit values from other enums"), nothing upstream of this generator flattens them, and
        // no target reads the parent name. So the declaration parses, the build succeeds, and the
        // generated enum silently holds only the members its own block declares. Every other entry
        // in this list costs the author an artifact that was never produced; this one hands them an
        // artifact that is wrong. Until flattening lands where the values are defined, saying so is
        // the whole of what this generator can honestly do.
        foreach (var e in ast.Enums)
            foreach (var parent in e.Inherits)
                unconsumed.Add($"::enum {e.Name} : {parent} ({EnumInheritanceUnreadNote})");

        // A `### Name` the language does not define lands in Sections.Custom as
        // unstructured entries, and no target can read it — the reader gets the
        // same silence the standalone view used to get. The sections the language
        // does define are left out of this: whether a target *should* consume one
        // is a feature question, not a name nobody can act on.
        foreach (var model in ast.Models)
        {
            var sections = model.Sections;
            if (sections is null) continue;

            // `sections.relations` mixes two origins: a `### Relations` section
            // (§3.2.3) and relationship notation the parser lifted out of the
            // field list (§3.2.2/§3.2.4, e.g. `- <>tags: many-to-many`) — the
            // latter carries `declaredIn: "fields"`, the former does not. Report
            // the field-position kind by its own text, the way a misplaced field
            // used to be named, so the reader still learns what to change and
            // not just that something in the model went unread.
            var relations = sections.Relations ?? [];
            var fieldNotations = relations
                .Where(r => r.TryGetProperty("declaredIn", out var d) && d.GetString() == "fields")
                .ToList();
            foreach (var r in fieldNotations)
            {
                var raw = r.TryGetProperty("raw", out var rw) ? rw.GetString() : r.ToString();
                unconsumed.Add($"{model.Name}: 관계 표기 '{raw}' (관계는 {RelationReadForm} 로만 읽는다)");
            }

            // Indexes is the one section a target reads. The other three the
            // language defines are read by nothing — the same model generated with
            // and without all three produces byte-identical output across every
            // target (Sql in both dialects, Model, Api, TypeScript; measured).
            if (relations.Count > fieldNotations.Count) unconsumed.Add($"{model.Name}: ### Relations");
            if (sections.Behaviors?.Count > 0) unconsumed.Add($"{model.Name}: ### Behaviors");
            if (sections.Metadata?.Count > 0) unconsumed.Add($"{model.Name}: ### Metadata");

            if (sections.Custom is null) continue;
            unconsumed.AddRange(sections.Custom.Keys.Select(k => $"{model.Name}: ### {k}"));
        }

        return unconsumed;
    }
}
