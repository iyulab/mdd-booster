using System.Text.RegularExpressions;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// Every module specifier a generated form carries has to come from somewhere
/// the consumer can reach: a setting they declared, or a path derived from where
/// they told the generator to write. A literal is neither — and because the
/// rendered file says <c>DO NOT EDIT</c>, a literal is not a default the
/// consumer can override but a folder layout they are required to build.
/// </summary>
/// <remarks>
/// Six such literals existed. Removing them is not what keeps them gone: the
/// next control, helper, or convenience import would arrive the same way, and
/// nothing about writing one looks wrong at the call site. So this asserts the
/// property rather than the six instances.
/// <para>
/// It reads the <em>output</em>, not the renderer's source. A source-text
/// ratchet would have to find a <c>.cs</c> file from the test binary, which
/// couples the assertion to a path outside the project and fails for reasons
/// that have nothing to do with imports.
/// </para>
/// </remarks>
public sealed class FormImportSurfaceRatchetTests
{
    // Sentinels: nothing that reaches the file legitimately can look like these.
    private const string TypesBase = "@@derived-types";
    private const string Layout = "@@configured-layout";
    private const string Controls = "@@configured-controls";
    private const string SelectOptions = "@@configured-select-options";

    /// <summary>
    /// Module specifiers a generated form may state literally, and why each one
    /// is not the defect this class exists to prevent.
    /// </summary>
    /// <remarks>
    /// Adding an entry is a decision, not a fix. The question to answer first is
    /// the one the six removed literals failed: <b>does naming this module
    /// constrain the consumer's project layout, or the identity of a package
    /// they must install?</b> If either, it belongs in
    /// <see cref="TsFormModuleImports"/> instead.
    /// </remarks>
    private static readonly Dictionary<string, string> AllowedLiterals = new(StringComparer.Ordinal)
    {
        ["react"] = "React itself. A React component that imported React from a " +
                    "configurable location would not be a React component; the " +
                    "specifier is fixed by the framework, not by any project's layout.",
    };

    private static string RenderTicketForm()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "form-import-surface.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll();

        return TsFormRenderer.RenderAll(models, ast.Enums, new TsFormImports
        {
            GeneratedTypesBase = TypesBase,
            Modules = new TsFormModuleImports
            {
                Layout = Layout,
                Controls = Controls,
                SelectOptions = SelectOptions,
            },
        })["Ticket"];
    }

    private static IReadOnlyList<string> SpecifiersIn(string form) =>
        [.. Regex.Matches(form, @"from '(?<m>[^']+)'").Select(m => m.Groups["m"].Value)];

    /// <summary>
    /// The ratchet. A new import that names its module literally shows up here
    /// as a specifier that is neither a sentinel nor a declared exception.
    /// </summary>
    [Fact]
    public void Every_import_is_configured_derived_or_a_declared_exception()
    {
        var specifiers = SpecifiersIn(RenderTicketForm());

        var unaccounted = specifiers
            .Where(s => !s.StartsWith("@@", StringComparison.Ordinal))
            .Where(s => !AllowedLiterals.ContainsKey(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(unaccounted.Count == 0,
            $"generated form imports {string.Join(", ", unaccounted.Select(u => $"'{u}'"))} " +
            "literally. Route it through TsFormModuleImports (consumer's choice) or derive it " +
            "from the output paths (this generator's own file) — or, if it genuinely cannot be " +
            "either, add it to AllowedLiterals with the reason.");
    }

    /// <summary>
    /// The ratchet's reach is bounded by what the fixture renders, so an
    /// incomplete fixture would let a whole class of import through unseen.
    /// </summary>
    [Fact]
    public void The_fixture_actually_exercises_every_kind_of_import()
    {
        var specifiers = SpecifiersIn(RenderTicketForm());

        Assert.Contains("react", specifiers);                        // slot type
        Assert.Contains(Layout, specifiers);                         // FormSection/FormRow
        Assert.Contains(Controls, specifiers);                       // UInput/UTextarea/USelect/UCheckbox
        Assert.Contains(SelectOptions, specifiers);                  // enumToOptions
        Assert.Contains($"{TypesBase}/entities_gen", specifiers);
        Assert.Contains($"{TypesBase}/enums_gen", specifiers);
        Assert.Contains($"{TypesBase}/enum_labels_gen", specifiers);
    }

    /// <summary>
    /// Guards the guard: a rendered file with no imports at all would satisfy
    /// the ratchet trivially.
    /// </summary>
    [Fact]
    public void The_ratchet_is_not_asserting_over_an_empty_set()
    {
        Assert.True(SpecifiersIn(RenderTicketForm()).Count >= 7);
    }
}
