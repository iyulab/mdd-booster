using MddBooster.Core.Generation;
using M3L.Native;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// Model 타깃 파이프라인 진입 게이트. 생성은 성공하지만 런타임에 파탄나는
/// 모델(조용한 실패)을 생성 시점의 명시적 오류로 끌어올린다.
/// 렌더러 내부 정합 게이트(EntityPairRenderer의 PK 게이트)와 달리, 여기는
/// IyuEntity/IyuDbContext 런타임 계약과의 정합을 검증하는 자리다.
/// </summary>
public static class ModelTargetValidator
{
    /// <summary>
    /// <c>IyuEntity</c>가 항상 매핑하는 감사 타임스탬프.
    /// 타임스탬프 인터셉터가 모든 엔티티에서 CreatedAt/UpdatedAt 속성에 무조건 접근하므로,
    /// 모델이 이 필드들을 선언하지 않으면 존재하지 않는 컬럼 매핑으로 첫 쿼리/저장에서 실패한다.
    /// </summary>
    private static readonly string[] RequiredTimestamps = ["created_at", "updated_at"];

    public static void Validate(
        IReadOnlyList<ResolvedModel> models,
        IReadOnlyList<ResolvedView>? readViews = null,
        IReadOnlyList<EnumNode>? enums = null,
        string? contextName = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        foreach (var model in models)
        {
            ValidateTimestampContract(model);
        }
        ValidateGeneratedTypeNames(models, readViews ?? [], enums ?? [], contextName);
    }

    /// <summary>
    /// Every type this target writes into one namespace — per model the interface, the write and read types and their
    /// configurations, per standalone view its type and configuration, per enum its type, and the context — must have a
    /// name of its own. Two models <c>Order</c> and <c>OrderExt</c>, or <c>Order</c> and <c>OrderConfiguration</c>,
    /// otherwise generate two classes of one name: one file overwrites the other, or the build stops on a duplicate
    /// definition far from the model that caused it. Compared ignoring case, because the generated files share a folder.
    /// </summary>
    private static void ValidateGeneratedTypeNames(
        IReadOnlyList<ResolvedModel> models, IReadOnlyList<ResolvedView> readViews, IReadOnlyList<EnumNode> enums, string? contextName)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();
        void Claim(string typeName, string owner)
        {
            if (owners.TryAdd(typeName, owner)) return;
            clashes.Add($"'{typeName}' — {owners[typeName]} · {owner}");
        }

        foreach (var model in models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var name = NameCasing.ToPascalCase(model.Name);
            var owner = $"model '{model.Name}'";
            Claim("I" + name, owner);
            Claim(name, owner);
            Claim(name + "Ext", owner);
            Claim(EntityConfiguration.ConfigurationClassName(name), owner);
            Claim(EntityConfiguration.ConfigurationClassName(name + "Ext"), owner);
        }
        foreach (var view in readViews.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            var name = NameCasing.ToPascalCase(view.Name);
            Claim(name, $"view '{view.Name}'");
            Claim(EntityConfiguration.ConfigurationClassName(name), $"view '{view.Name}'");
        }
        foreach (var e in enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            Claim(NameCasing.ToPascalCase(e.Name), $"enum '{e.Name}'");
        if (!string.IsNullOrWhiteSpace(contextName))
            Claim(contextName, "dbContextName");

        if (clashes.Count > 0)
        {
            throw new GeneratorRefusalException(GeneratorRefusalKind.Model,
                "Model 타깃이 다루는 형 둘이 같은 이름입니다(생성 형끼리는 한 폴더의 같은 파일, 참조 형과는 같은 테이블) — 모델 · 뷰 · enum 이름이나 dbContextName 을 바꾸세요:\n  "
                + string.Join("\n  ", clashes));
        }
    }

    private static void ValidateTimestampContract(ResolvedModel model)
    {
        foreach (var required in RequiredTimestamps)
        {
            var declared = model.Fields.Any(f =>
                f.Kind == FieldKind.Stored &&
                string.Equals(f.Name, required, StringComparison.OrdinalIgnoreCase));
            if (!declared)
            {
                throw new GeneratorRefusalException(GeneratorRefusalKind.Model,
                    $"Model '{model.Name}': '{required}' 필드가 없습니다. IyuEntity는 CreatedAt/UpdatedAt을 " +
                    "항상 매핑하므로 미선언 모델은 존재하지 않는 컬럼 매핑으로 런타임에 실패합니다. " +
                    "Timestampable 인터페이스를 상속하거나 두 필드를 직접 선언하세요.");
            }
        }
    }
}
