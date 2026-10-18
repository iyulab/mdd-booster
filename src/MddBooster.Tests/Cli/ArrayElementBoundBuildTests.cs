using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A list whose elements carry a bound — <c>string(n)[]</c>, <c>decimal(p,s)[]</c> — is generated with the bound on each
/// element: the runtime's per-element validation on the entity, the element type on PostgreSQL, and no single-value
/// rule anywhere that would measure the list instead.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ArrayElementBoundBuildTests
{
    private const string Model = """
# Namespace: test.lists

## Item
- id: identifier @pk @generated
- codes: string(4)[]
- notes: string(10)?[]?
- rates: decimal(5,2)[]
- created_at: timestamp @not_null
- updated_at: timestamp @not_null
""";

    private (int Exit, string Stderr, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-array-bounds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "mdd"));
        File.WriteAllText(Path.Combine(root, "mdd", "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(root, "mdd", "mdd.json"), """
            { "sources": ["./tables.m3l.md"], "targets": [
              { "type": "Model", "projectPath": "../model", "namespace": "Lists", "dbContextName": "ListsDbContext" },
              { "type": "Sql", "projectPath": "../db", "emitSqlProj": false },
              { "type": "Sql", "dialect": "postgres", "projectPath": "../pg" },
              { "type": "TypeScript", "outputPath": "../ts" } ] }
            """);
        using var err = new ConsoleErrorCapture(this);
        var exit = Program.Main(["build", Path.Combine(root, "mdd")]);
        return (exit, err.Text, root);
    }

    private static string Read(string root, params string[] path) => File.ReadAllText(Path.Combine([root, .. path])).Replace("\r\n", "\n");

    [Fact]
    public void Each_element_carries_the_bound()
    {
        var (exit, stderr, root) = Build();
        try
        {
            Assert.True(exit == 0, stderr);

            var item = Read(root, "model", "Entity_gen", "ItemExt.cs");
            Assert.Contains("[ElementStringLength(4)]\n    public List<string> Codes", item);
            Assert.Contains("[ElementStringLength(10)]\n    public List<string?>? Notes", item);
            Assert.Contains("[ElementDecimalDigits(5, 2)]\n    public List<decimal> Rates", item);
            Assert.DoesNotContain("[StringLength(", item);
            Assert.DoesNotContain("[DecimalDigits(", item);

            var pg = Read(root, "pg", "tables_gen", "item.sql");
            Assert.Contains("codes varchar(4)[] NOT NULL", pg);
            Assert.Contains("notes varchar(10)[]", pg);
            Assert.Contains("rates numeric(5,2)[] NOT NULL", pg);

            // SQL Server keeps a list as JSON text — the element bound is the entity's rule alone there.
            Assert.Contains("[Codes] NVARCHAR(MAX)", Read(root, "db", "dbo", "Tables_gen", "Item.sql"));

            // The TypeScript schema's maxLength is a value's length; a list's elements are not it.
            var schema = Read(root, "ts", "field_schema_gen.ts");
            Assert.DoesNotContain("maxLength: 4", schema);
            Assert.DoesNotContain("maxLength: 10", schema);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
