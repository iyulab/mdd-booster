using M3L.Native;
using MddBooster.Core.Ast;

namespace MddBooster.Core.Semantic;

public sealed class InterfaceResolver
{
    private readonly M3lAst _ast;
    private readonly Dictionary<string, ModelNode> _interfacesByName;

    public InterfaceResolver(M3lAst ast)
    {
        _ast = ast ?? throw new ArgumentNullException(nameof(ast));
        _interfacesByName = ast.Interfaces.ToDictionary(i => i.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<ResolvedModel> ResolveAll()
    {
        return _ast.Models.Select(Resolve).ToList();
    }

    public ResolvedModel Resolve(ModelNode model)
    {
        // M3L.Native already flattens inherited fields into model.Fields (inherited first).
        // We reconstruct the canonical order: own fields first, then inherited.

        // Step 1: collect all field names coming from inherited interfaces.
        var inheritedFieldNames = new HashSet<string>(StringComparer.Ordinal);
        var inheritedFields = new List<FieldNode>();

        foreach (var parentName in model.Inherits)
        {
            if (!_interfacesByName.TryGetValue(parentName, out var parent))
            {
                // MVP에서는 인터페이스가 아닌 부모는 무시 (모델 상속은 향후 지원).
                continue;
            }

            foreach (var field in parent.Fields ?? new List<FieldNode>())
            {
                if (inheritedFieldNames.Add(field.Name))
                {
                    inheritedFields.Add(field);
                }
            }
        }

        // Step 2: own fields = fields in model.Fields that are NOT from any interface.
        var allModelFields = model.Fields ?? new List<FieldNode>();
        var ownFields = allModelFields
            .Where(f => !inheritedFieldNames.Contains(f.Name))
            .ToList();

        // Step 3: merge — own first, then inherited (from interface source for canonical def).
        var merged = new List<FieldNode>(ownFields.Count + inheritedFields.Count);
        merged.AddRange(ownFields);
        merged.AddRange(inheritedFields);

        // Step 4: 이 생성기가 읽지 않는 관계 표기 필드를 떨군다. AstAccounting 이 로드
        // 시점에 이미 경고로 가시화했으므로 조용한 탈락이 아니다 — 흘려보내면 렌더러가
        // 「타입이 없습니다」라는 다른 층위의 오류를 내고, 명세대로 쓴 사람은 「관계는
        // @reference 로만 읽는다」를 끝내 못 듣는다. 상세: M3lRelationNotation.
        merged.RemoveAll(f => M3lRelationNotation.IsRelationNotation(f));

        return new ResolvedModel
        {
            Name = model.Name,
            Description = model.Description,
            Fields = merged,
            Source = model,
        };
    }
}
