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

    private static void CheckBase(ResolvedModel model, List<SemanticDiagnostic> diagnostics)
    {
        if (model.Source.Base is not { } b) return;
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
