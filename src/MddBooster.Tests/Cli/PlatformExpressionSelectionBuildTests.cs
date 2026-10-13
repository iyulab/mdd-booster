using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A field computed only for one platform (<c>@computed_raw(…, platform: "sqlserver")</c>) and a rollup filtered only for
/// one platform (<c>where_raw:</c>) reach the SQL Server output with that platform's expression. The model's AST keeps the
/// portable expression and the platform expressions apart, and a renderer that read only the portable one emitted an
/// empty expression for a field that had none.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class PlatformExpressionSelectionBuildTests
{
    private const string Model = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Customer : Timestampable
- id: identifier @pk @generated
- name: string(50)
- year_created: integer @computed_raw("YEAR(created_at)", platform: "sqlserver")
- paid_orders: integer @rollup(Order.customer_id, count, where_raw: "status = N'paid'", platform: "sqlserver")

## Order : Timestampable
- id: identifier @pk @generated
- customer_id: identifier @reference(Customer)
- status: string(20)
""";

    private static string Build(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-platform-expr-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(Path.Combine(root, "db"));
        Directory.CreateDirectory(Path.Combine(root, "entities"));
        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "dialect": "tsql", "projectPath": "../db", "emitSqlProj": false },
    { "type": "Model", "dialect": "tsql", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" }
  ]
}
""");
        Assert.Equal(0, new BuildCommand().Run(mddDir));
        return root;
    }

    private static string ReadSingle(string root, string fileName)
    {
        var file = Assert.Single(Directory.GetFiles(root, fileName, SearchOption.AllDirectories));
        return File.ReadAllText(file);
    }

    [Fact]
    public void The_sql_server_view_and_entity_carry_the_platform_expressions()
    {
        var root = string.Empty;
        try
        {
            Build(out root);
            var view = ReadSingle(root, "CustomerFullView.sql");
            Assert.Contains("YEAR(", view);
            Assert.Contains("N'paid'", view);

            var entity = ReadSingle(root, "CustomerExt.cs");
            Assert.Contains("[Computed(\"YEAR(created_at)\")]", entity);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best effort */ } }
    }
}
