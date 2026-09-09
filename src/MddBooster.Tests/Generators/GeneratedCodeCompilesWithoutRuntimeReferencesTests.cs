using MddBooster.Cli.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators;

/// <summary>
/// 생성 C# 을 <b>컴파일까지</b> 검증한다 — 단, 런타임 패키지를 참조하지 않는다.
/// </summary>
/// <remarks>
/// <para>
/// <b>왜 이 게이트가 있나</b>(2026-09-09): 이 리포의 C# 축 검증은 오래 Roslyn <i>파싱</i>까지였고,
/// 그 위에 머문 근거는 *「컴파일까지 올리면 생성 엔티티가 런타임 패키지를 참조해야 한다」*였다.
/// 그런데 실제로 소비자를 깨뜨린 두 결함은 <b>BCL 이름 해석 문제</b>였다 —
/// <c>CS0246</c>(방출 파일이 <c>using System;</c> 을 선언하지 않아 소비자의 <c>ImplicitUsings</c>
/// 설정에 걸림)와 <c>CS0104</c>(엔티티 단순명이 BCL 타입과 충돌). 둘 다 런타임 참조 <b>0개</b>로
/// 재현된다. 파싱은 이 클래스를 통과시킨다 — 문법은 멀쩡하기 때문이다.
/// </para>
/// <para>
/// 🔴 <b>이 게이트는 런타임의 «네임스페이스»를 알아서는 안 된다.</b> 그것을 아는 순간 테스트가
/// 「생성물은 단순 이름만 내고 네임스페이스는 소비앱이 정한다」는 계약을 대신 위반한다.
/// 그래서 아래 스텁은 <see cref="HostNamespace"/> 라는 <b>중립 네임스페이스</b>에 선언되고,
/// 소비앱의 <c>GlobalUsings.cs</c> 가 하는 일을 <c>global using</c> 한 줄로 흉내낸다.
/// 스텁이 제공하는 것은 <b>이름뿐</b>이고 동작이 아니다 — 동작까지 흉내내면 실물과 어긋난
/// 서명 위에서 초록이 나와 거짓 확신이 된다.
/// </para>
/// </remarks>
public class GeneratedCodeCompilesWithoutRuntimeReferencesTests
{
    private const string HostNamespace = "GeneratedCodeHost";

    /// <summary>
    /// 소비앱이 공급해야 하는 «이름»만 선언한 스텁. 실제 네임스페이스는 어디에도 없다.
    /// </summary>
    private const string RuntimeNameStubs = $$"""
        namespace {{HostNamespace}};

        public abstract class IyuEntity { public System.Guid Id { get; set; } }
        public class IyuDbContext { }
        public abstract class IyuODataController<TRead, TWrite>
        {
            protected IyuODataController(IyuDbContext context) { _ = context; }
        }

        [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = true)]
        public sealed class ReferenceAttribute : System.Attribute
        { public ReferenceAttribute(params string[] a) { _ = a; } }

        [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = true)]
        public sealed class LookupAttribute : System.Attribute
        { public LookupAttribute(params string[] a) { _ = a; } }

        [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = true)]
        public sealed class RollupAttribute : System.Attribute
        {
            public RollupAttribute(params string[] a) { _ = a; }
            // 렌더러가 내는 유일한 named 인자(`[Rollup("...", Indexed = true)]`).
            // 별도 속성이 아니라 이 속성의 프로퍼티다 — 스텁을 세울 때 실제 방출을 읽고 확인했다.
            public bool Indexed { get; set; }
        }

        [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = true)]
        public sealed class ComputedAttribute : System.Attribute
        { public ComputedAttribute(params string[] a) { _ = a; } }

        [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = true)]
        public sealed class BindingAttribute : System.Attribute
        { public BindingAttribute(params string[] a) { _ = a; } }

        """;

