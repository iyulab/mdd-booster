using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// End-to-end validation of the view generation pipeline.
/// View derivation chain: Entity → UdView → FullView → ExtView (user-maintained)
/// </summary>
public class ViewPipelineE2ETests
{
    [Fact]
    public void Order_with_derived_produces_full_view_with_all_derived_fields()
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

            // FullView is generated; ExtView is user-maintained (not generated).
            Assert.True(File.Exists(Path.Combine(viewsDir, "OrderFullView.sql")));
            Assert.False(File.Exists(Path.Combine(viewsDir, "OrderExtView.sql")));

            // Order has no deleted_at → no UdView.
            Assert.False(File.Exists(Path.Combine(viewsDir, "OrderUdView.sql")));

            // Customer and OrderItem have no derived fields → no views.
            Assert.False(File.Exists(Path.Combine(viewsDir, "CustomerFullView.sql")));
            Assert.False(File.Exists(Path.Combine(viewsDir, "OrderItemFullView.sql")));

            // FullView contains Lookup JOIN + Rollup subqueries + Computed CTEs.
            var fullSql = File.ReadAllText(Path.Combine(viewsDir, "OrderFullView.sql"));
            Assert.Contains("CREATE VIEW [dbo].[OrderFullView]", fullSql);
            Assert.Contains("LEFT JOIN [dbo].[Customer]", fullSql);
            Assert.Contains("COUNT(*)", fullSql);
            Assert.Contains("WITH SCHEMABINDING", fullSql);
            Assert.Contains("[Subtotal] * 0.1 AS [TaxAmount]", fullSql);

            // Ext class maps to FullView (no user ExtView present).
            var orderExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "OrderExt.cs"));
            Assert.Contains("[Table(\"OrderFullView\")]", orderExt);

            // Customer has no derived → Ext maps to base table.
            var customerExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "CustomerExt.cs"));
            Assert.Contains("[Table(\"Customer\")]", customerExt);

            // .sqlproj includes FullView but not ExtView.
            var sqlProj = File.ReadAllText(Path.Combine(dbDir, "X.sqlproj"));
            Assert.Contains("dbo\\Tables_gen\\Order.sql", sqlProj);
            Assert.Contains("dbo\\Views_gen\\OrderFullView.sql", sqlProj);
            Assert.DoesNotContain("dbo\\Views_gen\\OrderExtView.sql", sqlProj);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Model_with_deleted_at_produces_ud_view_and_full_view_derives_from_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-ud-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "X.Database");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);

        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"),
            "# Namespace: X\n\n" +
            "## Timestampable ::interface\n" +
            "- created_at: timestamp = now()\n" +
            "- updated_at: timestamp = now()\n\n" +
            "---\n\n" +
            "## Product : Timestampable\n" +
            "- id: identifier @pk @generated\n" +
            "- cat_id: identifier @reference(Category) @not_null\n" +
            "- price: decimal(12,2) @not_null\n" +
            "- deleted_at: timestamp\n" +
            "- cat_name: string @lookup(cat_id.name)\n\n" +
            "## Category : Timestampable\n" +
            "- id: identifier @pk @generated\n" +
            "- name: string(50) @not_null\n");

        File.WriteAllText(Path.Combine(dbDir, "X.sqlproj"),
            "<Project Sdk=\"Microsoft.Build.Sql/0.2.5-preview\"><PropertyGroup><Name>X</Name></PropertyGroup></Project>");

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/X.Entities", "namespace": "X", "dbContextName": "XDbContext" }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            var viewsDir = Path.Combine(dbDir, "dbo", "Views_gen");

            // UdView is generated for soft-delete model.
            Assert.True(File.Exists(Path.Combine(viewsDir, "ProductUdView.sql")));
            var udSql = File.ReadAllText(Path.Combine(viewsDir, "ProductUdView.sql"));
            Assert.Contains("CREATE VIEW [dbo].[ProductUdView]", udSql);
            Assert.Contains("WHERE [DeletedAt] IS NULL", udSql);

            // FullView derives from UdView (not base table).
            Assert.True(File.Exists(Path.Combine(viewsDir, "ProductFullView.sql")));
            var fullSql = File.ReadAllText(Path.Combine(viewsDir, "ProductFullView.sql"));
            Assert.Contains("FROM [dbo].[ProductUdView] AS b", fullSql);
            Assert.Contains("LEFT JOIN [dbo].[Category]", fullSql);

            // Ext class maps to FullView (highest available layer).
            var productExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "ProductExt.cs"));
            Assert.Contains("[Table(\"ProductFullView\")]", productExt);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Custom_ext_view_file_makes_ext_class_map_to_ext_view()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-extfile-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "X.Database");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);

        // Place a hand-maintained OrderExtView.sql in dbo/Views/
        var viewsManualDir = Path.Combine(dbDir, "dbo", "Views");
        Directory.CreateDirectory(viewsManualDir);
        File.WriteAllText(Path.Combine(viewsManualDir, "OrderExtView.sql"),
            "CREATE VIEW [dbo].[OrderExtView] AS SELECT * FROM [dbo].[OrderFullView]\n");

        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-derived.m3l.md");
        File.Copy(fixture, Path.Combine(mddDir, "tables.m3l.md"));

        File.WriteAllText(Path.Combine(dbDir, "X.sqlproj"),
            "<Project Sdk=\"Microsoft.Build.Sql/0.2.5-preview\"><PropertyGroup><Name>X</Name></PropertyGroup></Project>");

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo" },
    {
      "type": "Model",
      "projectPath": "../src/X.Entities",
      "namespace": "X.Entities",
      "dbContextName": "XDbContext",
      "sqlProjectPath": "../src/X.Database"
    }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            // Ext class should map to ExtView because the user file exists.
            var orderExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "OrderExt.cs"));
            Assert.Contains("[Table(\"OrderExtView\")]", orderExt);

            // DbContext.ToView should also map to ExtView.
            var dbContext = File.ReadAllText(Path.Combine(modelDir, "DbContext_gen", "XDbContext.cs"));
            Assert.Contains("ToView(\"OrderExtView\")", dbContext);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
