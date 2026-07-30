namespace MddBooster.Tests;

/// <summary>
/// Resolves the model the acceptance gates run against.
/// </summary>
/// <remarks>
/// The gates run on a model checked in beside the other fixtures, so they
/// execute on every machine and in CI. Setting <see cref="OverrideVariable"/>
/// points them at a different model instead — useful for running the same
/// gate against a large model kept outside the repository before a release.
/// <para>
/// There is deliberately no "skip when absent" path. An earlier version
/// resolved a machine-specific path and returned quietly when it was missing,
/// which reported the gate as passing on every machine that did not have it —
/// the tests counted toward the suite total while asserting nothing. Both
/// branches here either produce a model or fail loudly, so a misconfigured
/// override surfaces as a failure rather than as silent coverage loss.
/// </para>
/// </remarks>
public static class AcceptanceModel
{
    /// <summary>Environment variable pointing at a replacement model file.</summary>
    public const string OverrideVariable = "MDDBOOSTER_ACCEPTANCE_MODEL";

    private const string FixtureName = "large-model-acceptance.m3l.md";

    /// <summary>Absolute path of the model to generate from. Never null.</summary>
    public static string Path
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
            if (string.IsNullOrWhiteSpace(overridePath)) return FixturePath;

            if (!File.Exists(overridePath))
            {
                throw new InvalidOperationException(
                    $"{OverrideVariable} is set to '{overridePath}' but no file exists there. " +
                    "Point it at a readable .m3l.md file, or unset it to run the gate against " +
                    $"the checked-in {FixtureName}.");
            }
            return overridePath;
        }
    }

    /// <summary>
    /// The checked-in model, regardless of any override. Assertions that describe
    /// the fixture itself — rather than the generator's behaviour on an arbitrary
    /// model — use this so they keep running when an override is set.
    /// </summary>
    public static string FixturePath
    {
        get
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureName);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The acceptance fixture is missing from the test output at '{path}'. " +
                    "It ships via the fixtures copy-to-output item in the test project — check that " +
                    "the file is still tracked and that the item group still matches it.");
            }
            return path;
        }
    }
}
