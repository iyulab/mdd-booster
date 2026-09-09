using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Docs;

/// <summary>
/// The bundled specification copy names five attributes that no target reads
/// (<c>docs/M3L.md</c>, "Attributes no target reads"). This drives the generator with a model
/// declaring all five and asserts the claim: none of them reaches any output.
/// </summary>
/// <remarks>
/// <para>
/// The note exists because this axis is silent — an attribute the generator ignores produces no
/// build-time report, unlike a section, so it looks exactly like one that works. A note that says
/// so is only worth writing if it stays true, and a note about <em>absence</em> cannot be checked
/// by reading the document. So it is checked by behaviour: implement one of these attributes and
/// its name (or its effect) appears in the output, this test fails, and the note is what the
/// failure points at.
/// </para>
/// <para>
/// This is deliberately <em>not</em> a maintained vocabulary inside the generator. Nothing here
/// enumerates what M3L defines — the fixture names five specific attributes, and the assertion is
/// that these five leave no trace. Widening it into "every attribute the language defines" would
/// require carrying that list in this repository, which is the cost that kept the accounting
/// warning from being extended along this axis in the first place.
/// </para>
/// </remarks>
public class UnreadAttributeAxisTests
{
    /// <summary>The attributes the note lists, spelled as they appear in a document.</summary>
    private static readonly string[] Unread =
        ["min_length", "max_length", "pattern", "ledger", "materialized"];

    private const string Model = """
        # Namespace: silent

        ## Account @ledger
        - id: identifier @primary
        - probe_marker: string(20) @min_length(3) @max_length(20) @pattern("[A-Z]+")
        - created_at: timestamp
        - updated_at: timestamp

        ## AccountRollup ::view @materialized
        - id: identifier @primary
        """;

    private const string Config = """
        {
          "sources": ["./tables.m3l.md"],
          "targets": [
            { "type": "Sql", "projectPath": "../src/sql", "emitSqlProj": false },
            { "type": "Model", "projectPath": "../src/ent", "namespace": "S.Entities", "dbContextName": "SDbContext" },
            { "type": "Api", "projectPath": "../src/api", "namespace": "S.Server" }
          ]
        }
        """;

    [Fact]
    public void The_attributes_the_copy_calls_unread_leave_no_trace_in_any_output()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-unread-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), Config);

        try
        {
            Assert.Equal(0, new BuildCommand().Run(mddDir));

            var outputs = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(outputs);

            // Instrument check: the run must actually have emitted the field these attributes sit
            // on. Without it, "none of the five appears" would also hold for an empty output tree.
            // The field is named distinctively on purpose — an ordinary name like "code" occurs in
            // generated output for unrelated reasons, so it would keep this assertion true after
            // the field itself was removed. Measured: it did.
            //
            // The declared name is not what reaches the output: every target renames, and the
            // spelling that lands here is PascalCase. Asserting on the source spelling would look
            // strict and never match, which is the same empty green by another route.
            Assert.Contains(
                outputs,
                f => File.ReadAllText(f).Contains("ProbeMarker", StringComparison.Ordinal));

            foreach (var attribute in Unread)
            {
                var carriers = outputs
                    .Where(f => File.ReadAllText(f).Contains(attribute, StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .ToList();

                Assert.True(
                    carriers.Count == 0,
                    $"'@{attribute}' reached the output ({string.Join(", ", carriers)}). If that is " +
                    "deliberate, the bundled copy's \"Attributes no target reads\" table no longer " +
                    "tells the truth — update it in the same commit.");
            }
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
