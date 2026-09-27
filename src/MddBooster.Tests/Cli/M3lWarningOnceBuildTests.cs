using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A warning the m3l parser raises is reported once. The validator's result carries the parser's
/// warnings as well, so reporting both channels as they came printed each one twice — and counted
/// it twice toward a promoted-warning build failure.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class M3lWarningOnceBuildTests
{
    // `= 0` after an attribute is not read by the field-line grammar: M3L-W010.
    private const string Model = """
# Namespace: test.once

## Invoice

- id: identifier @pk @generated
- amount: decimal(12,2) @not_null = 0 "Amount"
""";

    [Fact]
    public void A_parser_warning_is_reported_once()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-once-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            """{ "sources": ["./tables.m3l.md"], "targets": [ { "type": "TypeScript", "outputPath": "../ts" } ] }""");
        try
        {
            using var err = new ConsoleErrorCapture(this);
            new BuildCommand().Run(mddDir);

            var lines = err.Text.Split('\n').Count(l => l.Contains("[M3L-W010]", StringComparison.Ordinal));
            Assert.Equal(1, lines);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
