using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// The symbol after <c>@reference(X)</c> declares what happens on delete (M3L §8.4.1): <c>!</c> NO
/// ACTION, <c>?</c> SET NULL, <c>!!</c> RESTRICT. Both SQL dialects write it; a reference without
/// a symbol writes no clause.
/// </summary>
public class ReferentialActionTests
{
    private const string Source = """
        # Namespace: test.onDelete

        ## Customer
        - id: identifier @pk

        ## Invoice
        - id: identifier @pk
        - plain_id: identifier @reference(Customer)
        - kept_id: identifier @reference(Customer)!
        - cleared_id: identifier? @reference(Customer)?
        - guarded_id: identifier @reference(Customer)!!
        """;

    private static (IReadOnlyList<ResolvedModel> Models, M3lAst Ast) Load(string source)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-ondelete-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, source);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return (new InterfaceResolver(ast).ResolveAll(), ast);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Tsql_writes_each_declared_action_and_nothing_for_a_bare_reference()
    {
        var (models, _) = Load(Source);

        var sql = TableRenderer.Render(models.Single(m => m.Name == "Invoice"), "dbo", allModels: models);

        Assert.Matches(@"\[PlainId\] UNIQUEIDENTIFIER NOT NULL REFERENCES \[dbo\]\.\[Customer\]\(\[Id\]\)(?! ON DELETE)", sql);
        Assert.Contains("[KeptId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE NO ACTION", sql);
        Assert.Contains("[ClearedId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE SET NULL", sql);
        // SQL Server has no RESTRICT; its NO ACTION is checked immediately, which is the same thing.
        Assert.Contains("[GuardedId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE NO ACTION", sql);
        Assert.DoesNotContain("CASCADE", sql);
    }

    [Fact]
    public void Postgres_writes_each_declared_action_and_nothing_for_a_bare_reference()
    {
        var (models, ast) = Load(Source);
        var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var tableNames = PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name));
        var enums = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var sql = PgTableRenderer.Render(lookup["Invoice"], "public", tableNames, lookup, enums).Sql;

        Assert.Matches(@"FOREIGN KEY \(plain_id\) REFERENCES public\.customer \(id\)(?! ON DELETE)", sql);
        Assert.Contains("FOREIGN KEY (kept_id) REFERENCES public.customer (id) ON DELETE NO ACTION", sql);
        Assert.Contains("FOREIGN KEY (cleared_id) REFERENCES public.customer (id) ON DELETE SET NULL", sql);
        Assert.Contains("FOREIGN KEY (guarded_id) REFERENCES public.customer (id) ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("CASCADE", sql);
    }

    [Fact]
    public void Set_null_on_a_key_that_cannot_be_null_is_MDD023()
    {
        var (models, ast) = Load("""
            # Namespace: test.onDelete

            ## Customer
            - id: identifier @pk

            ## Invoice
            - id: identifier @pk
            - customer_id: identifier @reference(Customer)?
            """);

        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

        var d = Assert.Single(diagnostics, d => d.Code == "MDD023");
        Assert.Equal(SemanticSeverity.Error, d.Severity);
        Assert.Contains("Invoice.customer_id", d.Message);
    }

    [Fact]
    public void Set_null_on_a_nullable_key_is_accepted()
    {
        var (models, ast) = Load(Source);

        var diagnostics = new SemanticAnalyzer(models, ast.Enums).Analyze();

        Assert.DoesNotContain(diagnostics, d => d.Code == "MDD023");
    }
}
