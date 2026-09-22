namespace MddBooster.Core.Semantic;

/// <summary>
/// <c>::aspect</c> 가 <b>이 생성기에</b> 지우는 규칙. base 쪽에 생기는 navigation 의 이름에
/// 관한 것뿐이다.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>여기에 들어오지 «않는» 것이 더 중요하다.</b> base 가 실재하는가, aspect 가 다른
/// aspect 의 base 가 될 수 있는가 — 이것들은 <b>언어의 규칙</b>이고 M3L 이 이미 거절한다
/// (<c>M3L-E016</c>–<c>E018</c>). 같은 판정을 여기서 한 번 더 하면 두 곳이 같은 말을 하다가
/// 어느 한쪽만 바뀌는 날 어긋나고, 그 어긋남을 말해 주는 것이 없다. 실제로 그 규칙들을 여기
/// 먼저 썼다가 M3L 이 이미 거절하는 것을 실측으로 확인하고 걷어냈다.
/// </para>
/// <para>
/// 남은 둘은 언어가 답할 수 없는 것이다 — <c>&lt;Base&gt;&lt;Name&gt;</c> 약속과 navigation
/// 이름 충돌은 <b>생성되는 코드</b>에 대한 사실이고, 모델만 보아서는 문제가 아니다.
/// </para>
/// <para>
/// 이름 약속이 경고인 이유: 오류로 막으면 기존 테이블의 개명을 강요하고, is-a 성격이라 임시로
/// aspect 에 둔 모델은 <c>::subtype</c> 이 열릴 때 이름이 되돌아가 마이그레이션이 두 번이
/// 된다. 모델명 접두어를 경고로 둔 <c>MDD017</c> 과 같은 판단이다. 충돌은 반대로 오류다 —
/// 같은 이름의 멤버 둘은 «덜 좋은 이름»이 아니라 컴파일되지 않는 코드다.
/// </para>
/// </remarks>
internal static class AspectRules
{
    private const string AspectKind = "aspect";

    public static void Check(IReadOnlyList<ResolvedModel> models, List<SemanticDiagnostic> diagnostics)
    {
        var byName = new Dictionary<string, ResolvedModel>(StringComparer.Ordinal);
        foreach (var model in models) byName[model.Name] = model;

        // base 이름 → (navigation 이름 → 그 이름을 낸 aspect). 충돌 판정은 한 base 안에서만
        // 의미가 있다 — 다른 base 의 같은 이름은 서로 다른 타입의 서로 다른 멤버다.
        var navByBase = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            if (model.Source.Base is not { } b) continue;
            if (!string.Equals(b.Kind, AspectKind, StringComparison.Ordinal)) continue;

            // base 가 없는 모델은 M3L-E017 이 이미 거절했으므로 여기까지 오지 않는다.
            if (!byName.TryGetValue(b.Model, out var baseModel)) continue;

            var (nav, matchesConvention) = AspectNaming.ReverseNavigation(model, b.Model);

            if (!matchesConvention)
            {
                var prefix = AspectNaming.PascalPrefix(model);
                var suggested = prefix.Length == 0 ? $"{b.Model}<Name>" : $"{prefix}{b.Model}<Name>";
                diagnostics.Add(new SemanticDiagnostic("MDD019",
                    $"'{model.Name}' ::aspect({b.Model}): 이름 약속은 '{suggested}' 입니다 — 어긋나면 '{b.Model}' 쪽 navigation 이 모델명 전체('{nav}')가 됩니다.",
                    model.Source.Loc, SemanticSeverity.Warning));
            }

            var taken = navByBase.TryGetValue(b.Model, out var existing)
                ? existing
                : navByBase[b.Model] = new Dictionary<string, string>(StringComparer.Ordinal);

            if (taken.TryGetValue(nav, out var owner))
            {
                diagnostics.Add(new SemanticDiagnostic("MDD020",
                    $"'{model.Name}' 와 '{owner}' 가 '{b.Model}' 에 같은 navigation 이름 '{nav}' 를 냅니다 — 한쪽의 모델명을 바꾸십시오.",
                    model.Source.Loc));
                continue;
            }

            if (baseModel.Fields.Any(f => string.Equals(f.Name, nav, StringComparison.Ordinal)))
            {
                diagnostics.Add(new SemanticDiagnostic("MDD020",
                    $"'{model.Name}' 가 '{b.Model}' 에 내는 navigation 이름 '{nav}' 가 '{b.Model}' 의 기존 필드와 충돌합니다 — 모델명을 바꾸십시오.",
                    model.Source.Loc));
                continue;
            }

            taken[nav] = model.Name;
        }
    }
}
