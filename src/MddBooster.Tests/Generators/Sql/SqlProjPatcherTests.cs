using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class SqlProjPatcherTests
{
    private static string CreateTempSqlProj(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdd-test-{Guid.NewGuid():N}.sqlproj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Patch_AddsMissingBuildIncludeEntriesForTablesGenFolder()
    {
        var original = """
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
    <Build Include="dbo\Tables_\Legacy.sql" />
  </ItemGroup>
</Project>
""";
        var projPath = CreateTempSqlProj(original);
        try
        {
            SqlProjPatcher.Patch(projPath, generatedFolderRelative: @"dbo\Tables_gen", generatedFileNames: new[] { "BankAccount.sql" });

            var updated = File.ReadAllText(projPath);

            Assert.Contains(@"<Build Include=""dbo\Tables_\Legacy.sql"" />", updated); // 기존 수동 엔트리 유지
            Assert.Contains(@"<Build Include=""dbo\Tables_gen\BankAccount.sql"" />", updated); // 신규 엔트리 추가
        }
        finally
        {
            File.Delete(projPath);
        }
    }

    [Fact]
    public void Patch_RemovesStaleTablesGenEntriesNotInList()
    {
        var original = """
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
    <Build Include="dbo\Tables_gen\Stale.sql" />
    <Build Include="dbo\Tables_gen\BankAccount.sql" />
  </ItemGroup>
</Project>
""";
        var projPath = CreateTempSqlProj(original);
        try
        {
            SqlProjPatcher.Patch(projPath, generatedFolderRelative: @"dbo\Tables_gen", generatedFileNames: new[] { "BankAccount.sql" });

            var updated = File.ReadAllText(projPath);

            Assert.DoesNotContain("Stale.sql", updated);
            Assert.Contains("BankAccount.sql", updated);
        }
        finally
        {
            File.Delete(projPath);
        }
    }

    [Fact]
    public void Patch_IsIdempotent()
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
            var first = File.ReadAllText(projPath);
            SqlProjPatcher.Patch(projPath, @"dbo\Tables_gen", new[] { "BankAccount.sql" });
            var second = File.ReadAllText(projPath);

            Assert.Equal(first, second);
        }
        finally
        {
            File.Delete(projPath);
        }
    }
}
