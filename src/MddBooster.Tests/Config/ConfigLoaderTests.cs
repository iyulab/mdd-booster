using MddBooster.Cli.Config;

namespace MddBooster.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_ValidMddJson_ParsesSourcesAndTargets()
    {
        var json = """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    {
      "type": "Sql",
      "projectPath": "../src/Yesung.Database",
      "schema": "dbo"
    }
  ]
}
""";
        var tmpDir = Path.Combine(Path.GetTempPath(), $"mdd-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var cfgPath = Path.Combine(tmpDir, "mdd.json");
        File.WriteAllText(cfgPath, json);

        try
        {
            var cfg = ConfigLoader.Load(cfgPath);

            Assert.Single(cfg.Sources);
            Assert.Equal("./tables.m3l.md", cfg.Sources[0]);
            Assert.Single(cfg.Targets);
            Assert.Equal("Sql", cfg.Targets[0].Type);
            Assert.Equal("../src/Yesung.Database", cfg.Targets[0].ProjectPath);
            Assert.Equal("dbo", cfg.Targets[0].Schema);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