    /// <summary>
    /// 게이트가 덮지 «못하는» 파일과 그 사유. 🔴 이 목록은 <b>단언된다</b> — 생성기가 새 파일을
    /// 내기 시작하면 「덮이거나, 사유와 함께 여기 있거나」 둘 중 하나여야 하고, 아니면 빨강이다.
    /// 게이트가 조용히 좁아지는 것을 막는 것이 목적이다.
    /// </summary>
    private static readonly Dictionary<string, string> NotCovered = new(StringComparer.Ordinal)
    {
        ["DbContext_gen"] =
            "EF Core 실물이 필요하다(DbContextOptions<>·DbSet<>·ModelBuilder 의 fluent 체인). " +
            "그 체인을 스텁으로 흉내내면 실물과 어긋난 서명 위에서 초록이 나온다 — 이 게이트의 목적은 " +
            "BCL 이름 해석이지 런타임 API 정합성이 아니다.",
        ["ApiRegistration_gen"] =
            "IyuMainServerOptions 의 «구조»(ODataModel/GraphQL 하위 객체와 그 제네릭 메서드)가 필요하다. " +
            "위와 같은 이유로 이름만으로는 세울 수 없다.",
    };

    private static (string ModelDir, string ApiDir, string Root) Generate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-compile-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        var apiDir = Path.Combine(root, "src", "X.Server");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(apiDir);

