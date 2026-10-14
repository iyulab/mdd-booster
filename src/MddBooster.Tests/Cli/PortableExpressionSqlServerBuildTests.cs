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
- open_count: integer @rollup(Ticket.member_id, count, where: "is_open")

## Ticket : Timestampable
- id: identifier @pk @generated
- member_id: identifier @reference(Member)
- is_open: boolean
""", "MemberFullView.sql");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD037", stderr);
        // A boolean field standing alone as the filter: SQL Server takes no bit where a condition belongs.
        Assert.Contains("(([IsOpen] = 1))", view);
        Assert.NotNull(view);
        Assert.Contains("CASE WHEN [Age] >= 18 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END", view);
        Assert.Contains("DATEADD(DAY, -30, SYSDATETIMEOFFSET())", view);
        Assert.Contains("CONCAT(N'#', [Age])", view);
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
    public void Plus_between_text_fields_stops_the_build_where_it_would_only_work_on_sql_server()
    {
        // SQL Server joins two strings with +, PostgreSQL has no + for text: the model's own types decide, so the
        // expression is refused on both platforms rather than building on one.
        var (exit, stderr, view) = Build("""
## Member : Timestampable
- id: identifier @pk @generated
- first_name: string(50)
- last_name: string(50)
- name: string @computed("first_name + last_name")
- vip_tickets: integer @rollup(Ticket.member_id, count, where: "label + $parent.first_name = 'x'")

## Ticket : Timestampable
- id: identifier @pk @generated
- member_id: identifier @reference(Member)
- label: string(20)
""", "MemberFullView.sql");

        Assert.NotEqual(0, exit);
        Assert.Contains("MDD037", stderr);
        Assert.Contains("'Member.name' @computed", stderr);
        Assert.Contains("'Member.vip_tickets' @rollup where", stderr);
        Assert.Contains("+ joining text", stderr);
        Assert.Null(view);
    }

    [Fact]
    public void A_view_column_s_rollup_filter_in_sql_server_syntax_stops_the_build_too()
    {
        // A rollup in a view is translated like a model's: its filter is asked the same question before anything is
        // written, instead of failing in the renderer with an exception that names no rule.
        var (exit, stderr, view) = Build("""
## Client : Timestampable
- id: identifier @pk @generated
- name: string(50)

## Purchase : Timestampable
- id: identifier @pk @generated
- client_id: identifier @reference(Client)
- is_open: boolean

## ClientOpen ::view
### Source
- from: Client

- client_name: string @from(Client.name)
- open_purchases: integer @rollup(Purchase.client_id, count, where: "[IsOpen] = 1")
""", "ClientOpen.sql");

        Assert.NotEqual(0, exit);
        Assert.Contains("MDD037", stderr);
        Assert.Contains("view 'ClientOpen.open_purchases' @rollup where", stderr);
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
