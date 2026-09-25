using MddBooster.Cli.Commands;
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
      "projectPath": "../src/Sample.Database",
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
            Assert.Equal("../src/Sample.Database", cfg.Targets[0].ProjectPath);
            Assert.Equal("dbo", cfg.Targets[0].Schema);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    private static string WriteConfig(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdd-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mdd.json"), json);
        return dir;
    }

    [Fact]
    public void A_syntax_error_names_the_file_line_column_and_member_and_marks_the_parser_s_words()
    {
        // The target object is never closed: the parser finds "]" where "}" belongs, line 5.
        var dir = WriteConfig("""
            {
              "sources": ["t.m3l.md"],
              "targets": [
                { "type": "Model"
              ]
            }
            """);
        try
        {
            var path = Path.Combine(dir, "mdd.json");
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(path));

            Assert.Contains(path, ex.Message);
            Assert.Contains("5행 3열", ex.Message);                 // 1-based, where the editor shows it
            Assert.Contains("항목 targets[0]", ex.Message);
            Assert.Contains("JSON 문법 오류(파서 원문:", ex.Message);
            Assert.DoesNotContain("LineNumber", ex.Message);        // the parser's own position suffix is gone
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_value_of_the_wrong_shape_is_named_in_config_terms_not_clr_types()
    {
        var dir = WriteConfig("""{ "sources": "t.m3l.md", "targets": [] }""");
        try
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Load(Path.Combine(dir, "mdd.json")));

            Assert.Contains("항목 sources", ex.Message);
            Assert.Contains("기대: 문자열 배열", ex.Message);
            Assert.DoesNotContain("System.", ex.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_malformed_config_stops_the_build_with_the_configuration_exit_code()
    {
        var dir = WriteConfig("{ \"sources\": [ }");
        try
        {
            Assert.Equal(4, new BuildCommand().Run(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