        // 파생 필드(Lookup/Rollup/Computed)·enum·복합 타입이 실제로 방출되는 픽스처를 쓴다 —
        // 방출되지 않으면 이 게이트는 조용히 공허하게 참이 된다.
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-derived.m3l.md"),
            Path.Combine(mddDir, "tables.m3l.md"));

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Model", "projectPath": "../src/X.Entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
                { "type": "Api", "projectPath": "../src/X.Server", "namespace": "X.Server" }
              ]
            }
            """);

        Assert.Equal(0, new BuildCommand().Run(mddDir));
        return (modelDir, apiDir, root);
    }

    private static IReadOnlyList<string> AllGenerated(string modelDir, string apiDir) =>
        Directory.GetFiles(modelDir, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(apiDir, "*.cs", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static bool IsCovered(string path) =>
        !NotCovered.Keys.Any(dir =>
            path.Contains(Path.DirectorySeparatorChar + dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.GetFileNameWithoutExtension(path).Equals(dir, StringComparison.Ordinal));

    private static IEnumerable<MetadataReference> BclReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

    private static IReadOnlyList<Diagnostic> Compile(IEnumerable<string> sourceFiles, string extraGlobalUsings)
    {
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = sourceFiles
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f))
            .Append(CSharpSyntaxTree.ParseText(RuntimeNameStubs, parse, path: "stubs.cs"))
            .Append(CSharpSyntaxTree.ParseText(
                $"global using {HostNamespace};\n{extraGlobalUsings}", parse, path: "GlobalUsings.cs"));

        var compilation = CSharpCompilation.Create(
            "GeneratedOutput",
            trees,
            BclReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// <summary>
    /// 🔴 이 리포의 C# 축이 «컴파일까지» 올라온 자리. 되돌리면(방출 파일에서 `using System;` 을
    /// 빼거나, 이름 충돌을 다시 만들면) 여기가 빨개진다.
    /// </summary>
    [Fact]
    public void Generated_output_compiles_against_names_alone_with_no_runtime_package()
    {
        var (modelDir, apiDir, root) = Generate();
        try
        {
            var all = AllGenerated(modelDir, apiDir);
            Assert.NotEmpty(all);

            var covered = all.Where(IsCovered).ToList();
            Assert.NotEmpty(covered);

            var errors = Compile(covered, extraGlobalUsings: string.Empty);

            Assert.True(errors.Count == 0,
                "런타임 참조 없이 컴파일되어야 한다. 실패:\n" +
                string.Join("\n", errors.Select(d =>
                    $"  {Path.GetFileName(d.Location.SourceTree?.FilePath ?? "?")}: {d.Id} {d.GetMessage()}")));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    /// <summary>
    /// 게이트가 «조용히 좁아지는» 것을 막는다 — 생성된 모든 파일은 덮이거나, 사유와 함께
    /// <see cref="NotCovered"/> 에 있어야 한다. 새 타깃 파일이 생기면 여기서 먼저 걸린다.
    /// </summary>
    [Fact]
    public void Every_generated_file_is_either_covered_or_excluded_with_a_recorded_reason()
    {
        var (modelDir, apiDir, root) = Generate();
        try
        {
            foreach (var f in AllGenerated(modelDir, apiDir))
            {
                if (IsCovered(f)) continue;

                var key = NotCovered.Keys.First(k =>
                    f.Contains(Path.DirectorySeparatorChar + k + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || Path.GetFileNameWithoutExtension(f).Equals(k, StringComparison.Ordinal));
                Assert.False(string.IsNullOrWhiteSpace(NotCovered[key]),
                    $"{key} 가 사유 없이 제외돼 있다.");
            }

            // 제외 «목록» 자체가 실물과 맞는지 — 존재하지 않는 것을 제외하고 있으면 목록이 낡은 것이다.
            var all = AllGenerated(modelDir, apiDir);
            foreach (var key in NotCovered.Keys)
            {
                Assert.Contains(all, f =>
                    f.Contains(Path.DirectorySeparatorChar + key + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || Path.GetFileNameWithoutExtension(f).Equals(key, StringComparison.Ordinal));
            }
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    /// <summary>
    /// <b>편입된 축 — 종전에는 「기록된 공백」이었다.</b> 소비 프로젝트가 <c>ImplicitUsings</c> 를
    /// 켜면(= SDK 기본값) <c>System.Threading.Tasks</c> 가 스코프에 들어오고, 모델명이
    /// <c>Task</c> 이면 생성 컨트롤러가 <c>CS0104</c> 로 깨졌다. Api 타깃이 엔티티를 한정 이름으로
    /// 방출하게 되면서 닫혔고(<c>EntityTypeRef</c>), 이 테스트는 그 공백의 고정에서
    /// <b>닫힘의 고정</b>으로 뒤집혔다.
    /// <para>
    /// ⚠ <c>Task</c> 는 억지 사례가 아니라 이 리포가 배포하는 샘플 모델의 이름이다.
    /// 되돌리면(한정을 걷어내고 <c>using</c> 으로 돌아가면) 여기가 빨개진다.
    /// </para>
    /// </summary>
    [Fact]
    public void An_entity_named_like_a_BCL_type_compiles_even_when_the_consumer_enables_implicit_usings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-bclname-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        var apiDir = Path.Combine(root, "src", "X.Server");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(apiDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), """
            # Namespace: x

            ## Task
            - id: identifier @primary
            - title: string(120)
            - created_at: timestamp
            - updated_at: timestamp
            """);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
            {
              "sources": ["./tables.m3l.md"],
              "targets": [
                { "type": "Model", "projectPath": "../src/X.Entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
                { "type": "Api", "projectPath": "../src/X.Server", "namespace": "X.Server" }
              ]
            }
            """);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var covered = AllGenerated(modelDir, apiDir).Where(IsCovered).ToList();
            Assert.NotEmpty(covered);

            // 소비자가 ImplicitUsings 를 켠 상태를 흉내낸다(그 설정이 끌어오는 것 중
            // 이 충돌을 만들던 것 하나만).
            var errors = Compile(covered, extraGlobalUsings: "global using System.Threading.Tasks;");

            Assert.True(errors.Count == 0,
                "ImplicitUsings 를 켠 소비자에게서도 컴파일되어야 한다. 실패:\n" +
                string.Join("\n", errors.Select(d =>
                    $"  {Path.GetFileName(d.Location.SourceTree?.FilePath ?? "?")}: {d.Id} {d.GetMessage()}")));

            // 계측기 생존 — 그 충돌을 실제로 만들 수 있는 픽스처인지 확인한다. 한정을 걷어낸
            // 형태(단순 이름)를 직접 세워 CS0104 가 나오는지 본다: 안 나오면 이 테스트가
            // 검사하는 축 자체가 사라진 것이고, 초록은 공허하다.
            var naive = CSharpSyntaxTree.ParseText(
                "namespace X.Server.Naive; public class Probe { public Task? P; }",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path: "naive.cs");
            var probe = CSharpCompilation.Create("Probe",
                new[]
                {
                    naive,
                    CSharpSyntaxTree.ParseText("namespace X.Entities; public class Task { }"),
                    CSharpSyntaxTree.ParseText("global using X.Entities;\nglobal using System.Threading.Tasks;"),
                },
                BclReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Contains(probe.GetDiagnostics(), d => d.Id == "CS0104");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
