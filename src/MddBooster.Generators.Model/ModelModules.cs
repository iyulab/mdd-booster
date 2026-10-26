using M3L.Native;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>
/// One module of a Model target: the owners (<c># Prefix:</c>) it holds, its project, its namespace — and, optionally,
/// the name of the layer's data interface (<see cref="DataInterfaceRenderer"/>) and the modules it builds on
/// (<paramref name="DependsOn"/>: owners, each naming the module that holds it; absent means every module before it).
/// </summary>
public sealed record ModelModule(
    IReadOnlyList<string> Owners, string ProjectPath, string Namespace, string? DataInterface = null,
    IReadOnlyList<string>? DependsOn = null);

/// <summary>
/// One data interface of a modular Model target: what it extends, and which modules' sets it declares itself.
/// </summary>
/// <param name="Module">The module the interface is generated into.</param>
/// <param name="Name">The interface's name.</param>
/// <param name="Extends">The nearest interfaces among the modules this one builds on — empty for the lowest, which carries the context surface.</param>
/// <param name="Own">The modules whose sets this interface declares: its own, and those it builds on that no extended interface already reaches.</param>
public sealed record DataInterfaceLayer(int Module, string Name, IReadOnlyList<string> Extends, IReadOnlyList<int> Own);

/// <summary>
/// Which module each model, enum and standalone view of a model set belongs to — by its owner (the declaring file's
/// <c># Prefix:</c>). The modules are listed lowest layer first. Every owner the set has belongs to exactly one module,
/// and every module owner is one the set has; anything else is refused rather than dropped or guessed.
/// </summary>
public sealed class ModelModules
{
    private readonly Dictionary<string, int> _moduleOfOwner;
    private readonly IReadOnlyList<IReadOnlySet<int>> _below;

    private ModelModules(IReadOnlyList<ModelModule> modules, Dictionary<string, int> moduleOfOwner, IReadOnlyList<IReadOnlySet<int>> below)
    {
        Modules = modules;
        _moduleOfOwner = moduleOfOwner;
        _below = below;
        DataInterfaces = PlanDataInterfaces(modules, below);
    }

    public IReadOnlyList<ModelModule> Modules { get; }

    /// <summary>The index of <paramref name="owner"/>'s module — 0 is the lowest layer.</summary>
    public int IndexOf(string owner) => _moduleOfOwner[owner];

    public int IndexOf(ResolvedModel model) => IndexOf(EntitySurfaceFilter.OwnerOf(model));

    public int IndexOf(EnumNode e) => IndexOf(e.Prefix ?? "");

    public int IndexOf(ResolvedView view) => IndexOf(view.Source.Prefix ?? "");

    /// <summary>
    /// The modules a module builds on — every module before it, or, when it names its <c>dependsOn</c>, the closure of what it
    /// names (the named modules and what they build on). Their types are the ones its types may name.
    /// </summary>
    public IReadOnlySet<int> Below(int module) => _below[module];

    /// <summary>Whether a declaration of module <paramref name="from"/> may name one of module <paramref name="to"/>.</summary>
    public bool Sees(int from, int to) => from == to || _below[from].Contains(to);

    /// <summary>
    /// The namespaces a module's generated files import: those of the modules it builds on, whose types its types may name.
    /// </summary>
    public IReadOnlyList<string> ImportsOf(int module) =>
        _below[module].Order().Select(i => Modules[i].Namespace).Where(ns => ns != Modules[module].Namespace).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The data interfaces of the modules that declare one, lowest module first.</summary>
    public IReadOnlyList<DataInterfaceLayer> DataInterfaces { get; }

    /// <summary>
    /// The interfaces no other interface extends — what the generated context implements. One for a line of layers; one per
    /// product when products stand side by side on a common layer.
    /// </summary>
    public IReadOnlyList<string> TopDataInterfaces =>
        DataInterfaces.Where(top => !DataInterfaces.Any(other => other.Extends.Contains(top.Name))).Select(l => l.Name).ToList();

