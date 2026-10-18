using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>One module of a Model target: the owners (<c># Prefix:</c>) it holds, its project, its namespace.</summary>
public sealed record ModelModule(IReadOnlyList<string> Owners, string ProjectPath, string Namespace);

/// <summary>
/// Which module each model, enum and standalone view of a model set belongs to — by its owner (the declaring file's
/// <c># Prefix:</c>). The modules are listed lowest layer first. Every owner the set has belongs to exactly one module,
/// and every module owner is one the set has; anything else is refused rather than dropped or guessed.
/// </summary>
public sealed class ModelModules
{
    private readonly Dictionary<string, int> _moduleOfOwner;

    private ModelModules(IReadOnlyList<ModelModule> modules, Dictionary<string, int> moduleOfOwner)
    {
        Modules = modules;
        _moduleOfOwner = moduleOfOwner;
    }

    public IReadOnlyList<ModelModule> Modules { get; }

    /// <summary>The index of <paramref name="owner"/>'s module — 0 is the lowest layer.</summary>
    public int IndexOf(string owner) => _moduleOfOwner[owner];

    public int IndexOf(ResolvedModel model) => IndexOf(EntitySurfaceFilter.OwnerOf(model));

    public int IndexOf(EnumNode e) => IndexOf(e.Prefix ?? "");

    public int IndexOf(ResolvedView view) => IndexOf(view.Source.Prefix ?? "");

    /// <summary>
    /// The namespaces a module's generated files import: those of the modules below it, whose types its types may name.
    /// </summary>
    public IReadOnlyList<string> ImportsOf(int module) =>
        Modules.Take(module).Select(m => m.Namespace).Where(ns => ns != Modules[module].Namespace).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Assigns every declaration of the set to a module, or refuses the configuration with every reason at once.</summary>
    public static ModelModules Assign(
        IReadOnlyList<ModelModule> modules,
        IReadOnlyList<ResolvedModel> models,
        IReadOnlyList<EnumNode> enums,
        IReadOnlyList<ResolvedView> readViews)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var problems = new List<string>();
        if (modules.Count == 0) problems.Add("modules 가 비어 있습니다 — 모듈로 나누지 않으려면 키를 빼세요.");

        var moduleOfOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (string.IsNullOrWhiteSpace(module.ProjectPath)) problems.Add($"modules[{i}]: projectPath 가 없습니다.");
            if (string.IsNullOrWhiteSpace(module.Namespace)) problems.Add($"modules[{i}]: namespace 가 없습니다.");
            if (module.Owners.Count == 0) problems.Add($"modules[{i}]: owners 가 비어 있습니다.");
            foreach (var owner in module.Owners)
            {
                if (!moduleOfOwner.TryAdd(owner, i))
                    problems.Add($"소유자 {Label(owner)} 가 modules[{moduleOfOwner[owner]}] 와 modules[{i}] 에 함께 있습니다 — 한 소유자는 한 모듈에만.");
            }
        }

        // The owners the set actually has — a model's, an enum's, a view's. Interfaces are not generated types.
        var present = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in models) present.Add(EntitySurfaceFilter.OwnerOf(m));
        foreach (var e in enums) present.Add(e.Prefix ?? "");
        foreach (var v in readViews) present.Add(v.Source.Prefix ?? "");

        foreach (var owner in present.Where(o => !moduleOfOwner.ContainsKey(o)))
            problems.Add($"소유자 {Label(owner)} 의 선언이 어느 모듈에도 없습니다 — 그 선언을 낼 모듈의 owners 에 넣으세요.");
        foreach (var owner in moduleOfOwner.Keys.Where(o => !present.Contains(o)).Order(StringComparer.Ordinal))
            problems.Add($"modules[{moduleOfOwner[owner]}] 의 소유자 {Label(owner)} 는 모델에 없습니다(있는 소유자: {string.Join(", ", present.Select(Label))}).");

        if (problems.Count > 0)
            throw new GeneratorRefusalException(GeneratorRefusalKind.Configuration, "Model 타깃의 modules:\n  " + string.Join("\n  ", problems));
        return new ModelModules(modules, moduleOfOwner);
    }

    private static string Label(string owner) => owner.Length == 0 ? "\"\"(접두어 없는 파일)" : $"'{owner}'";
}

/// <summary>
/// Places a generated C# file into a module: its file-scoped namespace becomes the module's, and it imports the
/// namespaces whose types it may name. The renderers write one namespace; this is the one seam that moves a file.
/// </summary>
internal static class GeneratedFilePlacement
{
    public static string Place(string source, string ns, IReadOnlyList<string> imports)
    {
        var lineEnd = source.Contains("\r\n", StringComparison.Ordinal) ? "\r" : "";
        var lines = source.Split('\n').ToList();
        var nsLine = lines.FindIndex(l => l.StartsWith("namespace ", StringComparison.Ordinal));
        if (nsLine < 0) throw new InvalidOperationException("A generated file without a namespace declaration.");
        lines[nsLine] = $"namespace {ns};{lineEnd}";
        return AddImports(lines, nsLine, imports.Where(i => i != ns).ToList(), lineEnd);
    }

    private static string AddImports(List<string> lines, int ns, IReadOnlyList<string> namespaces, string lineEnd)
    {
        if (namespaces.Count == 0) return string.Join('\n', lines);
        var usings = namespaces.Select(n => $"using {n};{lineEnd}").ToList();
        // After the file's own usings when it has some, otherwise as a block of their own before the namespace.
        var lastUsing = lines.FindLastIndex(ns, l => l.StartsWith("using ", StringComparison.Ordinal));
        if (lastUsing >= 0) lines.InsertRange(lastUsing + 1, usings);
        else lines.InsertRange(ns, usings.Append(lineEnd));
        return string.Join('\n', lines);
    }
}
