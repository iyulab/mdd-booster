using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Docs;

/// <summary>
/// Three more places where the bundled copy claims a construct reaches no output — and unlike
/// the sections around them, these produce no build report either.
/// </summary>
/// <remarks>
/// <para>
/// Every note in <c>docs/M3L.md</c> says what this generator implements, but they do not all make
/// the same kind of claim, and only one kind can be checked by behaviour. A note saying a
/// construct is read <em>in part</em> ("the attribute form only", "<c>object</c> sub-fields and
/// <c>map</c> are read") describes a boundary; asserting an absence against it would be asserting
/// something the note does not say. A note saying <em>nothing</em> is read is the checkable kind,
/// and it is checkable in exactly one way: drive the generator and look for a trace.
/// </para>
/// <para>
/// Among the notes making that second claim, the section-level ones — <c>### Metadata</c>,
/// <c>### Behaviors</c>, <c>### Version</c>, <c>### Migration</c> — say the build reports the
/// section as unconsumed, and it does. Their absence already announces itself. What is left is
/// this: constructs whose note says nothing reads them <em>and</em> nothing reports them, because
/// the accounting that covers sections does not cover attributes. Those are silent twice over, so
/// they are the ones worth a test.
/// </para>
/// <para>
/// Same shape and same limits as <see cref="UnreadAttributeAxisTests"/>: a fixture naming specific
/// constructs, not a vocabulary of everything the language defines. Carrying such a vocabulary
/// here is the cost that kept the accounting warning off this axis to begin with.
/// </para>
/// </remarks>
public class UnreportedAttributeClaimsTests
{
    /// <summary>
    /// What a target would have to emit if it read one of these constructs: the argument each one
    /// carries. Asserting on the argument rather than the attribute name is not a refinement — it
    /// is what makes the assertion possible at all.
    /// </summary>
    /// <remarks>
    /// The first spelling of this test looked for the attribute names, and <c>if</c> matched
    /// <c>Ticket.sql</c> — inside <c>identifier</c>, <c>IDENTITY</c> and every other word
    /// containing those two letters. A short name cannot be searched for; the failure was not the
    /// generator's. Arguments are chosen to occur nowhere else, so a match is evidence rather than
    /// coincidence, and they are the better subject anyway: a target could read a construct and
    /// emit only its value, which a name search would miss.
    /// </remarks>
    private static readonly (string Construct, string Payload)[] Unreported =
    [
        ("@validate", "beaconvalidatearg"),
        ("@behavior", "beaconbehaviorarg"),
        ("@if", "beaconconditionarg"),
        ("visible sub-field", "beaconvisiblearg"),
    ];

    private const string Model = """
        # Namespace: silent

        ## Ticket @behavior(beaconbehaviorarg)
        - id: identifier @primary
        - probe_beacon: string(40) @validate("beaconvalidatearg") @if("beaconconditionarg")
        - status: string(20)
          - visible: beaconvisiblearg
        - created_at: timestamp
        - updated_at: timestamp
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
    public void The_constructs_the_copy_calls_unread_and_unreported_leave_no_trace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-unreported-{Guid.NewGuid():N}");
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

            // Instrument check, for the same reason the sibling test has one: "none of these
            // appears" is also true of an empty output tree. The beacon field is named so it
            // cannot occur for an unrelated reason, and it is asserted in the spelling the
            // targets actually emit rather than the one the document declares.
            Assert.Contains(
                outputs,
                f => File.ReadAllText(f).Contains("ProbeBeacon", StringComparison.Ordinal));

            foreach (var (construct, payload) in Unreported)
            {
                var carriers = outputs
                    .Where(f => File.ReadAllText(f).Contains(payload, StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .ToList();

                Assert.True(
                    carriers.Count == 0,
                    $"the argument of {construct} reached the output ({string.Join(", ", carriers)}). " +
                    "If a target started reading it, the bundled copy's note for that section is " +
                    "now false — update it in the same commit.");
            }
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
