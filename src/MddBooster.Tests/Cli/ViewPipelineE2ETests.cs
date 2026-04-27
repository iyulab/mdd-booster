using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Cycle 31 — end-to-end validation that the SqlGenerator emits FullView
/// and ExtView files alongside tables, and that the Model generator's Ext
/// class points at the correct target (base table for simple models, _ext
/// view for models with derived fields).
/// </summary>
public class ViewPipelineE2ETests
{
    [Fact]
    public void Order_with_derived_produces_full_and_ext_views_and_Ext_class_maps_to_view()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-view-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "X.Database");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);

        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-derived.m3l.md");
        File.Copy(fixture, Path.Combine(mddDir, "tables.m3l.md"));

        File.WriteAllText(Path.Combine(dbDir, "X.sqlproj"),
            """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup>
                <Name>X</Name>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/X.Entities", "namespace": "X.Entities", "dbContextName": "XDbContext" }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var viewsDir = Path.Combine(dbDir, "dbo", "Views_gen");

            // Only Order has derived fields → Only Order views exist
            Assert.True(File.Exists(Path.Combine(viewsDir, "OrderFullView.sql")));
            Assert.True(File.Exists(Path.Combine(viewsDir, "OrderExtView.sql")));
            Assert.False(File.Exists(Path.Combine(viewsDir, "CustomerFullView.sql")));
            Assert.False(File.Exists(Path.Combine(viewsDir, "OrderItemExtView.sql")));

            var orderFullSql = File.ReadAllText(Path.Combine(viewsDir, "OrderFullView.sql"));
            Assert.Contains("CREATE VIEW [dbo].[OrderFullView]", orderFullSql);
            Assert.Contains("LEFT JOIN [dbo].[Customer]", orderFullSql);

            var orderExtSql = File.ReadAllText(Path.Combine(viewsDir, "OrderExtView.sql"));
            Assert.Contains("CREATE VIEW [dbo].[OrderExtView]", orderExtSql);
            Assert.Contains("WITH SCHEMABINDING", orderExtSql);
            Assert.Contains("COUNT(*)", orderExtSql);

            // Model classes map correctly
            var orderExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "OrderExt.cs"));
            Assert.Contains("[Table(\"OrderExtView\")]", orderExt);

            // Customer has no derived → Ext maps to base table
            var customerExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "CustomerExt.cs"));
            Assert.Contains("[Table(\"Customer\")]", customerExt);

            // Verify .sqlproj includes both Tables_gen and Views_gen entries
            var sqlProj = File.ReadAllText(Path.Combine(dbDir, "X.sqlproj"));
            Assert.Contains("dbo\\Tables_gen\\Order.sql", sqlProj);
            Assert.Contains("dbo\\Views_gen\\OrderFullView.sql", sqlProj);
            Assert.Contains("dbo\\Views_gen\\OrderExtView.sql", sqlProj);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
