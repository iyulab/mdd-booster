using System.Text.RegularExpressions;
using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// The generator writes two file sets into two independently configured
/// directories and then has one import the other. Nothing verified that the two
/// settings agreed: the forms carried a literal <c>'../types/…'</c>, so any
/// layout other than the one that literal assumed produced files this generator
/// reported as written and the consumer's compiler rejected — in a project this
/// code never runs in.
/// </summary>
/// <remarks>
/// These tests exercise <see cref="TypeScriptGenerator"/> rather than the
/// renderer, because the two directories are what the class owns; the renderer
/// only receives the answer. That is also why the renderer-level fixtures state
/// the specifier as a constant and this file derives it.
/// </remarks>
public sealed class TypeScriptGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mddbooster-tsgen", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private GeneratorContext LoadContext()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-group.m3l.md"));
        return new GeneratorContext
        {
            Models = new InterfaceResolver(ast).ResolveAll(),
            Enums = ast.Enums,
            WorkingDirectory = _root,
        };
    }

    /// <summary>Runs the generator for one layout and returns the rendered form.</summary>
    private string RenderFormFor(string outputPath, string formsOutputPath)
    {
        Directory.CreateDirectory(_root);
        new TypeScriptGenerator(new TypeScriptGeneratorOptions
        {
            OutputPath = outputPath,
            FormsOutputPath = formsOutputPath,
        }).Generate(LoadContext());

        return File.ReadAllText(Path.Combine(_root, formsOutputPath, "OrderItemForm_gen.tsx"));
    }

    [Fact]
    public void Writes_the_five_standard_files_and_a_form_per_entity()
    {
        RenderFormFor("types", "forms");

        foreach (var name in new[]
        {
            "enums_gen.ts", "entities_gen.ts", "entity_names_gen.ts",
            "enum_labels_gen.ts", "field_schema_gen.ts",
        })
        {
            Assert.True(File.Exists(Path.Combine(_root, "types", name)), name);
        }

        Assert.True(File.Exists(Path.Combine(_root, "forms", "OrderItemForm_gen.tsx")));
    }

    /// <summary>
    /// The layout the generator emitted for before the specifier was derived.
    /// It has to keep producing byte-identical imports, or deriving the value
    /// would silently break every existing consumer instead of fixing the ones
    /// it never fitted.
    /// </summary>
    [Fact]
    public void Sibling_layout_still_emits_the_historical_specifier()
    {
        var form = RenderFormFor("types", "forms");

        Assert.Contains("from '../types/entities_gen'", form);
        Assert.Contains("from '../types/enums_gen'", form);
        Assert.Contains("from '../types/enum_labels_gen'", form);
    }

    [Fact]
    public void Forms_beside_the_types_emit_a_same_directory_specifier()
    {
        var form = RenderFormFor("shared", "shared");

        Assert.Contains("from './entities_gen'", form);
        Assert.Contains("from './enums_gen'", form);
        Assert.Contains("from './enum_labels_gen'", form);
    }

    /// <summary>
    /// A bare leading segment is a <em>package</em> specifier in TypeScript, not
    /// a subdirectory: <c>'types/entities_gen'</c> sends the resolver to
    /// node_modules. The relative form is the only one that addresses the file
    /// the generator just wrote.
    /// </summary>
    [Fact]
    public void Types_below_the_forms_directory_are_not_emitted_as_a_package_specifier()
    {
        var form = RenderFormFor("types", ".");

        Assert.Contains("from './types/entities_gen'", form);
        Assert.DoesNotContain("from 'types/entities_gen'", form);
    }

    [Fact]
    public void Forms_below_the_types_directory_walk_back_up()
    {
        var form = RenderFormFor(".", "forms");

        Assert.Contains("from '../entities_gen'", form);
    }

    /// <summary>
    /// The property that actually matters, stated once over every layout: the
    /// specifier the form carries, resolved from the directory the form was
    /// written to, lands on the directory the types were written to.
    /// </summary>
    /// <remarks>
    /// Asserting the literal per layout (above) documents each shape; this
    /// asserts they are all <em>correct</em>, which is the claim a new layout
    /// would have to keep satisfying.
    /// </remarks>
    [Theory]
    [InlineData("types", "forms")]
    [InlineData("shared", "shared")]
    [InlineData("types", ".")]
    [InlineData(".", "forms")]
    [InlineData("a/b/types", "c/forms")]
    public void The_emitted_specifier_resolves_to_the_directory_the_types_were_written_to(
        string outputPath, string formsOutputPath)
    {
        var form = RenderFormFor(outputPath, formsOutputPath);

        var specifier = Regex.Match(form, @"from '(?<base>[^']+)/entities_gen'").Groups["base"].Value;
        Assert.False(string.IsNullOrEmpty(specifier), "no entities_gen import was emitted");

        // A specifier that does not start with '.' is a package name — it would
        // resolve against node_modules, never against what was just generated.
        Assert.StartsWith(".", specifier);

        var formsDir = Path.GetFullPath(Path.Combine(_root, formsOutputPath));
        var resolved = Path.GetFullPath(Path.Combine(formsDir, specifier));
        var expected = Path.GetFullPath(Path.Combine(_root, outputPath));

        Assert.Equal(expected, resolved);
    }
}
