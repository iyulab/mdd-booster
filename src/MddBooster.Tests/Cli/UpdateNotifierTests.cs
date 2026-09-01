using MddBooster.Cli;

namespace MddBooster.Tests.Cli;

public class UpdateNotifierTests
{
    [Theory]
    [InlineData("0.19.0", "0.18.1", true)]
    [InlineData("0.18.1", "0.18.1", false)]
    [InlineData("0.18.0", "0.18.1", false)]
    [InlineData("0.18.1", "0.18.1+f1e0256455f3a993ce1a74919ef2e18c7d5910cb", false)]
    [InlineData("0.19.0", "0.18.1+f1e0256455f3a993ce1a74919ef2e18c7d5910cb", true)]
    public void IsNewer_compares_ignoring_build_metadata(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateNotifier.IsNewer(latest, current));

    [Fact]
    public void IsNewer_returns_false_for_unparseable_versions()
    {
        Assert.False(UpdateNotifier.IsNewer("not-a-version", "0.18.1"));
        Assert.False(UpdateNotifier.IsNewer("0.19.0", "not-a-version"));
    }

    [Fact]
    public void Cache_round_trips_through_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdd-update-cache-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Null(UpdateNotifier.ReadCache(path));

            var written = new UpdateNotifier.UpdateCache(DateTimeOffset.UtcNow, "0.19.0");
            UpdateNotifier.WriteCache(path, written);

            var read = UpdateNotifier.ReadCache(path);
            Assert.NotNull(read);
            Assert.Equal(written.LatestKnownVersion, read!.LatestKnownVersion);
            Assert.Equal(written.LastCheckedUtc, read.LastCheckedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CheckAndNotify_is_a_no_op_when_disabled()
    {
        // MDD_NO_UPDATE_CHECK is set assembly-wide (TestEnvironment's module initializer) — this
        // test pins that behavior explicitly rather than relying on it only implicitly elsewhere.
        var output = new StringWriter();
        UpdateNotifier.CheckAndNotify("0.0.1", output);
        Assert.Equal("", output.ToString());
    }
}
