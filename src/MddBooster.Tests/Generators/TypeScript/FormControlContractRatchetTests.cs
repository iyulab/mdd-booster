using System.Text;
using System.Text.RegularExpressions;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// Every prop a generated form puts on a consumer-supplied component is a demand on
/// that consumer: the module they point <c>formControlsImport</c> at has to accept it.
/// The README states those demands, and the rendered file is <c>DO NOT EDIT</c>, so a
/// prop the README omits cannot be discovered until someone else's build breaks.
/// </summary>
/// <remarks>
/// <c>step</c> and <c>maxlength</c> arrived that way in 0.8.0 and the table was updated
/// by hand. That worked because someone remembered. This asserts it instead.
/// <para>
/// It reads the README itself, not a copy declared here. A copy is the same defect one
/// level up — the two drift, the tests still pass, and the consumer is the one who
/// finds out. The file is copied to the test output by the project file.
/// </para>
/// <para>
/// The documented side is read as <em>the set of identifiers the contract section names
/// in backticks</em>, not as a table with columns. Prop names live in that section in
/// several shapes (a cell, a bullet, prose), and pinning the assertion to one table
/// layout would make an editorial reflow look like a contract change.
/// </para>
/// </remarks>
public sealed class FormControlContractRatchetTests
{
    /// <summary>
    /// Components the generated form hands props to. Each is supplied by the consumer,
    /// which is what puts its prop surface under contract — a tag rendered from this
    /// generator's own output (or a plain HTML element) would not belong here.
    /// </summary>
    private static readonly string[] ConsumerComponents =
        ["UInput", "UTextarea", "USelect", "UCheckbox", "FormSection", "FormRow"];

    /// <summary>
    /// Attribute names that are not demands on the consumer's component.
    /// </summary>
    /// <remarks>
    /// Adding an entry is a decision, not a fix: the question is whether the consumer's
    /// component has to understand the name. If it does, it belongs in the README.
    /// </remarks>
    private static readonly Dictionary<string, string> NotAContractDemand = new(StringComparer.Ordinal)
    {
        ["key"] = "React's own list-reconciliation attribute. Every React component " +
                  "accepts it regardless of its props, so naming it in the contract " +
                  "would describe React rather than this generator's demands.",
    };

    private static string ReadmeText()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contract", "README.md");
        Assert.True(File.Exists(path),
            $"the consumer contract was not found at '{path}'. It is copied there from the " +
            "repository README by MddBooster.Tests.csproj; if the README moved, update that " +
            "copy rule. This ratchet asserts nothing while its subject is missing.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The 「소비 프로젝트 계약」 section, ending at the next same-level heading.
    /// Scoping matters: the README names props elsewhere too (the changelog-style
    /// notes, the option table), and reading the whole file would let an unrelated
    /// mention stand in for a contract entry.
    /// </summary>
    private static string ContractSection()
    {
        var text = ReadmeText();
        var start = text.IndexOf("### ⚠️ 소비 프로젝트 계약", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "the contract section heading was not found in the README. If it was retitled, " +
            "update this test — but check first that the section still exists at all.");

        var end = text.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>
    /// Identifiers the contract section names in backticks. A span like
    /// <c>step?: number</c> contributes <c>step</c>; one like <c>"date"</c> contributes
    /// nothing, because it does not start with an identifier.
    /// </summary>
    private static HashSet<string> DocumentedNames()
    {
        // Fenced blocks come out first. A fence is three backticks, so leaving one in
        // shifts every pair after it and the section is then read to say something it
        // does not — which is worse than failing, because it still produces a plausible
        // set. Spans are kept to one line for the same reason: a stray backtick should
        // cost one line, not the rest of the section.
        var section = Regex.Replace(ContractSection(), @"^```[\s\S]*?^```", "", RegexOptions.Multiline);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match span in Regex.Matches(section, @"`(?<s>[^`\r\n]+)`"))
        {
            var leading = Regex.Match(span.Groups["s"].Value.TrimStart(), @"^[A-Za-z][A-Za-z0-9]*");
            if (leading.Success) names.Add(leading.Value);
        }
        return names;
    }

    private static string RenderForm()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "form-control-contract.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll();

        return TsFormRenderer.RenderAll(models, ast.Enums, new TsFormImports
        {
            GeneratedTypesBase = "@@types",
            Modules = new TsFormModuleImports
            {
                Layout = "@@layout",
                Controls = "@@controls",
                SelectOptions = "@@select-options",
            },
        })["Everything"];
    }

