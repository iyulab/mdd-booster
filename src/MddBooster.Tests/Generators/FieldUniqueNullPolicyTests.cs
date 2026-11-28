using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators;

/// <summary>
/// A field-level <c>@unique(nulls: "not_distinct")</c> (M3L §3.3.4.1 · §3.3.4.3) is the same constraint a model-level
/// <c>@unique(code, nulls: "not_distinct")</c> declares: a <c>NULL</c> is one more value competing for uniqueness. SQL Server's
/// own <c>UNIQUE</c> already counts it so — no filter; PostgreSQL says <c>NULLS NOT DISTINCT</c>; the EF model says the same
/// thing to each provider. Without the policy, the field emits what it always did.
/// </summary>
public sealed class FieldUniqueNullPolicyTests
{
    private const string Model = """
        # Namespace: test

        ## Badge
        - id: identifier @pk
        - badge_code: string(20)? @unique(nulls: "not_distinct")
        - serial: string(20)? @unique
        - plate: string(20) @unique(nulls: "not_distinct")
        """;

    private static (string SqlServer, string Postgres, string EfSqlServer, string EfPostgres) Render()
    {
        var path = Path.Combine(Path.GetTempPath(), $"field-unique-nulls-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(path, Model);
        try
        {
            var models = new InterfaceResolver(new M3lLoader().LoadFile(path)).ResolveAll().ToList();
            var model = models.Single(m => m.Name == "Badge");
            var lookup = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
            return (
                TableRenderer.Render(model, schema: "dbo"),
                PostgresTableRenderer.Render(model, "public", PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name)), lookup).Sql,
                ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns")[model.Name],
                ModelMappingRenderer.Render(models, "TestDbContext", "Test.Ns", postgresNaming: true)[model.Name]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sql_server_keeps_a_not_distinct_nullable_unique_inline_and_unfiltered()
    {
        var (sql, _, _, _) = Render();

        Assert.Contains("UNIQUE NONCLUSTERED ([BadgeCode])", sql);
        Assert.DoesNotContain("WHERE [BadgeCode] IS NOT NULL", sql);
        Assert.Contains("WHERE [Serial] IS NOT NULL;", sql);       // the default policy, unchanged
        Assert.Contains("UNIQUE NONCLUSTERED ([Plate])", sql);     // NOT NULL: both policies are one constraint
    }

    [Fact]
    public void Postgres_declares_nulls_not_distinct()
    {
        var (_, pg, _, _) = Render();

        Assert.Contains("UNIQUE NULLS NOT DISTINCT (badge_code)", pg);
        Assert.Contains("UNIQUE (serial)", pg);
        Assert.DoesNotContain("NULLS NOT DISTINCT (serial)", pg);
    }

    [Fact]
    public void The_ef_model_says_the_same_to_each_provider()
    {
        var (_, _, ef, efPg) = Render();

        Assert.Contains(".HasIndex(x => x.BadgeCode).IsUnique().HasFilter(null).HasDatabaseName(", ef);
        Assert.Contains(".HasIndex(x => x.Serial).IsUnique().HasDatabaseName(", ef);
        Assert.Contains(".HasIndex(x => x.Plate).IsUnique().HasDatabaseName(", ef);   // NOT NULL — no filter to turn off
        Assert.Contains(".HasIndex(x => x.BadgeCode).IsUnique().AreNullsDistinct(false).HasDatabaseName(", efPg);
        Assert.Contains(".HasIndex(x => x.Plate).IsUnique().AreNullsDistinct(false).HasDatabaseName(", efPg);
    }
}
