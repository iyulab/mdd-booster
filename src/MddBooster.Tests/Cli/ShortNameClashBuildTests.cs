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
