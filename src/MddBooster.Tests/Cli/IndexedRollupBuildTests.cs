using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>@indexed</c> asks for a rollup stored ahead of reads, and this generator produces none — on either dialect the
/// rollup is computed when its view is read, and the build says so (<c>MDD052</c>). SQL Server cannot index what a
/// FullView is made of (a rollup's correlated subquery, a lookup's outer join), so the schema binding the attribute once
/// added bought no index; it only made a view that SQL Server refuses when it reads another FullView (Msg 4513) and
/// that blocks renames. PostgreSQL used to skip the model's views altogether, which left its read type pointing at a
/// view that did not exist.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class IndexedRollupBuildTests
{
    // Project's FullView reads Customer's FullView (a lookup of a derived column) and carries an @indexed rollup —
    // the shape SQL Server refused with Msg 4513 when the rollup made the view schema-bound.
    private const string Model = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Customer : Timestampable
- id: identifier @pk
- base_hours: decimal(6,2) @not_null
- planned_hours: decimal(8,2) @computed(`base_hours + base_hours`)

## Project : Timestampable
- id: identifier @pk
- customer_id: identifier @reference(Customer)!
- customer_planned: decimal(8,2) @lookup(customer_id.planned_hours)
- total_hours: decimal(12,2) @rollup(Task.project_id, sum(hours)) @indexed

## Task : Timestampable
- id: identifier @pk
- project_id: identifier @reference(Project)!
- hours: decimal(6,2) @not_null
""";

    private sealed record Output(string Root) : IDisposable
    {
        public string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Output Build(string dialect)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-indexed-{dialect}-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        foreach (var d in new[] { "db", "entities" })
            Directory.CreateDirectory(Path.Combine(root, d));

        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "dialect": "{{dialect}}", "projectPath": "../db", "emitSqlProj": false },
    { "type": "Model", "dialect": "{{dialect}}", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" }
  ]
}
""");

        var output = new Output(root);
        Assert.Equal(0, new BuildCommand().Run(mddDir));
        return output;
    }

    [Fact]
    public void Sql_server_computes_the_rollup_on_read_in_a_view_that_is_not_schema_bound()
    {
        using var stderr = new ConsoleErrorCapture(this);
        using var o = Build("tsql");

        var view = o.Read("db", "dbo", "Views_gen", "ProjectFullView.sql");
        Assert.DoesNotContain("SCHEMABINDING", view);
        Assert.Contains("LEFT JOIN [dbo].[CustomerFullView]", view);
        Assert.Contains("(SELECT ISNULL(SUM([Hours]), 0) FROM [dbo].[Task] WHERE [ProjectId] = b.[Id]) AS [TotalHours]", view);
        Assert.Contains("MDD052", stderr.Text);
        Assert.Contains("Project.total_hours", stderr.Text);
    }

    [Fact]
    public void Postgres_renders_the_views_of_a_model_with_an_indexed_rollup()
    {
        using var stderr = new ConsoleErrorCapture(this);
        using var o = Build("postgres");

        var view = o.Read("db", "views_gen", "project_full_view.sql");
        Assert.Contains("total_hours", view);
        Assert.Contains("customer_full_view", view);
        Assert.Contains("MDD052", stderr.Text);
    }

    [Fact]
    public void The_read_type_still_says_the_model_declared_it_indexed()
    {
        using var stderr = new ConsoleErrorCapture(this);
        using var o = Build("tsql");

        Assert.Contains("Indexed = true", o.Read("entities", "Entity_gen", "ProjectExt.cs"));
    }
}
