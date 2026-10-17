using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A delete action declared with <c>on_delete</c> — the extended field format's <c>- on_delete: cascade</c>, or the
/// attribute <c>@on_delete(cascade)</c> (M3L §3.2, §10.8) — reaches the schema and the EF model like the symbol after
/// <c>@reference(X)</c> does. It was known by name and read by nothing: the declaration was dropped without a word,
/// and a key declared to cascade did not.
/// </summary>
public class DeclaredOnDeleteTests
{
    private const string Source = """
        # Namespace: test.declaredOnDelete

        ## Customer
        - id: identifier @pk

        ## Invoice
        - id: identifier @pk
        - owner_id: identifier
          - reference: Customer
          - on_delete: cascade
        - guard_id: identifier @reference(Customer) @on_delete(restrict)
        - cleared_id: identifier? @reference(Customer) @on_delete(set_null)
        - kept_id: identifier @reference(Customer) @on_delete(no_action)
        - plain_id: identifier @reference(Customer)
        """;

    private static (IReadOnlyList<ResolvedModel> Models, M3lAst Ast) Load(string source)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-declared-ondelete-{Guid.NewGuid():N}.m3l.md");
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

    private static IReadOnlyList<SemanticDiagnostic> Analyze(string source)
    {
        var (models, ast) = Load(source);
        return new SemanticAnalyzer(models, ast.Enums).Analyze();
    }

    [Fact]
    public void Tsql_writes_each_declared_action()
    {
        var (models, _) = Load(Source);

        var sql = TableRenderer.Render(models.Single(m => m.Name == "Invoice"), "dbo", allModels: models);

        Assert.Contains("[OwnerId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE CASCADE", sql);
        Assert.Contains("[GuardId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE NO ACTION", sql);
        Assert.Contains("[ClearedId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE SET NULL", sql);
        Assert.Contains("[KeptId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[Customer]([Id]) ON DELETE NO ACTION", sql);
        Assert.Matches(@"\[PlainId\] UNIQUEIDENTIFIER NOT NULL REFERENCES \[dbo\]\.\[Customer\]\(\[Id\]\)(?! ON DELETE)", sql);
    }

    [Fact]
    public void Postgres_writes_each_declared_action()
    {
        var (models, ast) = Load(Source);
        var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var tables = MddBooster.Core.Naming.PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name));
        var enums = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var sql = PgTableRenderer.Render(lookup["Invoice"], "public", tables, lookup, enums).Sql;

        Assert.Contains("FOREIGN KEY (owner_id) REFERENCES public.customer (id) ON DELETE CASCADE", sql);
        Assert.Contains("FOREIGN KEY (guard_id) REFERENCES public.customer (id) ON DELETE RESTRICT", sql);
        Assert.Contains("FOREIGN KEY (cleared_id) REFERENCES public.customer (id) ON DELETE SET NULL", sql);
        Assert.Contains("FOREIGN KEY (kept_id) REFERENCES public.customer (id) ON DELETE NO ACTION", sql);
    }

    /// <summary>A declared CASCADE is configured on the EF model too — on an optional key EF's convention would otherwise only clear the tracked rows.</summary>
    [Fact]
    public void The_ef_model_carries_a_declared_cascade()
    {
        var (models, _) = Load(Source);

        var invoice = ModelMappingRenderer.Render(models, "AppDbContext", "Test.Entities")["Invoice"];

        Assert.Contains("builder.HasOne(e => e.Owner).WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Cascade);", invoice);
        Assert.Contains("builder.HasOne(e => e.Guard).WithMany().HasForeignKey(e => e.GuardId).OnDelete(DeleteBehavior.Restrict);", invoice);
    }

    [Fact]
    public void A_symbol_and_an_on_delete_that_disagree_are_MDD046()
    {
        var diagnostic = Assert.Single(
            Analyze(Source + "\n- both_id: identifier? @reference(Customer)? @on_delete(cascade)\n"),
            d => d.Code == "MDD046");

        Assert.Equal(SemanticSeverity.Error, diagnostic.Severity);
        Assert.Contains("both_id", diagnostic.Message);
    }

    [Fact]
    public void A_symbol_and_an_on_delete_that_agree_are_accepted()
    {
        Assert.DoesNotContain(
            Analyze(Source + "\n- both_id: identifier? @reference(Customer)? @on_delete(set_null)\n"),
            d => d.Code == "MDD046");
    }

    [Fact]
    public void An_action_the_language_does_not_define_is_MDD046()
    {
        var diagnostic = Assert.Single(
            Analyze(Source + "\n- odd_id: identifier @reference(Customer) @on_delete(delete_everything)\n"),
            d => d.Code == "MDD046");

        Assert.Contains("delete_everything", diagnostic.Message);
    }

    [Fact]
    public void A_declared_set_null_on_a_required_key_is_MDD023()
    {
        Assert.Contains(
            Analyze(Source + "\n- required_id: identifier @reference(Customer) @on_delete(set_null)\n"),
            d => d.Code == "MDD023");
    }
}
