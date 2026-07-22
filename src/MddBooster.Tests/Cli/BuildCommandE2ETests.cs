using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Cycle 23 — integration test for the full CLI BuildCommand with both
/// Sql and Model targets. Writes a temporary mdd.json + fixture and verifies
/// generated files land in the expected folders.
/// </summary>
public class BuildCommandE2ETests
{
    private static string FixtureContent() => """
# Namespace: test.bank

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## BankAccount : Timestampable
- id: identifier @pk @generated
- bank_name: string(50) @not_null "은행명"
- account_number: string(30) @not_null
- balance: decimal(18,2) @not_null
""";

    [Fact]
    public void Build_with_Sql_Model_and_Api_targets_produces_all_three_artifact_trees()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-build-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "Test.Database");
        var modelDir = Path.Combine(root, "src", "Test.Entities");
        var apiDir = Path.Combine(root, "src", "Test.Server");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(apiDir);

        // Seed a minimal .sqlproj so the SqlGenerator's patcher has a target.
        File.WriteAllText(Path.Combine(dbDir, "Test.sqlproj"),
            """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup>
                <Name>Test</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent());

        var json = """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/Test.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/Test.Entities", "namespace": "Test.Entities", "dbContextName": "TestDbContext" },
    { "type": "Api", "projectPath": "../src/Test.Server", "namespace": "Test.Server" }
  ]
}
""";
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), json);

        try
        {
            var exitCode = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exitCode);

            // SQL target outputs
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "BankAccount.sql")));

            // Model target outputs
            Assert.True(File.Exists(Path.Combine(modelDir, "Entity_gen", "IBankAccount.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "Entity_gen", "BankAccount.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "Entity_gen", "BankAccountExt.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "DbContext_gen", "TestDbContext.cs")));

            var dbContext = File.ReadAllText(Path.Combine(modelDir, "DbContext_gen", "TestDbContext.cs"));
            Assert.Contains("namespace Test.Entities", dbContext);
            Assert.Contains("public partial class TestDbContext", dbContext);
            Assert.Contains("DbSet<BankAccount>", dbContext);

            // Api target outputs
            var apiFile = Path.Combine(apiDir, "Api_gen", "ApiRegistration_gen.cs");
            Assert.True(File.Exists(apiFile));
            var apiSrc = File.ReadAllText(apiFile);
            Assert.Contains("namespace Test.Server", apiSrc);
            Assert.Contains("options.ODataModel.AddEntityPair<BankAccountExt, BankAccount>(\"BankAccounts\")", apiSrc);
            Assert.Contains("options.GraphQL.AddEntityPair<BankAccountExt, BankAccount>(\"bankAccounts\", \"bankAccount\")", apiSrc);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Model_target_missing_namespace_throws_with_clear_message()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-build-bad-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);

        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), FixtureContent());
        var json = """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Model", "projectPath": "../src/X", "dbContextName": "XDbContext" }
  ]
}
""";
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), json);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new BuildCommand().Run(mddDir));
            Assert.Contains("namespace", ex.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
