using System.Text.RegularExpressions;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The constraints a model declares — field nullability, the <c>string(n)</c>
/// bound, and a declared default — have to reach the C# entity, not only the
/// SQL column. Where they don't, the API surface accepts what the model forbids
/// and the violation returns as a raw provider error, or a CLR default is stored
/// in place of the declared one.
/// </summary>
public class FieldConstraintRenderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static (ResolvedModel Model, IReadOnlySet<string> Enums, Dictionary<string, EnumNode> EnumLookup)
        LoadSample()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("field-constraints.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Sample");
        var names = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);
        return (model, names, ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal));
    }

    private static string RenderWrite()
    {
        var (model, enums, _) = LoadSample();
        return EntityPairRenderer.Render(model, "Test.Entities", enums).Write;
    }

    /// <summary>
    /// Maps each rendered property to the attribute lines that precede it, so a
    /// test can ask what a single property carries without matching on the whole
    /// class text.
    /// </summary>
    private static Dictionary<string, List<string>> AttributesByProperty(string rendered)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pending = new List<string>();
        foreach (var raw in rendered.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                pending.Add(line);
                continue;
            }
            var match = Regex.Match(line, @"^public [\w\.<>\[\]:]+\??\s+(\w+) \{ get; set; \}");
            if (match.Success)
                result[match.Groups[1].Value] = [.. pending];
            pending.Clear();
        }
        return result;
    }

    private static string PropertyLine(string rendered, string propertyName) =>
        rendered.Split('\n')
            .Select(l => l.Trim())
            .Single(l => Regex.IsMatch(l, $@"^public [\w\.<>\[\]:]+\??\s+{Regex.Escape(propertyName)} \{{ get; set; \}}"));

    // ---------------------------------------------------------------- parser contract

    /// <summary>
    /// Everything below rests on what the parser actually produces, so the shape
    /// is pinned here rather than inferred. Two facts matter most: a field is
    /// non-nullable without any attribute (<c>@not_null</c> is the explicit
    /// spelling of the default, not the thing that creates it), and a quoted
    /// default arrives with its quotes already stripped.
    /// </summary>
    [Fact]
    public void Parser_reports_non_nullability_without_the_explicit_attribute()
    {
        var (model, _, _) = LoadSample();
        var bare = model.Fields.Single(f => f.Name == "bare_name");
        var explicitly = model.Fields.Single(f => f.Name == "explicit_name");

        Assert.False(bare.Nullable);
        Assert.DoesNotContain(bare.Attributes ?? [], a => a.Name == "not_null");
        Assert.False(explicitly.Nullable);
        Assert.True(model.Fields.Single(f => f.Name == "opt_name").Nullable);
    }

    [Fact]
    public void Parser_strips_quotes_from_declared_defaults()
    {
        var (model, _, _) = LoadSample();

        Assert.Equal("NEW", FieldAttributes.EffectiveDefault(model.Fields.Single(f => f.Name == "code")));
        Assert.Equal("in_review", FieldAttributes.EffectiveDefault(model.Fields.Single(f => f.Name == "grade")));
        Assert.Equal("true", FieldAttributes.EffectiveDefault(model.Fields.Single(f => f.Name == "is_active")));
    }

    // ---------------------------------------------------------------- [Required]

    /// <summary>
    /// Keying the attribute off the <c>@not_null</c> token instead of the parsed
    /// nullability would emit for <c>explicit_name</c> and silently skip
    /// <c>bare_name</c>, even though the two columns are identically NOT NULL.
    /// </summary>
    [Fact]
    public void Required_is_emitted_for_every_non_nullable_reference_type()
    {
        var attrs = AttributesByProperty(RenderWrite());

        foreach (var prop in new[] { "BareName", "ExplicitName", "Unbounded", "Memo", "Payload", "Blob", "Code" })
            Assert.Contains("[Required]", attrs[prop]);
    }

    [Fact]
    public void Required_is_not_emitted_for_nullable_or_value_typed_fields()
    {
        var attrs = AttributesByProperty(RenderWrite());

        foreach (var prop in new[]
                 {
                     "OptName", "OptCode",                       // nullable
                     "IsActive", "Qty", "Ratio", "Weight", "Score", // value types
                     "Grade", "PlainGrade",                      // enums are value types
                     "MadeAt", "OwnerId",                        // temporal / Guid
                 })
            Assert.DoesNotContain("[Required]", attrs[prop]);
    }

    /// <summary>
    /// A lookup through a non-nullable FK is itself non-nullable and carries the
    /// <c>string.Empty</c> initializer — which <c>RequiredAttribute</c> rejects.
    /// Emitting the attribute there would turn every create request into a 400
    /// for a property the caller cannot supply, because the view populates it.
    /// </summary>
    [Fact]
    public void Required_is_not_emitted_for_derived_fields_on_the_read_model()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var read = EntityPairRenderer.Render(
            order, "Test.Entities",
            new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal),
            EntityPairRenderer.ExtBacking.Ext).Read;

        var attrs = AttributesByProperty(read);

        // The lookup is non-nullable and a reference type — the exact shape that
        // would attract [Required] if the guard keyed off nullability alone.
        Assert.Contains("string CustomerName { get; set; } = string.Empty;", read);
        Assert.DoesNotContain("[Required]", attrs["CustomerName"]);
        Assert.DoesNotContain("[Required]", attrs["CustomerEmail"]);

        // Stored fields on the same read model still get theirs.
        Assert.Contains("[Required]", attrs["OrderNumber"]);
    }

    /// <summary>
    /// The emitted attribute is deliberately the plain one, which makes the C#
    /// contract <em>stricter</em> than the column it mirrors: SQL NOT NULL
    /// accepts an empty string, <c>RequiredAttribute</c> does not, and it trims
    /// before deciding so whitespace is rejected too. That narrowing is the point
    /// — a blank value arriving where the model declared a required field is the
    /// case this emission exists to reject — but it is a behaviour change for any
    /// caller that was storing blanks, so it is pinned here rather than left to
    /// the default of an attribute someone might later "fix" with
    /// <c>AllowEmptyStrings</c>.
    /// </summary>
    [Fact]
    public void Required_narrows_the_column_domain_by_rejecting_blank_strings()
    {
        var attrs = AttributesByProperty(RenderWrite());

        Assert.Contains("[Required]", attrs["BareName"]);
        Assert.DoesNotContain(attrs["BareName"], a => a.Contains("AllowEmptyStrings", StringComparison.Ordinal));

        var required = new System.ComponentModel.DataAnnotations.RequiredAttribute();
        Assert.False(required.IsValid(""));
        Assert.False(required.IsValid("   "));
        Assert.True(required.IsValid("x"));
    }

    // ---------------------------------------------------------------- [StringLength]

    [Fact]
    public void StringLength_is_emitted_from_the_declared_bound_regardless_of_nullability()
    {
        var attrs = AttributesByProperty(RenderWrite());

        Assert.Contains("[StringLength(50)]", attrs["BareName"]);
        Assert.Contains("[StringLength(60)]", attrs["ExplicitName"]);
        // An optional field is still bounded — reusing the [Required] condition
        // here would drop this one.
        Assert.Contains("[StringLength(70)]", attrs["OptName"]);
        Assert.Contains("[StringLength(10)]", attrs["OptCode"]);
    }

    [Fact]
    public void StringLength_is_absent_where_no_bound_is_declared()
    {
        var attrs = AttributesByProperty(RenderWrite());

        foreach (var prop in new[] { "Unbounded", "Memo", "Payload", "Blob", "Qty", "MadeAt" })
            Assert.DoesNotContain(attrs[prop], a => a.StartsWith("[StringLength", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- declared defaults

    [Fact]
    public void Declared_default_becomes_the_property_initializer()
    {
        var rendered = RenderWrite();

        Assert.Contains("public bool IsActive { get; set; } = true;", rendered);
        Assert.Contains("public int Qty { get; set; } = 3;", rendered);
        // A string default replaces the string.Empty fallback — without this the
        // field would be blank on create and [Required] would reject a request
        // that legitimately omitted it.
        Assert.Contains("public string Code { get; set; } = \"NEW\";", rendered);
    }

    /// <summary>
    /// An unsuffixed literal is a <c>double</c>: assigning it to <c>float</c> or
    /// <c>decimal</c> is CS0664. The generated-source gate parses syntax only, so
    /// the suffix is asserted here instead.
    /// </summary>
    [Fact]
    public void Numeric_defaults_carry_the_suffix_their_clr_type_requires()
    {
        var rendered = RenderWrite();

        Assert.Contains("public decimal Ratio { get; set; } = 0.5m;", rendered);
        Assert.Contains("public float Weight { get; set; } = 1.5f;", rendered);
        Assert.Contains("public double Score { get; set; } = 2.5;", rendered);
    }

    /// <summary>
    /// The default arrives as the bare snake_case member name, so the emitted
    /// expression has to apply the same conversion the enum type itself was
    /// generated with — otherwise it names a member that does not exist.
    /// </summary>
    [Fact]
    public void Enum_default_names_a_member_the_generated_enum_actually_declares()
    {
        var (_, _, enumLookup) = LoadSample();
        var rendered = RenderWrite();

        Assert.Contains("public Grade Grade { get; set; } = Grade.InReview;", rendered);
        // Without a declared default an enum keeps its CLR zero value.
        Assert.Contains("public Grade PlainGrade { get; set; }", rendered);
        Assert.DoesNotContain("PlainGrade { get; set; } =", rendered);

        // Lock the coupling: the member expression must match what EnumRenderer
        // emits for the same input, and both classes keep private PascalCase copies.
        var enumSource = EnumRenderer.Render(enumLookup["Grade"], "Test.Entities");
        Assert.Contains("    InReview,", enumSource);
    }

    /// <summary>
    /// Types with no C# literal form are skipped whatever their default says.
    /// The gate is the type, not a list of function names — a name list lets the
    /// next server-side function through and emits code that will not compile.
    /// </summary>
    [Fact]
    public void Defaults_without_a_literal_form_are_not_emitted()
    {
        var rendered = RenderWrite();

        Assert.Contains("public DateTimeOffset MadeAt { get; set; }", rendered);
        Assert.DoesNotContain("now()", rendered);
    }

    /// <summary>
    /// Seeding an optional property would change what "unset" means on it — a
    /// separate trade-off from carrying a non-nullable field's declared default,
    /// and deliberately out of scope here.
    /// </summary>
    [Fact]
    public void Nullable_field_keeps_null_even_when_a_default_is_declared()
    {
        var line = PropertyLine(RenderWrite(), "OptCode");

        Assert.Equal("public string? OptCode { get; set; }", line);
    }

    /// <summary>
    /// Every emitted initializer is compiled, not just parsed. Assigning an
    /// unsuffixed literal to <c>float</c> or <c>decimal</c> is CS0664 — a
    /// semantic error that a syntax-only check over generated sources reports as
    /// valid, so it would reach a consumer as a broken build. The enum member
    /// expression is proved the same way: a name the generated enum never
    /// declared is CS0117 here rather than downstream.
    /// </summary>
    [Fact]
    public void Emitted_initializers_compile_against_the_generated_enum()
    {
        var (_, _, enumLookup) = LoadSample();
        var properties = RenderWrite().Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("{ get; set; }", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(properties, l => l.Contains("= 0.5m;", StringComparison.Ordinal));

        var source = $$"""
            using System;
            {{EnumRenderer.Render(enumLookup["Grade"], "Probe").Replace("// <auto-generated> mdd-booster; DO NOT EDIT.</auto-generated>", "")}}

            public class Shape
            {
            {{string.Join("\n", properties.Select(p => "    " + p))}}
            }
            """;

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "InitializerProbe",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(a.Location)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();

        Assert.Empty(errors);
    }

    // ---------------------------------------------------------------- cross-target parity

    /// <summary>
    /// The point of the change is that one declaration reaches both targets, so
    /// the check is a correspondence rather than a per-field assertion: every
    /// NOT NULL string column has a <c>[Required]</c> property and vice versa,
    /// and every declared bound is the same number on both sides. An
    /// implementation keyed off <c>@not_null</c> under-emits and fails here even
    /// when each individual field test is written to pass.
    /// </summary>
    [Fact]
    public void Emitted_attributes_correspond_to_the_sql_columns_field_for_field()
    {
        var (model, enums, enumLookup) = LoadSample();
        var attrs = AttributesByProperty(EntityPairRenderer.Render(model, "Test.Entities", enums).Write);

        var expectedRequired = new SortedSet<string>(StringComparer.Ordinal);
        var expectedLengths = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var f in model.Fields.Where(f => f.Kind == FieldKind.Stored))
        {
            if (FieldAttributes.Has(f, "pk")
                || f.Name is "created_at" or "updated_at")
            {
                continue; // elided — IyuEntity provides these
            }

            var property = string.Concat(f.Name.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
            var column = ColumnRenderer.Render(f, enumLookup);

            if (column.Contains(" NOT NULL", StringComparison.Ordinal)
                && CSharpTypeMapper.IsReferenceType(f.Type!))
            {
                expectedRequired.Add(property);
            }

            // Only columns whose C# side is actually a string can carry the
            // attribute. An enum column is NVARCHAR too, but that width is how
            // the SQL target stores enum values — not a bound the model declared,
            // and [StringLength] on a `Grade` property would mean nothing.
            // The number still comes from the SQL column, so this is not circular.
            var bound = Regex.Match(column, @"NVARCHAR\((\d+)\)", RegexOptions.IgnoreCase);
            if (bound.Success && CSharpTypeMapper.MapFieldType(f.Type!, enums) == "string")
                expectedLengths[property] = int.Parse(bound.Groups[1].Value);
        }

        var actualRequired = new SortedSet<string>(
            attrs.Where(kv => kv.Value.Contains("[Required]")).Select(kv => kv.Key), StringComparer.Ordinal);

        var actualLengths = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (property, lines) in attrs)
        {
            var m = lines.Select(l => Regex.Match(l, @"^\[StringLength\((\d+)\)\]$")).FirstOrDefault(x => x.Success);
            if (m is not null)
                actualLengths[property] = int.Parse(m.Groups[1].Value);
        }

        // Guard against a vacuous pass — the fixture must actually exercise both.
        Assert.NotEmpty(expectedRequired);
        Assert.NotEmpty(expectedLengths);

        Assert.Equal(expectedRequired, actualRequired);
        Assert.Equal(expectedLengths, actualLengths);
    }
}
