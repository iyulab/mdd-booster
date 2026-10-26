using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// The layers of a Model target split into modules (<see cref="LayerDirection"/>): the modules in their listed order,
/// and below all of them the referenced sources (<see cref="ReferenceSet"/>). A module names only its own declarations,
/// those of the modules it builds on (every earlier module, or the closure of its <c>dependsOn</c>) and the references.
/// </summary>
public static class ModuleDirection
{
    public static LayerDirection For(ModelModules modules, ReferenceSet references) => new(
        m => references.Contains(m) ? -1 : modules.IndexOf(m),
        e => references.Contains(e) ? -1 : modules.IndexOf(e),
        v => references.Contains(v) ? -1 : modules.IndexOf(v),
        layer => layer < 0 ? "references" : $"modules[{layer}]",
        // The referenced sources are below every module; a module knows itself and the modules it builds on.
        (from, to) => to < 0 || (from >= 0 && modules.Sees(from, to)));

    public static void Validate(
        ModelModules modules,
        ReferenceSet references,
        IReadOnlyList<ResolvedModel> models,
        IReadOnlyList<EnumNode> enums,
        IReadOnlyList<ResolvedView> readViews)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var problems = For(modules, references).Violations(models, enums, readViews);
        if (problems.Count > 0)
            throw new GeneratorRefusalException(GeneratorRefusalKind.Model,
                "Model 타깃의 modules — 위 층을 가리키는 선언:\n  " + string.Join("\n  ", problems));
    }
}
