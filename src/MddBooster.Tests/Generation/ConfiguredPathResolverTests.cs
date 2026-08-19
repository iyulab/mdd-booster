using MddBooster.Core.Generation;
using MddBooster.Tests.Cli;

namespace MddBooster.Tests.Generation;

/// <summary>
/// <see cref="GitWorkTree.FindRoot"/> and <see cref="ConfiguredPathResolver.Resolve"/> —
/// the guard against a configured <c>projectPath</c>/<c>outputPath</c> silently
/// writing into a different physical checkout than the one <c>mdd build</c> was
/// invoked from (linked git worktree alongside its main checkout, or two
/// separate clones sharing one <c>mdd.json</c>).
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public class ConfiguredPathResolverTests
{
    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    // GitWorkTree.FindRoot ----------------------------------------------------

    [Fact]
    public void FindRoot_returns_null_outside_any_git_tree()
    {
        var dir = CreateTempDir("mdd-gitroot-none");
        try
        {
            Assert.Null(GitWorkTree.FindRoot(dir));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void FindRoot_finds_a_normal_checkout_from_a_nested_subdirectory()
    {
        var repo = CreateTempDir("mdd-gitroot-normal");
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            var nested = Path.Combine(repo, "a", "b", "c");
            Directory.CreateDirectory(nested);

            Assert.Equal(Path.GetFullPath(repo), GitWorkTree.FindRoot(nested));
        }
        finally { Cleanup(repo); }
    }

    [Fact]
    public void FindRoot_treats_a_linked_worktree_as_its_own_root_distinct_from_the_main_checkout()
    {
        // Mirrors real `git worktree add` layout: the main checkout's `.git` is a
        // directory; the linked worktree's `.git` is a *file* pointing at
        // `<main>/.git/worktrees/<name>` via a `gitdir:` line. FindRoot must not
        // chase that pointer back to the main checkout — each is a distinct
        // physical tree and that is exactly the boundary this guard cares about.
        var main = CreateTempDir("mdd-gitroot-main");
        try
        {
            Directory.CreateDirectory(Path.Combine(main, ".git"));
            var worktreesDir = Path.Combine(main, ".git", "worktrees", "feature");
            Directory.CreateDirectory(worktreesDir);
            File.WriteAllText(Path.Combine(worktreesDir, "commondir"), "../..\n");

            var linked = CreateTempDir("mdd-gitroot-linked");
            try
            {
                File.WriteAllText(Path.Combine(linked, ".git"), $"gitdir: {worktreesDir}\n");

                var mainRoot = GitWorkTree.FindRoot(main);
                var linkedRoot = GitWorkTree.FindRoot(linked);

                Assert.Equal(Path.GetFullPath(main), mainRoot);
                Assert.Equal(Path.GetFullPath(linked), linkedRoot);
                Assert.NotEqual(mainRoot, linkedRoot);
            }
            finally { Cleanup(linked); }
        }
        finally { Cleanup(main); }
    }

    // ConfiguredPathResolver.Resolve ------------------------------------------

    [Fact]
    public void Resolve_stays_silent_when_configured_path_is_relative()
    {
        var repo = CreateTempDir("mdd-cpr-relative");
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            var configDir = Path.Combine(repo, "mdd");
            Directory.CreateDirectory(configDir);

            using var stderr = new ConsoleErrorCapture(this);
            string resolved;
            try
            {
                resolved = ConfiguredPathResolver.Resolve(configDir, "../models", "projectPath");
            }
            finally
            {
                Assert.Equal("", stderr.Text);
            }

            Assert.Equal(Path.GetFullPath(Path.Combine(repo, "models")), resolved);
        }
        finally { Cleanup(repo); }
    }

    [Fact]
    public void Resolve_stays_silent_when_neither_side_is_inside_a_git_tree()
    {
        var configDir = CreateTempDir("mdd-cpr-noconfig");
        var target = CreateTempDir("mdd-cpr-notarget");
        try
        {
            using var stderr = new ConsoleErrorCapture(this);
            ConfiguredPathResolver.Resolve(configDir, target, "projectPath");
            Assert.Equal("", stderr.Text);
        }
        finally
        {
            Cleanup(configDir);
            Cleanup(target);
        }
    }

    [Fact]
    public void Resolve_stays_silent_when_absolute_path_stays_inside_the_same_checkout()
    {
        var repo = CreateTempDir("mdd-cpr-samecheckout");
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            var configDir = Path.Combine(repo, "mdd");
            Directory.CreateDirectory(configDir);
            var target = Path.Combine(repo, "models");
            Directory.CreateDirectory(target);

            using var stderr = new ConsoleErrorCapture(this);
            ConfiguredPathResolver.Resolve(configDir, target, "projectPath");
            Assert.Equal("", stderr.Text);
        }
        finally { Cleanup(repo); }
    }

    [Fact]
    public void Resolve_warns_when_absolute_path_points_at_a_different_checkout()
    {
        // The reported failure mode: `mdd.json` pins `projectPath` to an absolute
        // location. Building from checkout A while that path lives under
        // checkout B silently writes B's generated files — regardless of which
        // checkout the developer believes they are working in.
        var checkoutA = CreateTempDir("mdd-cpr-checkoutA");
        var checkoutB = CreateTempDir("mdd-cpr-checkoutB");
        try
        {
            Directory.CreateDirectory(Path.Combine(checkoutA, ".git"));
            Directory.CreateDirectory(Path.Combine(checkoutB, ".git"));
            var configDir = Path.Combine(checkoutA, "mdd");
            Directory.CreateDirectory(configDir);
            var target = Path.Combine(checkoutB, "models");
            Directory.CreateDirectory(target);

            using var stderr = new ConsoleErrorCapture(this);
            var resolved = ConfiguredPathResolver.Resolve(configDir, target, "projectPath");

            Assert.Equal(Path.GetFullPath(target), resolved);
            Assert.Contains("projectPath", stderr.Text);
            Assert.Contains(Path.GetFullPath(checkoutA), stderr.Text);
            Assert.Contains(Path.GetFullPath(checkoutB), stderr.Text);
        }
        finally
        {
            Cleanup(checkoutA);
            Cleanup(checkoutB);
        }
    }
}
