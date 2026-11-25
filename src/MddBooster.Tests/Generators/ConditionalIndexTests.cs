using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators;

/// <summary>
/// A conditional index (<c>where:</c> on <c>@index</c> / <c>@unique</c>, M3L §3.3.5) is the same index in both SQL dialects
/// and in the EF model: SQL Server's filtered index, PostgreSQL's partial index, and <c>HasFilter</c> with the text the SQL
/// target writes. A condition no index can carry stops the build (<c>MDD056</c>).
/// </summary>
public sealed class ConditionalIndexTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static List<ResolvedModel> Models(string path)
    {
        var ast = new M3lLoader().LoadFile(path);
        return new InterfaceResolver(ast).ResolveAll().ToList();
    }

    private static List<ResolvedModel> ModelsOf(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"conditional-index-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(path, text);
        try
        {
            return Models(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (string SqlServer, string Postgres, string EfSqlServer, string EfPostgres) Render(ResolvedModel model)
    {
        var models = new List<ResolvedModel> { model };
        var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        return (
            TableRenderer.Render(model, schema: "dbo"),
            PostgresTableRenderer.Render(model, "public", PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name)), lookup).Sql,
            ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns")[model.Name],
            ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns", postgresNaming: true)[model.Name]);
    }

    private static (string SqlServer, string Postgres, string EfSqlServer, string EfPostgres) Report() =>
        Render(Models(FixturePath("table-with-conditional-indexes.m3l.md")).Single(m => m.Name == "Report"));

    [Fact]
    public void A_conditional_unique_is_a_filtered_unique_index_and_the_ef_model_declares_the_same_filter()
    {
        var (sql, pg, ef, efPg) = Report();

        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX [ux_report_schedule] ON [dbo].[Report] ([Type], [PeriodFrom]) WHERE [Source] = N'SCHEDULE';", sql);
        Assert.DoesNotContain("CONSTRAINT [ux_report_schedule]", sql);
        Assert.Contains("CREATE UNIQUE INDEX ux_report_schedule ON public.report (type, period_from) WHERE source = 'SCHEDULE';", pg);
        Assert.DoesNotContain("CONSTRAINT ux_report_schedule", pg);
        Assert.Contains(".HasIndex(x => new { x.Type, x.PeriodFrom }).IsUnique().HasFilter(\"[Source] = N'SCHEDULE'\").HasDatabaseName(\"ux_report_schedule\");", ef);
        Assert.Contains(".HasIndex(x => new { x.Type, x.PeriodFrom }).IsUnique().HasFilter(\"source = 'SCHEDULE'\").HasDatabaseName(\"ux_report_schedule\");", efPg);
    }

    [Fact]
    public void On_sql_server_a_nullable_unique_keeps_its_null_filter_joined_to_the_condition_in_sql_and_ef_alike()
    {
        var (sql, pg, ef, _) = Report();

        const string filter = "[OwnerCode] IS NOT NULL AND [Type] IS NOT NULL AND [IsArchived] = 0";
        Assert.Contains($"CREATE UNIQUE NONCLUSTERED INDEX [ux_report_owner] ON [dbo].[Report] ([OwnerCode], [Type]) WHERE {filter};", sql);
        Assert.Contains($".IsUnique().HasFilter(\"{filter}\").HasDatabaseName(\"ux_report_owner\");", ef);
        // PostgreSQL leaves NULL rows out of uniqueness by itself: the condition alone.
        Assert.Contains("CREATE UNIQUE INDEX ux_report_owner ON public.report (owner_code, type) WHERE is_archived = FALSE;", pg);
    }

    [Fact]
    public void A_conditional_index_carries_its_condition_in_every_index_form()
    {
        var (sql, pg, ef, efPg) = Report();

        Assert.Contains("CREATE NONCLUSTERED INDEX [ix_report_live] ON [dbo].[Report] ([Type]) WHERE [Source] IN (N'SCHEDULE', N'MANUAL') AND [IsArchived] = 0;", sql);
        Assert.Contains("CREATE NONCLUSTERED INDEX [ix_report_recent] ON [dbo].[Report] ([PeriodFrom]) WHERE [OwnerCode] IS NOT NULL;", sql);
        Assert.Contains("CREATE INDEX ix_report_live ON public.report (type) WHERE source IN ('SCHEDULE', 'MANUAL') AND is_archived = FALSE;", pg);
        Assert.Contains("CREATE INDEX ix_report_recent ON public.report (period_from) WHERE owner_code IS NOT NULL;", pg);
        Assert.Contains(".HasIndex(x => new { x.PeriodFrom }).HasFilter(\"[OwnerCode] IS NOT NULL\").HasDatabaseName(\"ix_report_recent\");", ef);
        Assert.Contains(".HasIndex(x => new { x.PeriodFrom }).HasFilter(\"owner_code IS NOT NULL\").HasDatabaseName(\"ix_report_recent\");", efPg);
    }

    [Fact]
    public void A_conditional_unique_declared_not_distinct_keeps_its_null_policy()
    {
        var (sql, pg, ef, efPg) = Render(ModelsOf("""
            # Namespace: x

            ## Slot
            - id: identifier @pk @generated
            - scope: string(20)?
            - code: string(20)
            - live: boolean = true

            ### Indexes
            - ux_slot: @unique(scope, code, nulls: "not_distinct", where: "live = true")
            """).Single());

        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX [ux_slot] ON [dbo].[Slot] ([Scope], [Code]) WHERE [Live] = 1;", sql);
        Assert.Contains("CREATE UNIQUE INDEX ux_slot ON public.slot (scope, code) NULLS NOT DISTINCT WHERE live = TRUE;", pg);
        Assert.Contains(".IsUnique().HasFilter(\"[Live] = 1\").HasDatabaseName(\"ux_slot\");", ef);
        Assert.Contains(".IsUnique().AreNullsDistinct(false).HasFilter(\"live = TRUE\").HasDatabaseName(\"ux_slot\");", efPg);
    }

    [Theory]
    [InlineData("status = 'a' OR status = 'b'", "OR")]
    [InlineData("NOT archived", "NOT")]
    [InlineData("status LIKE 'a%'", "LIKE")]
    [InlineData("created_at > today()", "today")]
    [InlineData("status = code", "'code'")]
    [InlineData("status NOT IN ('a')", "NOT IN")]
    [InlineData("count + 1 > 2", "'+'")]
    [InlineData("count > 1 + 2", "after the constant")]
    [InlineData("owner.name = 'x'", "own table")]
    [InlineData("nickname = 'x'", "not a field of Item")]
    [InlineData("label = 'x'", "derived field")]
    public void A_condition_no_index_can_carry_stops_the_build(string where, string named)
    {
        var models = ModelsOf($$"""
            # Namespace: x

            ## Item
            - id: identifier @pk @generated
            - status: string(20)
            - code: string(20)
            - count: integer
            - archived: boolean
            - created_at: timestamp
            - label: string @computed("status + code")

            ### Indexes
            - ix_item: @index(status, where: "{{where}}")
            """);

        var found = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD056");
        Assert.Equal(SemanticSeverity.Error, found.Severity);
        Assert.Contains(named, found.Message);
    }

    [Fact]
    public void A_full_text_index_takes_no_condition()
    {
        var models = ModelsOf("""
            # Namespace: x

            ## Doc
            - id: identifier @pk @generated
            - body: text
            - live: boolean

            ### Indexes
            - ix_doc: @index(body, fulltext: true, where: "live = true")
            """);

        var found = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD056");
        Assert.Contains("full-text", found.Message);
    }

    [Fact]
    public void An_index_condition_is_a_flat_conjunction_of_field_constant_comparisons()
    {
        Assert.Null(IndexConditions.Unindexable("a = 'x' AND b IN (1, 2) AND c IS NOT NULL AND d AND e >= -3 AND f IS NULL AND g = true"));
        Assert.NotNull(IndexConditions.Unindexable("(a = 1)"));
        Assert.NotNull(IndexConditions.Unindexable("a = NULL"));
        Assert.NotNull(IndexConditions.Unindexable("a BETWEEN 1 AND 2"));
    }
}