    private static IReadOnlyList<DataInterfaceLayer> PlanDataInterfaces(IReadOnlyList<ModelModule> modules, IReadOnlyList<IReadOnlySet<int>> below)
    {
        var layers = new List<DataInterfaceLayer>();
        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i].DataInterface is not { } name) continue;
            var declared = below[i].Where(j => modules[j].DataInterface is not null).ToList();
            // The nearest interfaces: those no other declared one below this module builds on.
            var nearest = declared.Where(d => !declared.Any(e => e != d && below[e].Contains(d))).Order().ToList();
            var reached = nearest.SelectMany(a => below[a].Append(a)).ToHashSet();
            var own = below[i].Append(i).Where(j => !reached.Contains(j)).Order().ToList();
            layers.Add(new DataInterfaceLayer(i, name, nearest.Select(a => modules[a].DataInterface!).ToList(), own));
        }
        return layers;
    }

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
        var moduleOfInterface = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (string.IsNullOrWhiteSpace(module.ProjectPath)) problems.Add($"modules[{i}]: projectPath 가 없습니다.");
            if (string.IsNullOrWhiteSpace(module.Namespace)) problems.Add($"modules[{i}]: namespace 가 없습니다.");
            if (module.Owners.Count == 0) problems.Add($"modules[{i}]: owners 가 비어 있습니다.");
            if (module.DataInterface is { } di)
            {
                if (!IsIdentifier(di)) problems.Add($"modules[{i}]: dataInterface '{di}' 는 C# 형 이름이 아닙니다.");
                else if (!moduleOfInterface.TryAdd(di, i))
                    problems.Add($"modules[{moduleOfInterface[di]}] 와 modules[{i}] 가 같은 dataInterface '{di}' 를 냅니다.");
            }
            foreach (var owner in module.Owners)
            {
                if (!moduleOfOwner.TryAdd(owner, i))
                    problems.Add($"소유자 {Label(owner)} 가 modules[{moduleOfOwner[owner]}] 와 modules[{i}] 에 함께 있습니다 — 한 소유자는 한 모듈에만.");
            }
        }

        // What each module builds on. Without dependsOn: every module before it, as the order of the list says. With it: the closure
        // of the named modules — each named by an owner it holds, and a module before this one, so the list stays a bottom-up order.
        var below = new List<IReadOnlySet<int>>();
        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i].DependsOn is not { } named)
            {
                below.Add(Enumerable.Range(0, i).ToHashSet());
                continue;
            }
            var closure = new HashSet<int>();
            foreach (var owner in named)
            {
                if (!moduleOfOwner.TryGetValue(owner, out var target))
                    problems.Add($"modules[{i}]: dependsOn 의 소유자 {Label(owner)} 를 가진 모듈이 없습니다.");
                else if (target >= i)
                    problems.Add($"modules[{i}]: dependsOn 의 소유자 {Label(owner)} 는 {(target == i ? "자기 자신" : $"modules[{target}]")} 입니다 — "
                        + "순서가 층이다: 딛는 모듈은 목록에서 앞에 와야 합니다.");
                else
                {
                    closure.Add(target);
                    closure.UnionWith(below[target]);
                }
            }
            below.Add(closure);
        }

        // The context surface (Set<T>, Database, ...) is declared by the lowest data interface. Two that do not build on one another would
        // each declare it, and code receiving both through one interface could not tell which a member came from.
        var lowest = Enumerable.Range(0, modules.Count)
            .Where(i => modules[i].DataInterface is not null && !below[i].Any(j => modules[j].DataInterface is not null))
            .ToList();
        if (lowest.Count > 1)
            problems.Add($"modules[{string.Join("], modules[", lowest)}] 의 dataInterface 가 서로 아래층을 모릅니다 — 컨텍스트 면(Set<T> · Database …)이 둘 이상의 "
                + "인터페이스에 중복됩니다. 그 모듈들이 함께 딛는 아래 모듈에 dataInterface 를 두세요.");

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
        return new ModelModules(modules, moduleOfOwner, below);
    }

    private static string Label(string owner) => owner.Length == 0 ? "\"\"(접두어 없는 파일)" : $"'{owner}'";

    private static bool IsIdentifier(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');
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
