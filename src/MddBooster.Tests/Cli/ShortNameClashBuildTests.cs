using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// MDD053 — the same short name declared in two namespaces of one build. M3L allows it (each namespace is its own
/// scope); this generator names its outputs by the short name, so the two would be one type and one table.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ShortNameClashBuildTests
{
    [Fact]
    public void One_short_name_in_two_namespaces_stops_the_build_naming_both()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-clash-{Guid.NewGuid():N}");
        var mdd = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mdd);
        File.WriteAllText(Path.Combine(mdd, "a.m3l.md"), "# Namespace: inventory\n\n## Product\n- id: identifier @pk\n");
        File.WriteAllText(Path.Combine(mdd, "b.m3l.md"), "# Namespace: sales\n\n## Product\n- id: identifier @pk\n");
        File.WriteAllText(Path.Combine(mdd, "mdd.json"), """
            { "sources": ["./a.m3l.md", "./b.m3l.md"],
              "targets": [ { "type": "Sql", "projectPath": "../db", "emitSqlProj": false } ] }
            """);
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.Equal(3, new BuildCommand().Run(mdd));
            Assert.Contains("MDD053", stderr.Text);
            Assert.Contains("model inventory.Product", stderr.Text);
            Assert.Contains("model sales.Product", stderr.Text);
            Assert.False(Directory.Exists(Path.Combine(root, "db")), "nothing is generated");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

/// <summary>
/// Names written qualified — a key's target, a type, a view source and its <c>@from</c>, through a namespace or an
/// import alias — reach the generators as the declarations the parser resolved them to.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class QualifiedReferenceBuildTests
{
    [Fact]
    public void Qualified_and_aliased_names_resolve_to_their_declarations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-qualified-{Guid.NewGuid():N}");
        var mdd = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mdd);
        File.WriteAllText(Path.Combine(mdd, "core.m3l.md"), """
            # Namespace: core

            ## Timestampable ::interface
            - created_at: timestamp @not_null
            - updated_at: timestamp @not_null

            ## Status ::enum
            - open
            - closed

            ## Asset : Timestampable
            - id: identifier @pk @generated
            - name: string(50) @not_null
            """);
        File.WriteAllText(Path.Combine(mdd, "sales.m3l.md"), """
            # Namespace: sales

            @import "./core.m3l" as c

            ## Order : core.Timestampable
            - id: identifier @pk @generated
            - asset_id: identifier @not_null @reference(c.Asset)
            - status: core.Status

            ## AssetList ::view
            ### Source
            - from: core.Asset

            - id: identifier @from(core.Asset.id)
            - name: string @from(core.Asset.name)
            """);
        File.WriteAllText(Path.Combine(mdd, "mdd.json"), """
            { "sources": ["./core.m3l.md", "./sales.m3l.md"],
              "targets": [
                { "type": "Sql", "projectPath": "../db", "emitSqlProj": false },
                { "type": "Model", "projectPath": "../model", "namespace": "Q.Model", "dbContextName": "QDbContext" } ] }
            """);
        using var stderr = new ConsoleErrorCapture(this);
        try
        {
            Assert.True(new BuildCommand().Run(mdd) == 0, stderr.Text);
            var order = File.ReadAllText(Path.Combine(root, "model", "Entity_gen", "Order.cs"));
            Assert.Contains("public Status Status", order);
            var orderTable = Directory.GetFiles(Path.Combine(root, "db"), "Order.sql", SearchOption.AllDirectories).Single();
            Assert.Contains("REFERENCES [dbo].[Asset]", File.ReadAllText(orderTable));
            Assert.True(File.Exists(Path.Combine(root, "model", "Entity_gen", "AssetList.cs")), "the view's read model");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
