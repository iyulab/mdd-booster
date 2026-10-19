using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators;

/// <summary>
/// A list of enum members (<c>states: doc_state[]</c>): a <c>List&lt;DocState&gt;</c> the runtime stores by wire name,
/// JSON text on SQL Server, a native array on PostgreSQL, a TypeScript array of the member union — and, under the same
/// opt-in as a single enum, a PostgreSQL CHECK that each element is a member.
/// </summary>
public class EnumArrayFieldGenerationTests
{
    private const string Model = """
        # Namespace: test

        ## doc_state ::enum
        - draft: "Draft"
        - in_review: "In review"
        - done: "Done"

        ## Doc
        - id: identifier @pk @generated
        - states: doc_state[]
        - past_states: doc_state?[]?
        - single: doc_state
        """;

    private static (M3lAst Ast, List<ResolvedModel> Models) Load()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-enum-array-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, Model);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return (ast, new InterfaceResolver(ast).ResolveAll().ToList());
        }
        finally { File.Delete(tmp); }
    }

    private static Dictionary<string, EnumNode> Enums(M3lAst ast) => ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

    [Fact]
    public void A_list_of_enum_members_builds_without_a_refusal()
    {
        var (ast, models) = Load();
        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();
        Assert.DoesNotContain(diagnostics, d => d.Code is "MDD048" or "MDD001");
    }

    [Fact]
    public void The_entity_carries_a_list_of_the_enum()
    {
        var (_, models) = Load();
        var pair = EntityPairRenderer.Render(models.Single(), "Test.Entities", knownEnumNames: new HashSet<string> { "doc_state" });
        var source = string.Concat(pair.Write, pair.Read);
        Assert.Contains("public List<DocState> States { get; set; } = [];", source);
        Assert.Contains("public List<DocState?>? PastStates { get; set; }", source);
    }

    [Fact]
    public void Sql_server_keeps_the_list_as_json_and_holds_only_the_single_enum_to_its_members()
    {
        var (ast, models) = Load();
        var sql = TableRenderer.Render(models.Single(), "dbo", Enums(ast), emitEnumCheckConstraints: true);

        Assert.Contains("[States] NVARCHAR(MAX) NOT NULL", sql);
        Assert.Contains("CHECK (ISJSON([States]) = 1)", sql);
        Assert.Contains("[Single]=N'draft'", sql);
        Assert.DoesNotContain("[States]=N'", sql);
        Assert.DoesNotContain("[PastStates]=N'", sql);
    }

    [Fact]
    public void Postgres_holds_each_element_to_the_members_in_the_columns_one_check_when_opted_in()
    {
        var (ast, models) = Load();
        var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var tableNames = PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name));

        var on = PgTableRenderer.Render(models.Single(), "public", tableNames, lookup, Enums(ast), emitEnumCheckConstraints: true).Sql;
        Assert.Contains("states varchar(20)[] NOT NULL", on);
        Assert.Contains(
            "CHECK (array_position(states, NULL) IS NULL AND array_remove(states, NULL)::text[] <@ ARRAY['draft', 'in_review', 'done']::text[])",
            on);
        Assert.Contains("CHECK (array_remove(past_states, NULL)::text[] <@ ARRAY['draft', 'in_review', 'done']::text[])", on);
        Assert.Contains("CHECK (single IN ('draft', 'in_review', 'done'))", on);

        var off = PgTableRenderer.Render(models.Single(), "public", tableNames, lookup, Enums(ast)).Sql;
        Assert.Contains("CHECK (array_position(states, NULL) IS NULL)", off);
        Assert.DoesNotContain("<@", off);
    }

    [Fact]
    public void The_typescript_interface_carries_an_array_of_the_member_union()
    {
        var (_, models) = Load();
        var ts = TsInterfaceRenderer.RenderAll(models, knownEnumNames: new HashSet<string> { "doc_state" });
        var lines = ts.Replace("\r\n", "\n").Split('\n');
        Assert.Contains("  States: DocState[]", lines);
        Assert.Contains("  PastStates: (DocState | null)[] | null", lines);
    }
}
