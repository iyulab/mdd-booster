using System.Net.Http;
using System.Text.Json.Serialization;

namespace MddBooster.Cli;

/// <summary>
/// Best-effort "a newer version of mdd is available" notice. The live NuGet lookup runs at
/// most once per <see cref="CheckInterval"/> — state is cached to disk, so a normal invocation
/// pays no network cost — and is bounded by <see cref="HttpTimeout"/> so a slow or offline
/// network never delays the command it runs alongside. Every failure (network, disk, parse) is
/// swallowed silently; this must never affect the exit code or output of the command it's
/// attached to. Disabled entirely by the <c>MDD_NO_UPDATE_CHECK</c> environment variable, the
/// same opt-out shape as this CLI's existing <c>MDD_DEBUG</c>.
/// </summary>
public static class UpdateNotifier
{
    private const string PackageId = "mdd";
    private const string DisableEnvVar = "MDD_NO_UPDATE_CHECK";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);

    /// <param name="currentVersion">
    /// The running binary's version — typically <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>,
    /// which carries a <c>+{git-sha}</c> build-metadata suffix that <see cref="IsNewer"/> strips
    /// before comparing, per SemVer 2.0.0 (build metadata does not affect precedence).
    /// </param>
    /// <param name="output">Where the notice is written; defaults to <see cref="Console.Error"/>.</param>
    public static void CheckAndNotify(string currentVersion, TextWriter? output = null)
    {
        if (Environment.GetEnvironmentVariable(DisableEnvVar) is not null) return;

        try
        {
            var cachePath = GetCachePath();
            var cache = ReadCache(cachePath);
            var latest = cache?.LatestKnownVersion;

            if (cache is null || DateTimeOffset.UtcNow - cache.LastCheckedUtc > CheckInterval)
            {
                var fetched = FetchLatestVersionFromNuGet();
                if (fetched is not null)
                {
                    latest = fetched;
                    WriteCache(cachePath, new UpdateCache(DateTimeOffset.UtcNow, fetched));
                }
                // Fetch failed (offline, timeout, malformed response) — leave any existing cache
                // untouched so the next invocation retries, rather than parking a wrong answer
                // for a full CheckInterval.
            }

            if (latest is not null && IsNewer(latest, currentVersion))
            {
                var writer = output ?? Console.Error;
                writer.WriteLine();
                writer.WriteLine($"A new version of mdd is available: {StripBuildMetadata(currentVersion)} -> {latest}");
                writer.WriteLine("  dotnet tool update -g mdd");
            }
        }
        catch (Exception ex)
        {
            // Reuses this CLI's existing MDD_DEBUG escape hatch rather than adding a second,
            // narrower one — one flag surfaces every swallowed failure, not just this feature's.
            if (Environment.GetEnvironmentVariable("MDD_DEBUG") is not null)
                Console.Error.WriteLine($"[update-check] {ex}");
        }
    }

    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(StripBuildMetadata(latest), out var l) &&
        Version.TryParse(StripBuildMetadata(current), out var c) &&
        l > c;

    public static UpdateCache? ReadCache(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(path)) : null;

    public static void WriteCache(string path, UpdateCache cache)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(cache));
    }

    private static string StripBuildMetadata(string version) => version.Split('+')[0];

    private static string GetCachePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mdd", "update-check.json");

    private static string? FetchLatestVersionFromNuGet()
    {
        using var http = new HttpClient { Timeout = HttpTimeout };
        var json = http.GetStringAsync($"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json")
            .GetAwaiter().GetResult();
        var versions = JsonSerializer.Deserialize<NuGetVersionIndex>(json)?.Versions;
        if (versions is null) return null;

        string? latest = null;
        Version? latestParsed = null;
        foreach (var v in versions)
        {
            if (!Version.TryParse(v, out var parsed)) continue;
            if (latestParsed is null || parsed > latestParsed)
            {
                latestParsed = parsed;
                latest = v;
            }
        }
        return latest;
    }

    public sealed record UpdateCache(DateTimeOffset LastCheckedUtc, string LatestKnownVersion);

    private sealed class NuGetVersionIndex
    {
        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }
    }
}
