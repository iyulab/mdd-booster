using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// The SQL Server target translates expressions written in M3L's portable subset (§10.7) instead of passing them through:
/// <c>true</c> is <c>1</c>, <c>date_add</c> is <c>DATEADD</c>, a boolean field's comparison is a <c>CASE</c>. An expression
/// in SQL Server's own syntax stops the build until it is declared for the platform; so does a model with no SQL Server
/// expression at all.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class PortableExpressionSqlServerBuildTests
{
    private const string Header = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

""";

    private (int Exit, string Stderr, string? View) Build(string models, string viewFile)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-portable-tsql-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "db"));
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Header + models);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [ { "type": "Sql", "dialect": "tsql", "projectPath": "../db", "emitSqlProj": false } ]
}
""");
        try
        {
            using var err = new ConsoleErrorCapture(this);
            var exit = new BuildCommand().Run(mddDir);
            var file = Directory.GetFiles(root, viewFile, SearchOption.AllDirectories).SingleOrDefault();
            return (exit, err.Text, file is null ? null : File.ReadAllText(file));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best effort */ } }
    }

    [Fact]
    public void Portable_expressions_are_translated_into_sql_server()
    {
        var (exit, stderr, view) = Build("""
## Member : Timestampable
- id: identifier @pk @generated
- age: integer
- is_active: boolean
- is_adult: boolean @computed("age >= 18")
- recent: boolean @computed("created_at > date_add(now(), -30, 'day')")
- label: string @computed("concat('#', age)")
- order_count: integer @rollup(Ticket.member_id, count, where: "is_open = true")

## Ticket : Timestampable
- id: identifier @pk @generated
- member_id: identifier @reference(Member)
- is_open: boolean
""", "MemberFullView.sql");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD037", stderr);
        Assert.NotNull(view);
        Assert.Contains("CASE WHEN [Age] >= 18 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END", view);
        Assert.Contains("DATEADD(DAY, -30, SYSDATETIMEOFFSET())", view);
        Assert.Contains("CONCAT('#', [Age])", view);
        Assert.Contains("([IsOpen] = 1)", view);
    }

    [Fact]
    public void Sql_server_syntax_in_a_portable_context_stops_the_build_and_says_where_to_move_it()
    {
        var (exit, stderr, view) = Build("""
## Member : Timestampable
- id: identifier @pk @generated
- birth: date
- age: integer @computed("DATEDIFF(YEAR, birth, GETDATE())")
- open_tickets: integer @rollup(Ticket.member_id, count, where: "[IsOpen] = 1")

## Ticket : Timestampable
- id: identifier @pk @generated
- member_id: identifier @reference(Member)
- is_open: boolean
""", "MemberFullView.sql");

        Assert.NotEqual(0, exit);
        // Both are reported in one run — fixing them one build at a time is the cost this check exists to remove.
        Assert.Contains("MDD037", stderr);
        Assert.Contains("'Member.age' @computed", stderr);
        Assert.Contains("'Member.open_tickets' @rollup where", stderr);
        Assert.Contains("platform: \"sqlserver\"", stderr);
        Assert.Null(view);
    }

    [Fact]
    public void The_same_expression_declared_for_sql_server_is_emitted_as_written()
    {
        var (exit, stderr, view) = Build("""
## Member : Timestampable
- id: identifier @pk @generated
- birth: date
- age: integer @computed_raw("DATEDIFF(YEAR, birth, GETDATE())", platform: "sqlserver")
""", "MemberFullView.sql");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD037", stderr);
        Assert.Contains("DATEDIFF(YEAR, [Birth], GETDATE())", view);
    }

    [Fact]
    public void An_expression_declared_only_for_another_platform_stops_the_build()
    {
        var (exit, stderr, _) = Build("""
## Member : Timestampable
- id: identifier @pk @generated
- birth: date
- age: integer @computed_raw("date_part('year', age(birth))::int", platform: "postgresql")
""", "MemberFullView.sql");

        Assert.NotEqual(0, exit);
        Assert.Contains("MDD036", stderr);
        Assert.Contains("'Member.age'", stderr);
    }
}
