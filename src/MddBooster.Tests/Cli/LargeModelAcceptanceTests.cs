using M3L.Native;
using MddBooster.Cli.Commands;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Acceptance gate — runs a full four-target build over one model that crosses
/// the whole feature matrix, and checks the result against what the model says
/// should exist.
/// </summary>
/// <remarks>
/// Single-feature tests each hold one path steady; this holds them all at once,
/// which is where cross-feature interference shows up — a name collision
/// between two entities, an emission that works alone and is dropped when
/// another declaration is present, output that parses per file but disagrees
/// across files.
/// <para>
/// Every expectation is derived from the parsed model rather than written down
/// as a total, so adding an entity to the fixture does not require editing a
/// number here, and an entity that silently produces no output fails by name
/// instead of by an off-by-one that a lower bound would absorb. The model to
/// run against comes from <see cref="AcceptanceModel"/>.
/// </para>
/// <para>
/// C# output is checked for syntactic validity only. Full semantic compilation
/// would need the runtime assembly the generated entities inherit from, which
/// this project does not reference; the initializer forms that syntax parsing
/// cannot judge are compiled separately in
/// <c>FieldConstraintRenderTests.Emitted_initializers_compile_against_the_generated_enum</c>.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class LargeModelAcceptanceTests
{
    private sealed record Layout(string Root, string MddDir, string DbDir, string ModelDir, string ApiDir, string TsDir);

    private static Layout Scaffold(string? modelPath = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-acceptance-{Guid.NewGuid():N}");
        var layout = new Layout(
            root,
            Path.Combine(root, "mdd"),
            Path.Combine(root, "src", "Sample.Database"),
            Path.Combine(root, "src", "Sample.Entities"),
            Path.Combine(root, "src", "Sample.Server"),
            Path.Combine(root, "src", "sample-ui"));

        foreach (var dir in new[] { layout.MddDir, layout.DbDir, layout.ModelDir, layout.ApiDir, layout.TsDir })
            Directory.CreateDirectory(dir);

        File.Copy(modelPath ?? AcceptanceModel.Path, Path.Combine(layout.MddDir, "tables.m3l.md"));

        File.WriteAllText(Path.Combine(layout.DbDir, "Sample.sqlproj"),
            """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup><Name>Sample</Name></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(layout.MddDir, "mdd.json"), """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/Sample.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/Sample.Entities", "namespace": "Sample.Entities", "dbContextName": "SampleDbContext" },
    { "type": "Api", "projectPath": "../src/Sample.Server", "namespace": "Sample.Server" },
    { "type": "TypeScript", "outputPath": "../src/sample-ui" }
  ]
}
""");
        return layout;
    }

    private static void Cleanup(Layout layout)
    {
        try { Directory.Delete(layout.Root, recursive: true); } catch { }
    }

    /// <summary>Names the model declares, read independently of the generators.</summary>
    private static (List<ResolvedModel> Models, List<EnumNode> Enums) ReadDeclarations()
    {
        var ast = new M3lLoader().LoadFile(AcceptanceModel.Path);
        return (new InterfaceResolver(ast).ResolveAll().ToList(), ast.Enums.ToList());
    }

    [Fact]
    public void Every_declared_entity_and_enum_reaches_every_target()
    {
        var (models, enums) = ReadDeclarations();
        Assert.NotEmpty(models);
        Assert.NotEmpty(enums);

        var layout = Scaffold();
        try
        {
            Assert.Equal(0, new BuildCommand().Run(layout.MddDir));

            var tables = Path.Combine(layout.DbDir, "dbo", "Tables_gen");
            var views = Path.Combine(layout.DbDir, "dbo", "Views_gen");
            var entities = Path.Combine(layout.ModelDir, "Entity_gen");
            var enumDir = Path.Combine(layout.ModelDir, "Enum_gen");

            var missing = new List<string>();

            foreach (var model in models)
            {
                Expect(missing, tables, $"{model.Name}.sql");
                Expect(missing, entities, $"I{model.Name}.cs");
                Expect(missing, entities, $"{model.Name}.cs");
                Expect(missing, entities, $"{model.Name}Ext.cs");

                // A model with a derived field is materialised through a view; without
                // it the derived columns exist in C# and nowhere in the database.
                if (model.Fields.Any(f => f.Kind is FieldKind.Lookup or FieldKind.Rollup or FieldKind.Computed))
                    Expect(missing, views, $"{model.Name}FullView.sql");
            }

            foreach (var e in enums)
                Expect(missing, enumDir, $"{e.Name}.cs");

            Assert.True(missing.Count == 0,
                $"{missing.Count} declared element(s) produced no output:\n  " + string.Join("\n  ", missing));

            // The reverse direction — output nothing declared it. A stray file means a
            // renderer emitted for something the model does not contain.
            var unexpectedTables = Directory.GetFiles(tables, "*.sql")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !models.Any(m => m.Name == n))
                .ToList();
            Assert.True(unexpectedTables.Count == 0,
                "Tables_gen holds files no entity declares: " + string.Join(", ", unexpectedTables));
        }
        finally { Cleanup(layout); }
    }

    [Fact]
    public void Generated_csharp_parses_and_generated_sql_is_non_empty()
    {
        var layout = Scaffold();
        try
        {
            Assert.Equal(0, new BuildCommand().Run(layout.MddDir));

            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var allCs = Directory.GetFiles(layout.ModelDir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(layout.ApiDir, "*.cs", SearchOption.AllDirectories))
                .ToList();
            Assert.NotEmpty(allCs);

            foreach (var cs in allCs)
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(cs), parseOptions);
                var errors = tree.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .ToList();
                Assert.True(errors.Count == 0,
                    $"Syntax errors in {Path.GetFileName(cs)}: {string.Join("; ", errors.Select(d => d.GetMessage()))}");
            }

            var allSql = Directory.GetFiles(layout.DbDir, "*.sql", SearchOption.AllDirectories);
            Assert.NotEmpty(allSql);
            foreach (var sql in allSql)
                Assert.True(new FileInfo(sql).Length > 0, $"{Path.GetFileName(sql)} is empty");

            var allTs = Directory.GetFiles(layout.TsDir, "*.ts", SearchOption.AllDirectories);
            Assert.NotEmpty(allTs);
            foreach (var ts in allTs)
                Assert.True(new FileInfo(ts).Length > 0, $"{Path.GetFileName(ts)} is empty");
        }
        finally { Cleanup(layout); }
    }

    [Fact]
    public void Acceptance_model_passes_semantic_analysis()
    {
        var (models, enums) = ReadDeclarations();

        var diagnostics = new SemanticAnalyzer(models, enums).Analyze();

        Assert.True(diagnostics.Count == 0,
            $"The acceptance model produced {diagnostics.Count} semantic diagnostic(s):\n" +
            string.Join("\n", diagnostics.Select(d => d.Format())));
    }

    [Fact]
    public void Building_the_acceptance_fixture_reports_nothing_on_stderr()
    {
        // Warnings are how the build reports something it could not emit. The
        // checked-in fixture only declares what all four targets support, so any
        // warning here means either the fixture drifted into unsupported territory
        // or a target stopped emitting something it used to.
        //
        // This one runs against the fixture even when an override is set: the claim
        // is about the fixture, and an arbitrary external model may warn for reasons
        // that are none of this gate's business. Skipping under override would put
        // back the conditional no-op this gate was rebuilt to remove.
        var layout = Scaffold(AcceptanceModel.FixturePath);
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(0, new BuildCommand().Run(layout.MddDir));
        }
        finally { Cleanup(layout); }

        Assert.True(string.IsNullOrWhiteSpace(stderr.Text),
            "Building the acceptance fixture wrote to stderr:\n" + stderr.Text);
    }

    private static void Expect(List<string> missing, string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName)))
            missing.Add(Path.Combine(Path.GetFileName(directory), fileName));
    }
}