    /// <summary>
    /// Attribute names rendered onto <paramref name="component"/>, read by walking the
    /// tag rather than matching <c>name=</c> with a pattern: attribute values contain
    /// <c>=</c> and nested braces (<c>onChange={v =&gt; onChange({ x: v })}</c>), and a
    /// pattern loose enough to skip those also matches inside them.
    /// </summary>
    private static HashSet<string> AttributesOn(string form, string component)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match open in Regex.Matches(form, $@"<{component}(?=[\s/>])"))
        {
            var i = open.Index + open.Length;
            while (i < form.Length)
            {
                while (i < form.Length && char.IsWhiteSpace(form[i])) i++;
                if (i >= form.Length || form[i] == '>' || form[i] == '/') break;

                var nameStart = i;
                while (i < form.Length && (char.IsLetterOrDigit(form[i]) || form[i] == '-')) i++;
                if (i == nameStart) break;                       // not an attribute; stop this tag
                found.Add(form[nameStart..i]);

                while (i < form.Length && char.IsWhiteSpace(form[i])) i++;
                if (i >= form.Length || form[i] != '=') continue;  // bare attribute (e.g. `required`)
                i++;

                if (i < form.Length && form[i] == '"')
                {
                    i = form.IndexOf('"', i + 1);
                    if (i < 0) break;
                    i++;
                }
                else if (i < form.Length && form[i] == '{')
                {
                    var depth = 0;
                    for (; i < form.Length; i++)
                    {
                        if (form[i] == '{') depth++;
                        else if (form[i] == '}' && --depth == 0) { i++; break; }
                    }
                }
            }
        }

        return found;
    }

    private static Dictionary<string, HashSet<string>> EmittedByComponent()
    {
        var form = RenderForm();
        return ConsumerComponents.ToDictionary(c => c, c => AttributesOn(form, c), StringComparer.Ordinal);
    }

    /// <summary>
    /// The ratchet. A prop added to any consumer-supplied component shows up here until
    /// the README says the consumer has to accept it.
    /// </summary>
    [Fact]
    public void Every_emitted_prop_is_named_in_the_consumer_contract()
    {
        var documented = DocumentedNames();
        var undocumented = new StringBuilder();

        foreach (var (component, props) in EmittedByComponent())
        {
            var missing = props
                .Where(p => !documented.Contains(p))
                .Where(p => !NotAContractDemand.ContainsKey(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
                undocumented.Append($"  <{component}> emits {string.Join(", ", missing.Select(m => $"`{m}`"))}\n");
        }

        Assert.True(undocumented.Length == 0,
            "the generated form demands props the consumer contract does not mention:\n" +
            undocumented +
            "\nA consumer reads that section to build the module `formControlsImport` points at, " +
            "so an unlisted prop breaks their build and not ours. Add it to the 「소비 프로젝트 계약」 " +
            "section of README.md — or, if the consumer's component does not have to understand " +
            "the name, declare it in NotAContractDemand with the reason.");
    }

    /// <summary>
    /// The ratchet's reach is bounded by what the fixture renders. A prop that no fixture
    /// field triggers is a prop this ratchet cannot see, so the fixture is asserted to
    /// exercise the whole documented surface rather than trusted to.
    /// </summary>
    /// <remarks>
    /// This also closes the other direction: a prop the README demands but nothing emits
    /// makes consumers build what they do not need, and it surfaces here as an entry the
    /// fixture cannot produce.
    /// </remarks>
    [Fact]
    public void The_fixture_emits_every_documented_prop()
    {
        var emitted = EmittedByComponent().Values.SelectMany(p => p).ToHashSet(StringComparer.Ordinal);

        // The contract section names more than props — component names, module paths,
        // types. Only the props are the fixture's obligation, so they are listed rather
        // than derived; the previous test is what keeps that list honest in the other
        // direction (anything emitted and unlisted fails there).
        string[] documentedProps =
        [
            "label", "required", "description", "type", "step", "maxlength",
            "value", "onChange", "minRows", "options", "placeholder", "checked",
            "title", "full",
        ];

        var unexercised = documentedProps.Where(p => !emitted.Contains(p)).ToList();

        Assert.True(unexercised.Count == 0,
            $"the fixture never emits {string.Join(", ", unexercised.Select(u => $"`{u}`"))}, so the " +
            "ratchet is blind to those props. Either extend fixtures/form-control-contract.m3l.md to " +
            "trigger them, or — if the generator genuinely stopped emitting one — remove it from the " +
            "README contract too, because consumers are still being told to support it.");
    }

    /// <summary>
    /// Guards the guard, twice: a fixture that rendered no controls would satisfy the
    /// ratchet trivially, and so would a README whose contract section had been emptied.
    /// </summary>
    [Fact]
    public void The_ratchet_is_not_asserting_over_an_empty_set()
    {
        var emitted = EmittedByComponent();

        Assert.All(ConsumerComponents, c =>
            Assert.True(emitted[c].Count > 0, $"the fixture renders no <{c}>, so its props go unchecked"));

        Assert.True(DocumentedNames().Count >= 20,
            "the contract section names fewer identifiers than any real contract would; " +
            "it was probably emptied or restructured, and the ratchet is passing vacuously");
    }
}
