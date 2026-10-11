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

        var diagnostic = Assert.Single(Analyze(twice), d => d.Code == "MDD042");

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

        var diagnostic = Assert.Single(Analyze(model), d => d.Code == "MDD042");

        Assert.Contains(declared, diagnostic.Message);
        Assert.Contains("생성", diagnostic.Message);
    }

    [Fact]
    public void A_declared_name_equal_to_its_own_generated_name_is_not_MDD042()
    {
        // Naming an index what it would have been called anyway takes that name out of the generated set — nothing
        // else is generated under it.
        var model = Model.Replace("- by_name: @index(name)", "- IX_Product_Name: @index(name)", StringComparison.Ordinal);

        Assert.DoesNotContain(Analyze(model), d => d.Code == "MDD042");
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
