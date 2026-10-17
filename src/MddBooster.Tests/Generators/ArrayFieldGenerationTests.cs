using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;
using MddBooster.Generators.TypeScript;
using static MddBooster.Tests.Generators.TypeScript.FormImportFixtures;

namespace MddBooster.Tests.Generators;

/// <summary>
/// Array fields (<c>T[]</c>, specification §2.4.3) across the targets: which ones the build accepts, and what each target
/// emits for one — a JSON text column on SQL Server, a native array on PostgreSQL, a <c>List&lt;T&gt;</c> property, a
/// TypeScript <c>T[]</c>.
/// </summary>
/// <remarks>
/// The fixture carries one field per accepted element type and every nullability (<c>T[]</c>, <c>T?[]</c>, <c>T[]?</c>,
/// <c>T?[]?</c>). The same fixture runs through the C# compile gate and the TypeScript compile gate, so what is pinned here
/// as text is also checked to compile.
/// </remarks>
public class ArrayFieldGenerationTests
{
    private static (M3lAst Ast, List<ResolvedModel> Models) LoadFixture()
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", "array-fields.m3l.md"));
        return (ast, new InterfaceResolver(ast).ResolveAll().ToList());
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string fields, string extra = "")
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-array-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + extra + "## Doc\n- id: identifier @pk @generated\n" + fields);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new SemanticAnalyzer(new InterfaceResolver(ast).ResolveAll(), ast.Enums).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";

    // ---- the build: what it accepts ----

    [Fact]
    public void The_fixture_covering_every_accepted_element_builds_clean()
    {
        var (ast, models) = LoadFixture();
        Assert.Empty(new SemanticAnalyzer(models, ast.Enums).Analyze());
    }

    [Theory]
    [InlineData("- tags: string[]\n")]
    [InlineData("- notes: text?[]\n")]
    [InlineData("- scores: integer[]?\n")]
    [InlineData("- prices: decimal(10,2)[]\n")]
    [InlineData("- amounts: decimal[]\n")]
    [InlineData("- ids: identifier?[]?\n")]
    [InlineData("- tags: string[] @system\n")]
    [InlineData("- tags: string[] @immutable\n")]
    public void An_array_of_an_accepted_element_is_not_refused(string field)
    {
        Assert.DoesNotContain(Analyze(field), d => d.Code is "MDD048" or "MDD001");
    }

    // ---- the build: what it still refuses, and that the message names the combination ----

    [Theory]
    [InlineData("- tags: string(20)[]\n", "길이 상한", "string(20)")]
    [InlineData("- mails: email[]\n", "길이 상한", "email")]
    [InlineData("- phones: phone?[]\n", "길이 상한", "phone")]
    [InlineData("- blobs: binary[]\n", "배열 원소로 생성하지 않는다", "binary")]
    [InlineData("- docs: json[]\n", "배열 원소로 생성하지 않는다", "json")]
    [InlineData("- small: byte[]\n", "배열 원소로 생성하지 않는다", "byte")]
    [InlineData("- times: time[]\n", "배열 원소로 생성하지 않는다", "time")]
    [InlineData("- moments: datetime[]\n", "배열 원소로 생성하지 않는다", "datetime")]
    [InlineData("- ids: identifier[] @reference(Doc)\n", "`@reference`", "연결 모델")]
    [InlineData("- tags: string[] @unique\n", "`@unique`", "키")]
    [InlineData("- tags: string[] @index\n", "`@index`", "키")]
    [InlineData("- tags: string[] @searchable\n", "`@searchable`", "자유 검색")]
    [InlineData("- scores: integer[] @min(0)\n", "`@min`", "개수")]
    [InlineData("- scores: integer[] = []\n", "기본값", "빈 목록")]
    public void A_combination_not_generated_is_refused_by_name(string field, string named, string advice)
    {
        var diagnostics = Analyze(field);
        var refusal = Assert.Single(diagnostics, d => d.Code == "MDD048");
        Assert.Equal(SemanticSeverity.Error, refusal.Severity);
        Assert.Contains("배열", refusal.Message);
        Assert.Contains(named, refusal.Message);
        Assert.Contains(advice, refusal.Message);
        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD001");
    }

    [Fact]
    public void An_array_of_enum_members_is_refused()
    {
        var diagnostics = Analyze("- states: doc_state[]\n", "## doc_state ::enum\n- draft: \"Draft\"\n- done: \"Done\"\n\n");
        var refusal = Assert.Single(diagnostics, d => d.Code == "MDD048");
        Assert.Contains("enum `doc_state`", refusal.Message);
    }

    [Fact]
    public void An_array_a_section_index_names_is_refused()
    {
        var diagnostics = Analyze("- tags: string[]\n\n### Indexes\n- @index(tags)\n");
        var refusal = Assert.Single(diagnostics, d => d.Code == "MDD048");
        Assert.Contains("### Indexes", refusal.Message);
    }

    [Fact]
    public void A_derived_array_is_refused()
    {
        var diagnostics = Analyze("- total: integer\n- totals: integer[] @computed(\"total\")\n");
        var refusal = Assert.Single(diagnostics, d => d.Code == "MDD048");
        Assert.Contains("파생 필드", refusal.Message);
    }

    /// <summary>Two problems on one field are both named, in one diagnostic — the field is fixed in one pass.</summary>
    [Fact]
    public void Every_problem_on_one_field_is_named_in_one_refusal()
    {
        var refusal = Assert.Single(Analyze("- tags: string(20)[] @unique\n"), d => d.Code == "MDD048");
        Assert.Contains("길이 상한", refusal.Message);
        Assert.Contains("`@unique`", refusal.Message);
        Assert.Contains("`string(20)[]`", refusal.Message);
    }

    // ---- SQL Server ----

    [Fact]
    public void Sql_server_stores_an_array_as_json_text_held_to_json_by_a_named_check()
    {
        var (_, models) = LoadFixture();
        var sql = TableRenderer.Render(models.Single(), schema: "dbo");

        var expected = """
-- # Code generated by "mdd-booster"; DO NOT EDIT.
CREATE TABLE [dbo].[ArrayHolder]
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Title] NVARCHAR(100) NOT NULL,
    [Tags] NVARCHAR(MAX) NOT NULL,
    [Notes] NVARCHAR(MAX) NOT NULL,
    [Scores] NVARCHAR(MAX) NULL,
    [Totals] NVARCHAR(MAX) NOT NULL,
    [Ranks] NVARCHAR(MAX) NULL,
    [Flags] NVARCHAR(MAX) NOT NULL,
    [Prices] NVARCHAR(MAX) NOT NULL,
    [Ratios] NVARCHAR(MAX) NOT NULL,
    [Weights] NVARCHAR(MAX) NOT NULL,
    [RelatedIds] NVARCHAR(MAX) NOT NULL,
    [Dates] NVARCHAR(MAX) NOT NULL,
    [Stamps] NVARCHAR(MAX) NULL,
    [SecretCodes] NVARCHAR(MAX) NOT NULL,
    [CreatedAt] DATETIMEOFFSET NOT NULL,
    [UpdatedAt] DATETIMEOFFSET NOT NULL,
    CONSTRAINT [CK_ArrayHolder_Tags] CHECK (ISJSON([Tags]) = 1),
    CONSTRAINT [CK_ArrayHolder_Notes] CHECK (ISJSON([Notes]) = 1),
    CONSTRAINT [CK_ArrayHolder_Scores] CHECK (ISJSON([Scores]) = 1),
    CONSTRAINT [CK_ArrayHolder_Totals] CHECK (ISJSON([Totals]) = 1),
    CONSTRAINT [CK_ArrayHolder_Ranks] CHECK (ISJSON([Ranks]) = 1),
    CONSTRAINT [CK_ArrayHolder_Flags] CHECK (ISJSON([Flags]) = 1),
    CONSTRAINT [CK_ArrayHolder_Prices] CHECK (ISJSON([Prices]) = 1),
    CONSTRAINT [CK_ArrayHolder_Ratios] CHECK (ISJSON([Ratios]) = 1),
    CONSTRAINT [CK_ArrayHolder_Weights] CHECK (ISJSON([Weights]) = 1),
    CONSTRAINT [CK_ArrayHolder_RelatedIds] CHECK (ISJSON([RelatedIds]) = 1),
    CONSTRAINT [CK_ArrayHolder_Dates] CHECK (ISJSON([Dates]) = 1),
    CONSTRAINT [CK_ArrayHolder_Stamps] CHECK (ISJSON([Stamps]) = 1),
    CONSTRAINT [CK_ArrayHolder_SecretCodes] CHECK (ISJSON([SecretCodes]) = 1)
)
GO
""";
        Assert.Equal(Normalize(expected), Normalize(sql));
    }

    // ---- PostgreSQL ----

    [Fact]
    public void Postgres_stores_an_array_natively_and_holds_non_null_elements_by_a_named_check()
    {
        var (ast, models) = LoadFixture();
        var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var tableNames = PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name));
        var enums = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var artifact = PgTableRenderer.Render(models.Single(), "public", tableNames, lookup, enums);

        // `notes text?[]` and `ranks short?[]?` declare nullable elements, so they carry no element CHECK.
        var expected = """
-- # Code generated by "mdd-booster"; DO NOT EDIT.
CREATE TABLE public.array_holder
(
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    title varchar(100) NOT NULL,
    tags text[] NOT NULL,
    notes text[] NOT NULL,
    scores integer[],
    totals bigint[] NOT NULL,
    ranks smallint[],
    flags boolean[] NOT NULL,
    prices numeric(10,2)[] NOT NULL,
    ratios real[] NOT NULL,
    weights double precision[] NOT NULL,
    related_ids uuid[] NOT NULL,
    dates date[] NOT NULL,
    stamps timestamptz[],
    secret_codes text[] NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_array_holder PRIMARY KEY (id),
    CONSTRAINT ck_array_holder_tags CHECK (array_position(tags, NULL) IS NULL),
    CONSTRAINT ck_array_holder_scores CHECK (array_position(scores, NULL) IS NULL),
    CONSTRAINT ck_array_holder_totals CHECK (array_position(totals, NULL) IS NULL),
    CONSTRAINT ck_array_holder_flags CHECK (array_position(flags, NULL) IS NULL),
    CONSTRAINT ck_array_holder_prices CHECK (array_position(prices, NULL) IS NULL),
    CONSTRAINT ck_array_holder_ratios CHECK (array_position(ratios, NULL) IS NULL),
    CONSTRAINT ck_array_holder_weights CHECK (array_position(weights, NULL) IS NULL),
    CONSTRAINT ck_array_holder_related_ids CHECK (array_position(related_ids, NULL) IS NULL),
    CONSTRAINT ck_array_holder_dates CHECK (array_position(dates, NULL) IS NULL),
    CONSTRAINT ck_array_holder_stamps CHECK (array_position(stamps, NULL) IS NULL),
    CONSTRAINT ck_array_holder_secret_codes CHECK (array_position(secret_codes, NULL) IS NULL)
);
""";
        Assert.Equal(Normalize(expected), Normalize(artifact.Sql));
    }

    // ---- Model ----

    [Fact]
    public void The_entity_carries_a_list_per_array_with_both_nullabilities_and_an_empty_list_when_required()
    {
        var (_, models) = LoadFixture();
        var pair = EntityPairRenderer.Render(models.Single(), "Arrays.Entities");

        foreach (var cls in new[] { pair.Write, pair.Read })
        {
            Assert.Contains("using System.Collections.Generic;", cls);
            Assert.Contains("    public List<string> Tags { get; set; } = [];", cls);
            Assert.Contains("    public List<string?> Notes { get; set; } = [];", cls);
            Assert.Contains("    public List<int>? Scores { get; set; }", cls);
            Assert.Contains("    public List<short?>? Ranks { get; set; }", cls);
            Assert.Contains("    public List<decimal> Prices { get; set; } = [];", cls);
            Assert.Contains("    public List<Guid> RelatedIds { get; set; } = [];", cls);
            Assert.Contains("    public List<DateOnly> Dates { get; set; } = [];", cls);
            Assert.Contains("    public List<DateTimeOffset>? Stamps { get; set; }", cls);

            // A length or digit rule describes one value; on a list it would describe the list. The one length rule is
            // the scalar title's.
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(cls, @"\[StringLength\("));
            Assert.Contains("[StringLength(100)]\n    public string Title", Normalize(cls));
            Assert.DoesNotContain("MaxLength", cls);
            Assert.DoesNotContain("DecimalDigits", cls);
            Assert.DoesNotContain("Column(TypeName", cls);
        }

        // A required list is required whatever its element is — List<int> is a reference type.
        Assert.Contains("    [Required]\n    public List<long> Totals { get; set; } = [];", Normalize(pair.Write));
        Assert.DoesNotContain("[Required]\n    public List<int>? Scores", Normalize(pair.Write));

        // @internal stays on the write type only, as for any field.
        Assert.Contains("SecretCodes", pair.Write);
        Assert.DoesNotContain("SecretCodes", pair.Read);

        Assert.Contains("using System.Collections.Generic;", pair.Interface);
        Assert.Contains("    List<string?> Notes { get; }", pair.Interface);
        Assert.Contains("    List<int>? Scores { get; }", pair.Interface);
    }

    [Fact]
    public void On_sql_server_the_context_names_the_array_column_type_for_both_types()
    {
        var (_, models) = LoadFixture();
        var context = DbContextRenderer.Render(models, "ArraysDbContext", "Arrays.Entities");

        Assert.Contains("modelBuilder.Entity<ArrayHolder>().Property(e => e.Tags).HasColumnType(\"nvarchar(max)\");", context);
        Assert.Contains("modelBuilder.Entity<ArrayHolderExt>().Property(e => e.Tags).HasColumnType(\"nvarchar(max)\");", context);
        Assert.Contains("modelBuilder.Entity<ArrayHolder>().Property(e => e.Stamps).HasColumnType(\"nvarchar(max)\");", context);
        // The read type has no @internal property to configure.
        Assert.Contains("modelBuilder.Entity<ArrayHolder>().Property(e => e.SecretCodes)", context);
        Assert.DoesNotContain("modelBuilder.Entity<ArrayHolderExt>().Property(e => e.SecretCodes)", context);
        // A scalar column is left to convention, as before.
        Assert.DoesNotContain("e.Title).HasColumnType", context);
    }

    [Fact]
    public void On_postgres_the_context_leaves_the_array_type_to_the_providers_convention()
    {
        var (_, models) = LoadFixture();
        var context = DbContextRenderer.Render(models, "ArraysDbContext", "Arrays.Entities", postgresNaming: true);

        Assert.Contains("e.Property(x => x.Tags).HasColumnName(\"tags\");", context);
        Assert.DoesNotContain("nvarchar(max)", context);
    }

    /// <summary>A model without an array renders exactly as it did — no directive, no configuration.</summary>
    [Fact]
    public void A_model_without_an_array_gets_no_list_directive_or_column_type()
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", "bank-account.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        var pair = EntityPairRenderer.Render(models.Single(m => m.Name == "BankAccount"), "Ns");

        Assert.DoesNotContain("System.Collections.Generic", pair.Interface + pair.Write + pair.Read);
        Assert.DoesNotContain("HasColumnType", DbContextRenderer.Render(models, "Ctx", "Ns"));
    }

    // ---- TypeScript ----

    [Fact]
    public void The_typescript_interface_carries_each_array_as_an_array_type()
    {
        var (_, models) = LoadFixture();
        var ts = TsInterfaceRenderer.RenderAll(models);

        Assert.Contains("  Tags: string[]\n", Normalize(ts));
        Assert.Contains("  Notes: (string | null)[]\n", Normalize(ts));
        Assert.Contains("  Scores: number[] | null\n", Normalize(ts));
        Assert.Contains("  Ranks: (number | null)[] | null\n", Normalize(ts));
        Assert.Contains("  Flags: boolean[]\n", Normalize(ts));
        Assert.Contains("  RelatedIds: string[]\n", Normalize(ts));
        Assert.DoesNotContain("SecretCodes", ts);
    }

    /// <summary>
    /// No control in the consumer contract edits a list, so an array field is left out of the generated form — the
    /// component, its override keys and its form-state helpers alike — rather than given a scalar control.
    /// </summary>
    [Fact]
    public void The_generated_form_leaves_array_fields_out_consistently()
    {
        var (_, models) = LoadFixture();
        var form = TsFormRenderer.RenderAll(models, [], TestImports)["ArrayHolder"];

        Assert.Contains("Title", form);
        foreach (var array in new[] { "Tags", "Notes", "Scores", "Prices", "RelatedIds", "Stamps" })
            Assert.DoesNotContain(array, form);
    }
}
