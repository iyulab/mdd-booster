using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// End-to-end verification for the Plan 3 model generator: renders all three
/// C# artefacts (interface, write class, read class, DbContext) for the
/// BankAccount fixture and confirms each is syntactically well-formed C#
/// using Roslyn's parser. This closes the 20-cycle run by proving the
/// generator output is usable by the downstream compiler.
/// </summary>
public class ModelGeneratorE2ETests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void BankAccount_end_to_end_produces_well_formed_csharp()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        Assert.NotEmpty(models);

        var outputs = new List<(string name, string source)>();

        foreach (var model in models)
        {
            var pair = EntityPairRenderer.Render(model, "Yesung.Entities");
            outputs.Add(($"I{model.Name}.cs", pair.Interface));
            outputs.Add(($"{model.Name}.cs", pair.Write));
            outputs.Add(($"{model.Name}Ext.cs", pair.Read));
        }

        var dbContextSource = DbContextRenderer.Render(models, "YesungDbContext", "Yesung.Entities");
        outputs.Add(("YesungDbContext.cs", dbContextSource));

        // Parse each generated file as C#. We verify syntactic validity only —
        // full semantic compilation would require binding Iyu.Core / Iyu.Data /
        // EF Core metadata and belongs in an integration harness.
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        foreach (var (name, source) in outputs)
        {
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
            var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(
                diagnostics.Count == 0,
                $"Syntax errors in {name}: {string.Join("; ", diagnostics.Select(d => d.GetMessage()))}\n--- source ---\n{source}");
        }
    }

    [Fact]
    public void Entity_with_references_and_value_objects_also_parses_clean()
    {
        var astRef = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var astVo = new M3lLoader().LoadFile(FixturePath("contact-with-vos.m3l.md"));
        var allModels = new InterfaceResolver(astRef).ResolveAll()
            .Concat(new InterfaceResolver(astVo).ResolveAll())
            .ToList();

        var sources = new List<string>();
        foreach (var m in allModels)
        {
            var p = EntityPairRenderer.Render(m, "Test.All");
            sources.Add(p.Interface);
            sources.Add(p.Write);
            sources.Add(p.Read);
        }
        sources.Add(DbContextRenderer.Render(allModels, "AllDbContext", "Test.All"));

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        foreach (var src in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(src, parseOptions);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join("; ", errors.Select(d => d.ToString())));
        }
    }
}
