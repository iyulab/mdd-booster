using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;

namespace MddBooster.Core.Semantic;

/// <summary>
/// Rules for fields that arrived through an <c>::extend</c> block and for the owner prefix a
/// source file declares. The parser records ownership; the naming policy is enforced here.
/// </summary>
internal static class ExtensionRules
{
    public static void Check(IReadOnlyList<ResolvedModel> models, List<SemanticDiagnostic> diagnostics)
    {
        foreach (var model in models)
        {
            CheckBase(model, diagnostics);
            CheckModelName(model, diagnostics);
            CheckExtensionFields(model, diagnostics);
        }
    }

    /// <summary>
    /// base 를 가진 모델 중 <b>이 생성기가 아직 내지 못하는 것</b>을 거절한다.
    /// </summary>
    /// <remarks>
    /// <c>::aspect</c> 는 열렸다 — PK 가 곧 base FK 인 테이블로 낸다. <c>::subtype</c> 은
    /// 아직이다: is-a 의 저장 전략(단일 테이블 / 클래스별 테이블 / 구체 클래스별 테이블)이
    /// 정해지지 않았고, 하나를 고르는 것은 이 생성기가 낼 스키마의 모양을 정하는 일이라
    /// 조용히 하나를 고르는 대신 거절한다. 🔴 <b>kind 로 갈라 두는 것이 요점이다</b> —
    /// 검사를 통째로 걷으면 <c>::subtype</c> 의 보류가 함께 우회된다.
    /// </remarks>
    private static void CheckBase(ResolvedModel model, List<SemanticDiagnostic> diagnostics)
    {
        if (model.Source.Base is not { } b) return;
        if (string.Equals(b.Kind, "aspect", StringComparison.Ordinal)) return;
        diagnostics.Add(new SemanticDiagnostic("MDD016",
            $"'{model.Name}' ::{b.Kind}({b.Model}): 이 생성기는 아직 ::{b.Kind} 모델을 지원하지 않습니다.",
            model.Source.Loc));
    }

    private static void CheckModelName(ResolvedModel model, List<SemanticDiagnostic> diagnostics)
    {
        if (model.Source.Base is not null) return;
        if (model.Source.Prefix is not { Length: > 0 } prefix) return;
        var expected = NameCasing.ToPascalCase(prefix);
        if (model.Name.StartsWith(expected, StringComparison.Ordinal)) return;
        diagnostics.Add(new SemanticDiagnostic("MDD017",
            $"'{model.Name}': 이 파일은 '# Prefix: {prefix}' 를 선언했습니다 — 모델명을 '{expected}' 로 시작하면 소유자가 이름에 드러납니다.",
            model.Source.Loc, SemanticSeverity.Warning));
    }

    private static void CheckExtensionFields(ResolvedModel model, List<SemanticDiagnostic> diagnostics)
    {
        var owner = model.Source.Prefix ?? "";
        var reportedSources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in model.Fields)
        {
            if (field.Origin is not { } origin) continue;
            var extender = origin.Prefix ?? "";

            if (!string.Equals(extender, owner, StringComparison.Ordinal))
            {
                if (extender.Length == 0)
                {
                    if (reportedSources.Add(origin.Source))
                        diagnostics.Add(new SemanticDiagnostic("MDD014",
                            $"'{model.Name}' 는 '# Prefix: {owner}' 파일이 소유합니다 — 이를 확장하는 파일은 자기 '# Prefix:' 를 선언해야 합니다.",
                            field.Loc));
                }
                else if (!field.Name.StartsWith(extender + "_", StringComparison.Ordinal))
                {
                    diagnostics.Add(new SemanticDiagnostic("MDD013",
                        $"'{model.Name}.{field.Name}': 다른 소유자의 모델을 확장하는 필드는 '{extender}_' 로 시작해야 합니다.",
                        field.Loc));
                }
            }

            if (field.Kind == FieldKind.Stored && !field.Nullable
                && string.IsNullOrEmpty(FieldAttributes.EffectiveDefault(field)))
            {
                diagnostics.Add(new SemanticDiagnostic("MDD015",
                    $"'{model.Name}.{field.Name}': 확장 필드는 nullable 이거나 기본값이 있어야 합니다 — 이 컬럼이 추가될 때 '{model.Name}' 의 행이 이미 존재합니다.",
                    field.Loc));
            }
        }
    }
}
