using MddBooster.Cli.Commands;
using MddBooster.Cli.Config;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A key <c>mdd.json</c> does not define is reported as <c>MDD024</c> instead of being dropped without
/// a word — and, being a coded warning, it can stop a build under <c>treatWarningsAsErrors</c>, which
/// is how a CI run catches what the JSON Schema only catches in an editor.
/// </summary>
/// <remarks>
/// The case that motivated it: <c>"source"</c> for <c>"sources"</c> left the source list empty, and the
/// only thing said was that the sources were empty.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class UnknownConfigKeyBuildTests
{
    private const string Model = """
# Namespace: test.config

## Order

- id: identifier @pk @generated
- code: string(30) "코드"
""";

    private (int Exit, string Stderr, bool Generated) Build(string configJson)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-unknown-key-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), configJson);
        try
        {
            using var err = new ConsoleErrorCapture(this);
            var exit = new BuildCommand().Run(mddDir);
            return (exit, err.Text, File.Exists(Path.Combine(root, "ts", "entities_gen.ts")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private const string Target = """{ "type": "TypeScript", "outputPath": "../ts" }""";

    [Fact]
    public void A_misspelt_root_key_is_named_with_the_key_it_resembles()
    {
        var (exit, stderr, _) = Build($$"""{ "source": ["./tables.m3l.md"], "targets": [{{Target}}] }""");

        // The empty source list it leaves behind is a configuration error, and the warning above it
        // says why the list is empty.
        Assert.Equal(4, exit);
        Assert.Contains("sources 가 없습니다", stderr);
        Assert.Contains("[MDD024]", stderr);
        Assert.True(stderr.IndexOf("[MDD024]", StringComparison.Ordinal)
            < stderr.IndexOf("sources 가 없습니다", StringComparison.Ordinal));
        Assert.Contains("'source'", stderr);
        Assert.Contains("'sources' 의 오타일 수 있습니다", stderr);
    }

    [Fact]
    public void A_misspelt_target_key_is_named_with_its_position()
    {
        var (exit, stderr, generated) = Build(
            """{ "sources": ["./tables.m3l.md"], "targets": [{ "type": "TypeScript", "outputPath": "../ts", "formsOutputPth": "../forms" }] }""");

        Assert.Equal(0, exit);
        Assert.True(generated);
        Assert.Contains("[MDD024]", stderr);
        Assert.Contains("'targets[0].formsOutputPth'", stderr);
        Assert.Contains("'formsOutputPath' 의 오타일 수 있습니다", stderr);
    }

    [Fact]
    public void Treat_warnings_as_errors_stops_the_build_on_an_unknown_key()
    {
        var (exit, stderr, generated) = Build(
            $$"""{ "treatWarningsAsErrors": true, "sources": ["./tables.m3l.md"], "targets": [{{Target}}], "emitForeignKeyIndexes": true }""");

        Assert.Equal(3, exit);
        Assert.False(generated);
        Assert.Contains("[MDD024]", stderr);
    }

    [Fact]
    public void Known_keys_in_any_case_are_not_reported()
    {
        // The loader binds names without regard to case, so "Sources" is a known key, not an unknown one.
        var (exit, stderr, _) = Build($$"""{ "Sources": ["./tables.m3l.md"], "targets": [{{Target}}] }""");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD024", stderr);
    }

    [Fact]
    public void Keys_starting_with_an_underscore_are_notes_and_are_not_reported()
    {
        // JSON has no comment an editor accepts, so the rationale for a setting lives in a sibling
        // key. Under treatWarningsAsErrors a report here would stop the build.
        var (exit, stderr, generated) = Build("""
            {
              "_why": "root note",
              "treatWarningsAsErrors": true,
              "sources": ["./tables.m3l.md"],
              "targets": [{ "type": "TypeScript", "outputPath": "../ts", "_outputPath_why": "target note" }]
            }
            """);

        Assert.Equal(0, exit);
        Assert.True(generated);
        Assert.DoesNotContain("MDD024", stderr);
    }

    [Fact]
    public void The_schema_key_the_published_schema_recommends_is_not_reported()
    {
        var (exit, stderr, generated) = Build($$"""
            {
              "$schema": "https://raw.githubusercontent.com/iyulab/mdd-booster/main/schemas/mdd.schema.json",
              "treatWarningsAsErrors": true,
              "sources": ["./tables.m3l.md"],
              "targets": [{{Target}}]
            }
            """);

        Assert.Equal(0, exit);
        Assert.True(generated);
        Assert.DoesNotContain("MDD024", stderr);
    }

    [Fact]
    public void A_misspelt_key_is_still_reported_next_to_note_keys()
    {
        // The note rule must not widen into "anything unusual is fine".
        var (_, stderr, _) = Build(
            """{ "_why": "note", "sources": ["./tables.m3l.md"], "targets": [{ "type": "TypeScript", "outputPath": "../ts", "outputPth": "../x" }] }""");

        Assert.Contains("'targets[0].outputPth'", stderr);
        Assert.DoesNotContain("_why", stderr);
    }

    [Fact]
    public void Known_keys_come_from_the_configuration_types()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdd-keys-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "sources": [], "targets": [], "noWarnings": [] }""");
        try
        {
            var warning = Assert.Single(ConfigLoader.UnknownKeyWarnings(ConfigLoader.Load(path)));
            Assert.Contains("sources, targets, treatWarningsAsErrors, warningsAsErrors, noWarn", warning);
        }
        finally { File.Delete(path); }
    }
}
