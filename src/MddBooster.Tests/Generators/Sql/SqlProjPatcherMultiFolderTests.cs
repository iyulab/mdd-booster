using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// Cycle 35 — regression guards for patching multiple managed folders in
/// the same .sqlproj. SqlGenerator now writes Tables_gen AND Views_gen,
/// so two Patch calls must coexist cleanly.
/// </summary>
public class SqlProjPatcherMultiFolderTests
{
    private static string CreateTempSqlProj(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdd-multi-{Guid.NewGuid():N}.sqlproj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Two_Patch_calls_for_different_folders_keep_both_sets_of_entries()
    {
        var original = """
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
  </ItemGroup>
</Project>
""";
        var projPath = CreateTempSqlProj(original);
        try
        {
            SqlProjPatcher.Patch(projPath, @"dbo\Tables_gen", new[] { "Order.sql", "Customer.sql" });
            SqlProjPatcher.Patch(projPath, @"dbo\Views_gen", new[] { "Order_full.sql", "Order_ext.sql" });

            var updated = File.ReadAllText(projPath);

            Assert.Contains(@"<Build Include=""dbo\Tables_gen\Order.sql""", updated);
            Assert.Contains(@"<Build Include=""dbo\Tables_gen\Customer.sql""", updated);
            Assert.Contains(@"<Build Include=""dbo\Views_gen\Order_full.sql""", updated);
            Assert.Contains(@"<Build Include=""dbo\Views_gen\Order_ext.sql""", updated);
        }
        finally { File.Delete(projPath); }
    }

    [Fact]
    public void Second_patch_does_not_strip_first_folder_entries()
    {
        var original = """
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
  </ItemGroup>
</Project>
""";
        var projPath = CreateTempSqlProj(original);
        try
        {
            SqlProjPatcher.Patch(projPath, @"dbo\Tables_gen", new[] { "BankAccount.sql" });
            var afterFirst = File.ReadAllText(projPath);
            SqlProjPatcher.Patch(projPath, @"dbo\Views_gen", new[] { "Order_full.sql" });
            var afterSecond = File.ReadAllText(projPath);

            // The first-folder entry must survive the second patch
            Assert.Contains(@"Tables_gen\BankAccount.sql", afterFirst);
            Assert.Contains(@"Tables_gen\BankAccount.sql", afterSecond);
            Assert.Contains(@"Views_gen\Order_full.sql", afterSecond);
        }
        finally { File.Delete(projPath); }
    }
}
