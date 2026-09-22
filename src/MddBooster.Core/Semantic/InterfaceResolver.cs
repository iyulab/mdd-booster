using System.Text.Json;
using M3L.Native;

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

        var aspectBase = model.Base is { Kind: AspectKind } b ? b.Model : null;
        if (aspectBase is not null) merged.Insert(0, SynthesizeAspectKey(aspectBase, model.Loc));

        return new ResolvedModel
        {
            Name = model.Name,
            Description = model.Description,
            Fields = merged,
            Source = model,
            AspectBase = aspectBase,
        };
    }

    private const string AspectKind = "aspect";

    /// <summary>
    /// <c>::aspect(Base)</c> 가 만드는 키 필드. 작성자가 쓰지 않았지만 이후의 모든 단계는
    /// 이것을 <b>보통 필드로</b> 본다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 합성을 여기서 하는 이유: 「aspect = PK 가 곧 base FK 인 테이블」이 <b>구성에 의해</b>
    /// 참이 된다. 렌더러마다 aspect 를 특별 취급하면 같은 사실을 네 곳이 각자 말하게 되고,
    /// 그중 하나가 빠져도 나머지 셋이 통과시킨다. 필드로 만들어 두면 <c>ModelPrimaryKey</c> 가
    /// 찾고, PG 렌더러의 FK 경로가 그대로 태우고, T-SQL 의 컬럼 경로가 그대로 낸다 —
    /// 공유 PK 확장 테이블을 이미 다루던 길이 그것이다.
    /// </para>
    /// <para>
    /// 맨 앞에 넣는다. PK 는 테이블의 첫 컬럼이라는 이 리포의 기존 산출 순서와 같고,
    /// 작성자 필드의 상대 순서는 바뀌지 않는다.
    /// </para>
    /// </remarks>
    private static FieldNode SynthesizeAspectKey(string baseName, SourceLocation loc) => new()
    {
        Name = AspectNaming.KeyFieldName(baseName),
        Type = "identifier",
        Kind = FieldKind.Stored,
        Nullable = false,
        Loc = loc,
        Attributes =
        [
            new FieldAttribute { Name = "pk", Args = [] },
            new FieldAttribute { Name = "reference", Args = [JsonDocument.Parse($"\"{baseName}\"").RootElement] },
        ],
    };
}
