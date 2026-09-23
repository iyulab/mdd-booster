using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MddBooster.Tests.Docs;

/// <summary>
/// Keeps references that only make sense inside the maintainers' own workflow out of every file
/// this repository publishes.
/// </summary>
/// <remarks>
/// Everything tracked here is public: the sources and tests on the repository page, and the
/// changelog and README inside the package on nuget.org. A comment such as "see item #142" or
/// "(measured in cycle 93)" points a reader at something they cannot open, so it explains
/// nothing — the sentence has to say what was learned, not where it was recorded.
/// <para>
/// Until this guard existed the rule was held by memory alone, and it kept slipping: each cleanup
/// found fresh instances written after the previous one. What is asserted is the <em>shape</em> of
/// such a reference, never a list of specific names — naming what must not appear would put those
/// names in a public file, which is the very thing being prevented.
/// </para>
/// </remarks>
public class PublicTextInternalReferenceTests
{
    /// <summary>
    /// Each shape, with the reason it means nothing to a public reader.
    /// </summary>
    private static readonly (Regex Pattern, string Why)[] ForbiddenShapes =
    [
        (new Regex(@"\bdocket\b", RegexOptions.IgnoreCase),
            "a reference into the maintainers' private issue queue"),
        (new Regex(@"\bcycle-\d+\b", RegexOptions.IgnoreCase),
            "a work-log iteration number"),
        (new Regex(@"\bclaudedocs\b", RegexOptions.IgnoreCase),
            "a path into untracked working notes"),
        // This repository keeps no roadmap or handoff document; a reference to one is dead.
        (new Regex(@"\b(?:ROADMAP|HANDOFF)\b"),
            "a planning document this repository does not contain"),
        (new Regex(@"\b(?:ISSUE|TRIAGE|PLAN|PROPOSAL|DECISION)-[A-Za-z0-9.-]+-\d{8}"),
            "a working-note file name"),
    ];

    /// <summary>
    /// Files allowed to contain the shapes, each for a stated reason. Paths are repository-relative
    /// with forward slashes, as <c>git ls-files</c> prints them.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // Untracking the notes directory is the policy working, not a reference to it.
        [".gitignore"] = "excludes the working-notes directory from the repository",
        // The guard has to name the shapes it forbids.
        ["src/MddBooster.Tests/Docs/PublicTextInternalReferenceTests.cs"] = "this guard",
    };

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MddBooster.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// The published set is exactly what git tracks — a directory walk would also read build output
    /// and ignored local files, and would miss nothing that matters less.
    /// </summary>
    private static string[] TrackedFiles(string root)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("ls-files");
        psi.ArgumentList.Add("-z");

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"`git ls-files` exited {p.ExitCode} in '{root}': {stderr}");
        var files = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        // A guard that stops finding its subject asserts nothing.
        Assert.Contains("CHANGELOG.md", files);
        Assert.Contains("README.md", files);
        return files;
    }

    [Fact]
    public void No_published_file_refers_to_the_maintainers_private_workflow()
    {
        var root = RepositoryRoot();
        var hits = new List<string>();

        foreach (var file in TrackedFiles(root))
        {
            if (Exempt.ContainsKey(file)) continue;

            var lines = File.ReadAllLines(Path.Combine(root, file));
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (pattern, why) in ForbiddenShapes)
                {
                    var match = pattern.Match(lines[i]);
                    if (match.Success)
                        hits.Add($"{file}:{i + 1}: '{match.Value}' — {why}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Published files carry references a public reader cannot follow. Say what was learned " +
            "instead of where it was recorded (or drop the reference if the sentence stands without it):\n" +
            string.Join("\n", hits));
    }

    [Fact]
    public void Every_exemption_still_names_a_tracked_file()
    {
        var tracked = TrackedFiles(RepositoryRoot()).ToHashSet(StringComparer.Ordinal);
        var stale = Exempt.Keys.Where(path => !tracked.Contains(path)).ToList();

        Assert.True(stale.Count == 0,
            "Exemptions outlived their files — remove them so the list cannot quietly cover a file " +
            "that later takes the same path:\n" + string.Join("\n", stale));
    }
}
