using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Every name in an expression is read against what its context can see (M3L §10.7.1), on both dialects: a view's filter
/// reads its relations (a bare name its <c>from</c>), a view's computed column reads the view's own row (a
/// <c>Relation.field</c> the column that projects it), a rollup's filter reads the rolled-up rows and <c>$parent</c>, a
/// model's computed field reads the fields before it. A name the context does not have stops the build — before, it reached
/// the database as a column that was not there, or as one there twice.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class ViewExpressionScopeTests
{
    private const string TsqlTarget = """{ "type": "Sql", "projectPath": "../db", "schema": "dbo", "emitSqlProj": false }""";
    private const string PgTarget = """{ "type": "Sql", "dialect": "postgres", "projectPath": "../db" }""";

    private const string Models = """
        # Namespace: test

        ## Client
        - id: identifier @pk @generated
        - name: string(100) @not_null
        - region: string(20)
        - active: boolean

        ## Purchase
        - id: identifier @pk @generated
        - client_id: identifier @reference(Client)
        - region: string(20)
        - amount: decimal(12,2) @not_null

        """;

    private const string View = """
        ## ClientPurchase ::view
        ### Source
        - from: Purchase
        - join: Client on Purchase.client_id = Client.id
        - where: "region = 'east' AND Client.active"

        - client_name: string @from(Client.name)
        - amount: decimal(12,2) @from(Purchase.amount)
        - label: string @computed("concat(Client.name, ': ', amount)")
        """;

    private (int Exit, string Stderr, string Root) Build(string model, string target)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-vscope-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "db"));
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
            { "sources": ["./tables.m3l.md"], "targets": [ {{target}} ] }
            """);
        using var err = new ConsoleErrorCapture(this);
        var exit = new BuildCommand().Run(mddDir);
        return (exit, err.Text, root);
    }

    private static string Read(string root, string file) =>
        File.ReadAllText(Directory.GetFiles(root, file, SearchOption.AllDirectories).Single());

    [Fact]
    public void A_view_filter_reads_its_from_by_a_bare_name_and_a_computed_column_reads_the_projection_on_sql_server()
    {
        var (exit, stderr, root) = Build(Models + View, TsqlTarget);
        try
        {
            Assert.True(exit == 0, stderr);
            var sql = Read(root, "ClientPurchase.sql");
            // Both relations have a region: the bare name is the view's from, qualified — not an ambiguous column.
            Assert.Contains("WHERE [Purchase].[Region] = N'east' AND ([Client].[Active] = 1)", sql);
            // Client.name is the column the view projects it as — the computed layer reads the view's row, where the
            // relation is not in scope.
            Assert.Contains("CONCAT([ClientName], N': ', [Amount]) AS [Label]", sql);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_same_view_on_postgresql()
    {
        var (exit, stderr, root) = Build(Models + View, PgTarget);
        try
        {
            Assert.True(exit == 0, stderr);
            var sql = Read(root, "client_purchase.sql");
            Assert.Contains("WHERE purchase.region = 'east' AND client.active", sql);
            Assert.Contains("concat(client_name, ': ', amount) AS label", sql);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// A platform filter is the author's text, but <c>Relation.field</c> in it names the view's relations as the model does
    /// (the specification's own example) — on PostgreSQL it is spelled with the alias each relation has there.
    /// </summary>
    [Fact]
    public void A_postgresql_filter_names_relations_as_the_model_does()
    {
        var (exit, stderr, root) = Build(Models + View.Replace("- where: \"region = 'east' AND Client.active\"",
            "- where_raw: \"Client.region ILIKE 'e%' AND Purchase.amount > 0\" platform: postgresql"), PgTarget);
        try
        {
            Assert.True(exit == 0, stderr);
            Assert.Contains("WHERE client.region ILIKE 'e%' AND purchase.amount > 0", Read(root, "client_purchase.sql"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(TsqlTarget)]
    [InlineData(PgTarget)]
    public void A_name_the_context_cannot_read_stops_the_build(string target)
    {
        var (exit, stderr, root) = Build(Models.Replace("- active: boolean", """
            - active: boolean
            - shown: string @computed("concat(nickname, '!')")
            - later: integer @computed("total + 1")
            - total: integer @computed("1")
            - big_spends: integer @rollup(Purchase.client_id, count, where: "amount > 100 AND b.region = region")
            """) + """
            ## ClientPurchase ::view
            ### Source
            - from: Purchase
            - join: Client on Purchase.client_id = Client.id
            - where: "Client.archived = false"

            - client_name: string @from(Client.name)
            - label: string @computed("concat(Client.region, '-')")
            """, target);
        try
        {
            Assert.NotEqual(0, exit);
            Assert.Contains("MDD037", stderr);
            Assert.Contains("'nickname' is not a field of Client", stderr);
            Assert.Contains("'total' is computed after 'later'", stderr);
            Assert.Contains("'b.region' names the generator's alias for the parent row (write $parent.region)", stderr);
            Assert.Contains("'archived' is not a column of Client", stderr);
            Assert.Contains("'Client.region' is not projected by view ClientPurchase", stderr);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_rollup_filter_may_name_the_rolled_up_model_and_the_qualifier_is_quoted()
    {
        var (exit, stderr, root) = Build(Models.Replace("- active: boolean", """
            - active: boolean
            - east_spends: integer @rollup(Purchase.client_id, count, where: "Purchase.region = $parent.region")
            """), TsqlTarget);
        try
        {
            Assert.True(exit == 0, stderr);
            var sql = Read(root, "ClientFullView.sql");
            // The rolled-up model's name qualifies its own rows — whatever the subquery reads them from.
            Assert.Contains("([Region] = b.[Region])", sql);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
