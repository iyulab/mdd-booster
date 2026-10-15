using System.Text.Json;
using M3L.Native;

namespace MddBooster.Core.Ast;

/// <summary>
/// 로더 회계 — "파싱된 모든 요소는 소비되거나 경고된다". 생성 파이프라인은
/// Models/Enums/Interfaces 와 standalone <c>::view</c>(<see cref="MddBooster.Core.Semantic.ViewResolver"/>)를
/// 소비하므로, 그 밖의 AST 요소(<c>::flow</c>, extension, 그리고 어떤 타깃도 읽지 않는 <c>### 섹션</c>)는
/// 여기서 열거해 호출자가 경고로 가시화한다. 섹션 중 소비되는 것은 <c>### Indexes</c> 하나뿐이다.
/// 뷰가 «생성되지 않는» 경우(Sql 타깃 없음·생성하지 않는 모양)는 빌드가 따로 말한다.
/// <para>
/// 속성(attribute) 축은 이 회계 대상이 아니다 — 열린 어휘라 「읽히지 않는 것」의 집합을
/// 열거할 수 없다. 예외는 사용자 프레임워크 속성(<c>`[X]`</c>, 명세 §2.5.6)이다 — 이 생성기는
/// 그중 어느 것도 읽지 않으므로 «읽히지 않는 것» = 선언된 것 전부라 열거할 수 있다. enum 헤더의 부모 목록(<c>## X ::enum : Base</c>)도 대상이 아니다: 파서가
/// 부모의 값을 자식의 <c>Values</c> 로 합쳐 주므로(명세 §3.1.6) 모든 타깃이 상속 멤버를 이미
/// 읽고 있다.
/// </para>
/// </summary>
public static class AstAccounting
{
    private static string FrameworkAttributeText(CustomAttribute attr) =>
        !string.IsNullOrWhiteSpace(attr.Raw) ? attr.Raw.Trim()
        : $"[{attr.Content}]";

    /// <summary>생성 파이프라인이 소비하지 않는 요소들의 표시명 목록.</summary>
    public static IReadOnlyList<string> ListUnconsumed(M3lAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        var unconsumed = new List<string>();
        unconsumed.AddRange(ast.Flows.Select(f => $"::flow {f.Name}"));
        unconsumed.AddRange(ast.Extensions.Keys.Select(k => $"extension {k}"));

        // A header's display label — `## Customer(고객)` — names the model, interface, view or enum itself.
        // Field and enum-value labels reach every target that names them to a person; a header's has no
        // counterpart in any generated artifact (no target writes a display name for a type), so it is
        // said here rather than dropped without a word.
        unconsumed.AddRange(ast.Models.Concat(ast.Interfaces).Concat(ast.Views)
            .Where(m => !string.IsNullOrWhiteSpace(m.Label))
            .Select(m => $"{m.Name}: 머리 라벨 ({m.Label})"));
        unconsumed.AddRange(ast.Enums
            .Where(e => !string.IsNullOrWhiteSpace(e.Label))
            .Select(e => $"{e.Name} ::enum: 머리 라벨 ({e.Label})"));

        // A `### Name` the language does not define lands in Sections.Custom as
        // unstructured entries, and no target can read it — the reader gets the
        // same silence the standalone view used to get. The sections the language
        // does define are left out of this: whether a target *should* consume one
        // is a feature question, not a name nobody can act on.
        var modelsByName = ast.Models.GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var model in ast.Models)
        {
            var sections = model.Sections;
            if (sections is null) continue;

            // `### Indexes` is read in full. `### Behaviors` and `### Metadata` are read by nothing — the
            // same model generated with and without them produces byte-identical output across every
            // target (Sql in both dialects, Model, Api, TypeScript; measured).
            // A `### Relations` entry — under the section or written among the fields (§3.2.4) — is read for the navigation names it declares and its on_delete
            // (RelationDeclarations); what it says beyond that — a reverse collection, a many-to-many, load,
            // order_by — no target generates, and each is named.
            unconsumed.AddRange(Semantic.RelationDeclarations.Unread(model, modelsByName));
            if (sections.Behaviors?.Count > 0) unconsumed.Add($"{model.Name}: ### Behaviors");
            if (sections.Metadata?.Count > 0) unconsumed.Add($"{model.Name}: ### Metadata");

            if (sections.Custom is null) continue;
            unconsumed.AddRange(sections.Custom.Keys.Select(k => $"{model.Name}: ### {k}"));
        }

        // Custom framework attributes (§2.5.6) are "processed by implementation-specific parsers", and
        // this implementation processes none: the generator owns how a model becomes C#, SQL and
        // TypeScript, and a raw platform attribute would bypass that mapping (and the portability of
        // the model across targets). Parsed and preserved, then dropped by every target — so it is
        // reported here instead of disappearing. The intent usually has M3L vocabulary already
        // (a row version is the `rowversion` type).
        // The parser copies an interface's fields into each model that takes it, location and all —
        // interfaces go first and a declaration is reported once, where it is written.
        var reported = new HashSet<(string?, int, int, string)>();
        foreach (var owner in ast.Interfaces.Concat(ast.Models))
            foreach (var field in owner.Fields)
                foreach (var attr in field.FrameworkAttrs ?? [])
                {
                    var text = FrameworkAttributeText(attr);
                    if (!reported.Add((field.Loc.File, field.Loc.Line, field.Loc.Col, text))) continue;
                    unconsumed.Add($"{owner.Name}.{field.Name}: 프레임워크 속성 {text} (명세 §2.5.6 — 어떤 타깃도 읽지 않는다)");
                }

        return unconsumed;
    }
}
