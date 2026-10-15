using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// An index's keyword arguments are not columns. The specification's own
/// <c>@index(customer_id, order_date, name: "customer_orders")</c> used to reach this generator with a third column
/// named <c>name: "customer_orders"</c> — uncompilable C# and SQL naming a column that does not exist. The keywords
/// now arrive apart from the columns; <c>fulltext: true</c>, which no target generates, is <c>MDD041</c>.
/// </summary>
public class IndexKeywordArgumentTests
{
    private const string Model = """
# Namespace: x

## Product
- id: identifier @pk @generated
- sku: string(20) @not_null
- name: string(100) @not_null
- @index(sku, name, name: "product_search", fulltext: true)
- @unique(sku, name: "uq_sku")
""";

    private static (IReadOnlyList<ResolvedModel> Models, IReadOnlyList<SemanticDiagnostic> Diagnostics) Load(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-index-kw-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return (models, new SemanticAnalyzer(models, ast.Enums).Analyze());
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Keyword_arguments_are_read_apart_from_the_columns()
    {
        var entries = SectionIndexParser.Parse(Load(Model).Models.Single());

        Assert.Equal(["sku", "name"], entries[0].Columns);
        Assert.Equal("product_search", entries[0].Name);
        Assert.True(entries[0].FullText);
        Assert.Equal(["sku"], entries[1].Columns);
        Assert.Equal("uq_sku", entries[1].Name);
        Assert.False(entries[1].FullText);
    }

    [Fact]
    public void A_full_text_index_is_MDD041_a_warning()
    {
        var diagnostic = Assert.Single(Load(Model).Diagnostics, d => d.Code == "MDD041");

        Assert.Equal(SemanticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("@index(sku, name, fulltext: true)", diagnostic.Message);
    }

    [Fact]
    public void An_ordinary_index_is_not_MDD041()
    {
        var diagnostics = Load(Model.Replace(", fulltext: true", "", StringComparison.Ordinal)).Diagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD041");
    }
}

/// <summary>
/// The name the model gives an index is its database name — in both SQL dialects and the EF model — whichever form
/// gave it: a directive's <c>name:</c>, or the item name of an <c>### Indexes</c> item (specification §3.3.3), in the
/// <c>- name: @index(…)</c> form and the <c>fields:</c> form alike. <c>MDD042</c> refuses a name that cannot be one:
/// not an identifier, or used twice. An item that names no columns is <c>MDD043</c> rather than nothing.
/// </summary>
public class DeclaredIndexNameTests
{
    private static string Build(string m3l, string target)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdd-index-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "model.m3l.md"), m3l);
            File.WriteAllText(Path.Combine(dir, "mdd.json"), target);
            Assert.Equal(0, new MddBooster.Cli.Commands.BuildCommand().Run(dir));
            return string.Join("\n", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".sql", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private const string Model = """
# Namespace: x

## Product
- id: identifier @pk @generated
- sku: string(20) @not_null
- name: string(100) @not_null
- created_at: timestamp = now()
- updated_at: timestamp = now()
- @index(sku, name, name: "product_search")
- @unique(sku, name: "uq_product_sku")

### Indexes
- by_name: @index(name)
- by_created(For the newest first)
  - fields: [created_at]
- uq_created_sku
  - fields: [created_at, sku]
  - unique: true
""";

    private static string Targets(string dialect) => $$"""
        { "sources": ["model.m3l.md"], "targets": [
          { "type": "Sql", "dialect": "{{dialect}}", "outputPath": "./sql", "emitSqlProj": false },
          { "type": "Model", "dialect": "{{dialect}}", "outputPath": "./model", "namespace": "Probe", "dbContextName": "ProbeDb" } ] }
        """;

    [Theory]
    [InlineData("tsql", "[product_search]", "[uq_product_sku]", "[by_name]")]
    [InlineData("postgres", "product_search", "uq_product_sku", "by_name")]
    public void A_directive_name_and_an_item_name_each_name_the_index(string dialect, string index, string unique, string item)
    {
        var output = Build(Model, Targets(dialect));

        Assert.Contains(index, output);
        Assert.Contains(unique, output);
        Assert.Contains(item, output);
        Assert.Contains("HasDatabaseName(\"product_search\")", output);
        Assert.Contains("HasDatabaseName(\"uq_product_sku\")", output);
        Assert.Contains("HasDatabaseName(\"by_name\")", output);
        // The generated name is what the item name replaces — it must not be emitted as well.
        Assert.DoesNotContain(dialect == "tsql" ? "IX_Product_Name" : "ix_product_name", output);
    }

    [Theory]
    [InlineData("tsql", "INDEX [by_created]", "[CreatedAt]", "[uq_created_sku]")]
    [InlineData("postgres", "INDEX by_created", "created_at", "uq_created_sku")]
    public void An_item_written_with_fields_is_an_index(string dialect, string index, string column, string unique)
    {
        // The section form the specification recommends for several indexes (§3.3.3). It used to generate nothing at
        // all, without a word — the entry carries `fields`, not the `args` the directive forms carry.
        var output = Build(Model, Targets(dialect));

        Assert.Contains(index, output);
        Assert.Contains(column, output);
        Assert.Contains(unique, output);
        Assert.Contains("HasDatabaseName(\"by_created\")", output);
        Assert.Contains("HasDatabaseName(\"uq_created_sku\")", output);
        Assert.DoesNotContain("For the newest first", output);
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-index-name-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return new SemanticAnalyzer(models, ast.Enums).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void A_name_that_is_not_an_identifier_is_MDD042()
    {
        var diagnostic = Assert.Single(Analyze(Model.Replace("product_search", "product search];--", StringComparison.Ordinal)),
            d => d.Code == "MDD042");

        Assert.Equal(SemanticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_name_used_twice_in_the_build_is_MDD042()
    {
        var twice = Model + "\n## Order\n- id: identifier @pk @generated\n- sku: string(20)\n- @index(sku, name: \"PRODUCT_SEARCH\")\n";

        // SQL Server only: in PostgreSQL the upper-case spelling is a second, separate MDD042.
        var diagnostic = Assert.Single(Analyze(twice, SqlDialect.SqlServer), d => d.Code == "MDD042");

        Assert.Contains("'Product'", diagnostic.Message);
    }

    [Fact]
    public void An_item_name_that_is_not_an_identifier_is_MDD042()
    {
        var diagnostic = Assert.Single(Analyze(Model.Replace("- by_created(", "- 최신순(", StringComparison.Ordinal)),
            d => d.Code == "MDD042");

        Assert.Contains("최신순", diagnostic.Message);
    }

    [Fact]
    public void An_item_name_used_in_two_models_is_MDD042()
    {
        // A common item name (`by_name`) is easy to repeat from table to table, and index names are schema-wide.
        var twice = Model + "\n## Order\n- id: identifier @pk @generated\n- name: string(20)\n\n### Indexes\n- by_name: @index(name)\n";

        var diagnostic = Assert.Single(Analyze(twice), d => d.Code == "MDD042");

        Assert.Contains("by_name", diagnostic.Message);
    }

    [Theory]
    [InlineData("IX_Product_Sku")]   // the name SQL Server would give the field's index
    [InlineData("ix_product_sku")]   // the name PostgreSQL would give it
    public void A_declared_name_another_index_is_generated_under_is_MDD042(string declared)
    {
        // `sku` carries a field-level @index, so a generator names that index itself; declaring the same name for a
        // different index puts two objects under one schema-wide name, which only the deployment would reveal.
        var model = Model.Replace("- sku: string(20) @not_null", "- sku: string(20) @not_null @index", StringComparison.Ordinal)
                    + $"\n- {declared}: @index(name)\n";

        // Each spelling in the dialect that writes it (PostgreSQL would refuse the upper-case one before comparing it).
        var built = declared == declared.ToLowerInvariant() ? SqlDialect.PostgreSql : SqlDialect.SqlServer;
        var diagnostic = Assert.Single(Analyze(model, built), d => d.Code == "MDD042" && d.Message.Contains("생성"));

        Assert.Contains(declared, diagnostic.Message);
    }

    [Fact]
    public void A_declared_name_equal_to_its_own_generated_name_is_not_MDD042()
    {
        // Naming an index what it would have been called anyway takes that name out of the generated set — nothing
        // else is generated under it.
        var model = Model.Replace("- by_name: @index(name)", "- IX_Product_Name: @index(name)", StringComparison.Ordinal);

        Assert.DoesNotContain(Analyze(model, SqlDialect.SqlServer), d => d.Code == "MDD042");
    }

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string m3l, params SqlDialect[] dialects)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-index-name-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            return new SemanticAnalyzer(models, ast.Enums, dialects).Analyze();
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// A generated name collides only in the dialect that generates it. Checked against both, a name SQL Server would
    /// give another index stopped a build that only writes PostgreSQL — a false alarm about output nobody produces.
    /// The two spellings must differ beyond case to be told apart: both dialects compare names case-insensitively
    /// (PostgreSQL folds an unquoted name), so <c>IX_Product_Sku</c> is <c>ix_product_sku</c> in either.
    /// </summary>
    [Theory]
    [InlineData("IX_Product_BatchCode", SqlDialect.SqlServer, true)]
    [InlineData("IX_Product_BatchCode", SqlDialect.PostgreSql, false)]
    [InlineData("ix_product_batch_code", SqlDialect.PostgreSql, true)]
    [InlineData("ix_product_batch_code", SqlDialect.SqlServer, false)]
    public void A_declared_name_collides_only_in_a_dialect_that_is_built(string declared, SqlDialect built, bool collides)
    {
        var model = WithBatchCode + $"\n- {declared}: @index(name)\n";

        Assert.Equal(collides, Analyze(model, built).Any(d => d.Code == "MDD042" && d.Message.Contains("생성")));
    }

    private static string WithBatchCode =>
        Model.Replace("- sku: string(20) @not_null", "- sku: string(20) @not_null\n- batch_code: string(20) @index", StringComparison.Ordinal);

    [Fact]
    public void A_build_that_writes_only_sql_server_accepts_a_name_postgres_would_generate()
    {
        var output = Build(WithBatchCode + "\n- ix_product_batch_code: @index(name)\n", Targets("tsql"));

        Assert.Contains("[ix_product_batch_code]", output);
    }

    /// <summary>
    /// PostgreSQL writes a declared name unquoted, so it must already be the folded form. The generator refused other
    /// spellings with an exception at generation time; in a build that writes PostgreSQL the analyzer says so first,
    /// with the rest of the model's diagnostics. A SQL Server build takes the same name as it is.
    /// </summary>
    [Theory]
    [InlineData(SqlDialect.PostgreSql, true)]
    [InlineData(SqlDialect.SqlServer, false)]
    public void A_name_postgres_cannot_write_unquoted_is_MDD042_only_where_postgres_is_built(SqlDialect built, bool refused)
    {
        var model = Model.Replace("- by_name: @index(name)", "- ByName: @index(name)", StringComparison.Ordinal);

        Assert.Equal(refused, Analyze(model, built).Any(d => d.Code == "MDD042" && d.Message.Contains("ByName")));
    }

    private const string Joined = """
# Namespace: x

## Part
- id: identifier @pk @generated
- a: string(10)
- b: string(10)
- a_b: string(20) @index
- @index(a, b)
""";

    /// <summary>
    /// Two indexes the model does not name can be given the same name: PostgreSQL joins the columns with the same
    /// underscore a column name may contain, so an index on <c>(a, b)</c> and one on <c>a_b</c> are both
    /// <c>ix_part_a_b</c>. Index names are schema-wide; the second <c>CREATE INDEX</c> fails at deployment.
    /// </summary>
    [Fact]
    public void Two_generated_names_that_coincide_are_MDD045_in_that_dialect()
    {
        var diagnostic = Assert.Single(Analyze(Joined, SqlDialect.PostgreSql), d => d.Code == "MDD045");

        Assert.Equal(SemanticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ix_part_a_b", diagnostic.Message);
    }

    [Fact]
    public void Names_that_coincide_only_in_another_dialect_are_not_MDD045()
    {
        // SQL Server writes the columns PascalCased (`IX_Part_A_B` and `IX_Part_AB`) — no collision there.
        Assert.DoesNotContain(Analyze(Joined, SqlDialect.SqlServer), d => d.Code == "MDD045");
    }

    private static string Wide(int columns) =>
        "# Namespace: x\n\n## MaintenanceScheduleRevisionHistoryEntry\n- id: identifier @pk @generated\n"
        + string.Concat(Enumerable.Range(1, columns).Select(i => $"- recorded_measurement_value_{i}: string(10)\n"))
        + $"- @index({string.Join(", ", Enumerable.Range(1, columns).Select(i => $"recorded_measurement_value_{i}"))})\n";

    /// <summary>
    /// A generated name the dialect cannot hold: SQL Server refuses an identifier over 128 characters at deployment,
    /// PostgreSQL over 63 bytes. Reported before anything is written, naming the limit — the fix is to name the index.
    /// </summary>
    [Theory]
    [InlineData(SqlDialect.SqlServer, 6, "128")]
    [InlineData(SqlDialect.PostgreSql, 2, "63")]
    public void A_generated_name_over_the_dialects_limit_is_MDD044(SqlDialect dialect, int columns, string limit)
    {
        var diagnostic = Assert.Single(Analyze(Wide(columns), dialect), d => d.Code == "MDD044");

        Assert.Equal(SemanticSeverity.Error, diagnostic.Severity);
        Assert.Contains(limit, diagnostic.Message);
    }

    [Fact]
    public void A_name_within_sql_servers_limit_is_not_MDD044_there()
    {
        // ~75 characters: over PostgreSQL's 63 bytes, well inside SQL Server's 128.
        Assert.DoesNotContain(Analyze(Wide(2), SqlDialect.SqlServer), d => d.Code == "MDD044");
    }

    [Fact]
    public void A_declared_name_over_the_limit_is_MDD044()
    {
        var model = Model + "\n- " + new string('n', 130) + ": @index(name)\n";

        Assert.Contains(Analyze(model, SqlDialect.SqlServer), d => d.Code == "MDD044");
    }

    [Fact]
    public void An_item_that_names_no_columns_is_MDD043_a_warning()
    {
        var diagnostic = Assert.Single(Analyze(Model + "\n- by_nothing\n  - unique: true\n"), d => d.Code == "MDD043");

        Assert.Equal(SemanticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("by_nothing", diagnostic.Message);
    }

    [Fact]
    public void A_model_whose_items_all_name_columns_is_not_MDD043()
    {
        Assert.DoesNotContain(Analyze(Model), d => d.Code == "MDD043");
    }
}
