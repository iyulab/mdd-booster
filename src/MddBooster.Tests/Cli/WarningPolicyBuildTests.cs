using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// <c>treatWarningsAsErrors</c> · <c>warningsAsErrors</c> · <c>noWarn</c> — a warning-free build
/// stated in codes rather than enforced by searching the console text.
/// </summary>
/// <remarks>
/// The fixture carries exactly one coded warning, <c>MDD018</c> (an attribute one edit away from a
/// known one), so each test can say which way that one warning went. The first test pins that it is
/// there at all: a policy test over a model with no warnings would pass for any policy.
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public class WarningPolicyBuildTests
{
    private const string Model = """
# Namespace: test.policy

## Order

- id: identifier @pk @generated
- code: string(30) @uniqe "코드"
""";

    /// <summary>The same shape, but its one coded warning comes from the m3l validator (<c>M3L-W009</c>).</summary>
    private const string ModelWithM3lWarning = """
# Namespace: test.policy

## Order

- id: identifier @pk @generated
- code: string(30) "코드"

## Line

- id: identifier @pk @generated
- order_id: identifier @reference(Order)
- >order
  - target: Order
  - from: order_id
""";

    private static string Scaffold(string policyJson, out string root, string model = Model)
    {
        root = Path.Combine(Path.GetTempPath(), $"mdd-policy-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        File.WriteAllText(Path.Combine(mddDir, "tables.m3l.md"), model);
        var policy = string.IsNullOrEmpty(policyJson) ? "" : policyJson + ",\n  ";
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"),
            "{\n  " + policy
            + "\"sources\": [\"./tables.m3l.md\"],\n"
            + "  \"targets\": [ { \"type\": \"TypeScript\", \"outputPath\": \"../ts\" } ]\n}");
        return mddDir;
    }

    private (int Exit, string Stderr, bool Generated) Build(string policyJson, string model = Model)
    {
        var mddDir = Scaffold(policyJson, out var root, model);
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

    [Fact]
    public void Without_a_policy_the_warning_is_reported_and_the_build_succeeds()
    {
        var (exit, stderr, generated) = Build("");

        Assert.Equal(0, exit);
        Assert.Contains("[MDD018]", stderr);
        Assert.True(generated);
    }

    [Fact]
    public void Treating_warnings_as_errors_stops_the_build_before_anything_is_generated()
    {
        var (exit, stderr, generated) = Build("\"treatWarningsAsErrors\": true");

        Assert.Equal(3, exit);
        Assert.Contains("[MDD018]", stderr);
        Assert.False(generated);
    }

    [Fact]
    public void Promoting_the_code_that_occurs_stops_the_build()
    {
        var (exit, _, generated) = Build("\"warningsAsErrors\": [\"MDD018\"]");

        Assert.Equal(3, exit);
        Assert.False(generated);
    }

    [Fact]
    public void Promoting_a_code_that_does_not_occur_changes_nothing()
    {
        var (exit, stderr, generated) = Build("\"warningsAsErrors\": [\"MDD017\"]");

        Assert.Equal(0, exit);
        Assert.Contains("[MDD018]", stderr);
        Assert.True(generated);
    }

    [Fact]
    public void A_suppressed_code_is_not_reported_and_is_not_promoted_by_the_blanket_switch()
    {
        var (exit, stderr, generated) = Build("\"treatWarningsAsErrors\": true, \"noWarn\": [\"MDD018\"]");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("MDD018", stderr);
        Assert.True(generated);
    }

    /// <summary>The policy covers the m3l validator's coded warnings, not only this generator's own.</summary>
    [Fact]
    public void An_m3l_warning_code_is_promoted_like_a_generator_one()
    {
        var reported = Build("", ModelWithM3lWarning);
        var promoted = Build("\"warningsAsErrors\": [\"M3L-W009\"]", ModelWithM3lWarning);

        Assert.Equal(0, reported.Exit);
        Assert.Contains("[M3L-W009]", reported.Stderr);
        Assert.Equal(3, promoted.Exit);
        Assert.False(promoted.Generated);
    }

    [Theory]
    [InlineData("\"noWarn\": [\"MDD18\"]")]
    [InlineData("\"warningsAsErrors\": [\"M3L-E002\"]")]
    [InlineData("\"warningsAsErrors\": [\"MDD018\"], \"noWarn\": [\"MDD018\"]")]
    public void A_list_entry_that_cannot_fire_as_written_is_a_configuration_error(string policyJson)
    {
        var (exit, _, generated) = Build(policyJson);

        Assert.Equal(4, exit);
        Assert.False(generated);
    }
}
