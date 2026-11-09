using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>postgresVersion</c> says which PostgreSQL the generated SQL may rely on. Omitted it is 15 — the lowest
/// the output supports — so nothing changes; from 18 a generated primary key defaults to the time-ordered
/// <c>uuidv7()</c> instead of the random <c>gen_random_uuid()</c>, the counterpart of SQL Server's sequential GUID.
/// </summary>
/// <remarks>
/// Every case here runs the whole build and reads the file it writes, because the option crosses the config
/// reader, the dialect branch and the renderer, and a unit test of any one of them cannot see the others
/// disagree.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class PostgresVersionBuildTests
{
    private const string Model = """
# Namespace: test.pgversion

## Ticket

- id: identifier @pk @generated
- code: string(30) "code"
""";

    private (int Exit, string Stderr, string? Sql) Build(string targetJson)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-pgversion-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            $$"""{ "sources": ["./tables.m3l.md"], "targets": [{{targetJson}}] }""");
        try
        {
            using var err = new ConsoleErrorCapture(this);
            var exit = new BuildCommand().Run(mddDir);
            var file = Directory.Exists(Path.Combine(root, "db"))
                ? Directory.GetFiles(Path.Combine(root, "db"), "ticket.sql", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            return (exit, err.Text, file is null ? null : File.ReadAllText(file));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string Postgres(string extra = "") =>
        $$"""{ "type": "Sql", "projectPath": "../db", "dialect": "postgres"{{extra}} }""";

    [Theory]
    [InlineData("")]
    [InlineData(""", "postgresVersion": 15""")]
    [InlineData(""", "postgresVersion": 17""")]
    public void Below_18_the_primary_key_default_is_the_random_uuid(string extra)
    {
        var (exit, _, sql) = Build(Postgres(extra));

        Assert.Equal(0, exit);
        Assert.Contains("DEFAULT gen_random_uuid()", sql);
        Assert.DoesNotContain("uuidv7", sql);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    public void From_18_the_primary_key_default_is_the_time_ordered_uuid(int version)
    {
        var (exit, _, sql) = Build(Postgres($", \"postgresVersion\": {version}"));

        Assert.Equal(0, exit);
        Assert.Contains("id uuid NOT NULL DEFAULT uuidv7()", sql);
        Assert.DoesNotContain("gen_random_uuid", sql);
    }

    [Fact]
    public void The_version_changes_that_default_and_nothing_else()
    {
        var (_, _, plain) = Build(Postgres());
        var (_, _, v18) = Build(Postgres(", \"postgresVersion\": 18"));

        Assert.NotNull(plain);
        Assert.Equal(plain, v18!.Replace("uuidv7()", "gen_random_uuid()"));
    }

    [Fact]
    public void A_version_below_the_supported_floor_stops_the_build_and_names_the_floor()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Postgres(", \"postgresVersion\": 14")));

        Assert.Contains("postgresVersion 14", ex.Message);
        Assert.Contains("15", ex.Message);
    }

    [Fact]
    public void The_option_belongs_to_the_postgres_dialect_and_is_refused_on_t_sql()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build("""{ "type": "Sql", "projectPath": "../db", "postgresVersion": 18 }"""));

        Assert.Contains("postgresVersion", ex.Message);
        Assert.Contains("postgres", ex.Message);
    }
}
