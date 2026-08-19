namespace MddBooster.Core.Generation;

/// <summary>
/// Resolves a path configured in <c>mdd.json</c> (<c>projectPath</c>,
/// <c>outputPath</c>, ...) against the generator's <see cref="GeneratorContext.WorkingDirectory"/>,
/// the same way every generator already does — absolute values pass through
/// unchanged, relative values are combined with the working directory.
/// </summary>
public static class ConfiguredPathResolver
{
    /// <summary>
    /// Resolves <paramref name="configuredPath"/> and warns to <see cref="Console.Error"/>
    /// when the result lands in a different git working tree than
    /// <paramref name="workingDirectory"/>.
    /// </summary>
    /// <remarks>
    /// A configured path that is absolute (or relative-with-<c>..</c>) is
    /// ordinarily fine — many setups intentionally point a target outside the
    /// directory <c>mdd build</c> was invoked from. It stops being fine the
    /// moment the *same* configuration is built from two different physical
    /// checkouts of the same repository (a linked git worktree alongside its
    /// main checkout, or two separate clones) while the configured path stays
    /// fixed: every build then writes into whichever checkout happened to be
    /// current when the path was set, silently, regardless of which checkout
    /// the developer is actually working in. This warns on exactly that
    /// crossing — target and source resolve to different git working trees —
    /// and stays silent whenever either side isn't inside a working tree at
    /// all, since there is nothing to compare.
    /// </remarks>
    public static string Resolve(string workingDirectory, string configuredPath, string paramLabel)
    {
        var resolved = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(workingDirectory, configuredPath));

        WarnIfDifferentWorkTree(workingDirectory, resolved, paramLabel);
        return resolved;
    }

    private static void WarnIfDifferentWorkTree(string workingDirectory, string resolvedPath, string paramLabel)
    {
        var invokedRoot = GitWorkTree.FindRoot(workingDirectory);
        var targetRoot = GitWorkTree.FindRoot(resolvedPath);
        if (invokedRoot is null || targetRoot is null) return;
        if (string.Equals(invokedRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) return;

        Console.Error.WriteLine(
            $"[mdd] 경고: '{paramLabel}' 이 지금 빌드를 실행한 git 작업트리({invokedRoot}) 밖의 " +
            $"다른 작업트리({targetRoot})를 가리킵니다 — 산출물이 '{resolvedPath}' 에 쓰입니다. " +
            "linked worktree 나 별도 클론에서 이 설정을 그대로 재사용하면 지금 작업 중이 아닌 " +
            "체크아웃에 쓰게 될 수 있습니다. 상대경로 사용을 고려하거나 이 값이 의도한 것인지 확인하세요.");
    }
}

/// <summary>
/// Identifies which git working tree a path belongs to.
/// </summary>
public static class GitWorkTree
{
    /// <summary>
    /// Walks upward from <paramref name="path"/> looking for a <c>.git</c>
    /// entry and returns the directory that owns it, or <c>null</c> if none is
    /// found before reaching the filesystem root.
    /// </summary>
    /// <remarks>
    /// <c>.git</c> is a directory for a normal checkout and a file (containing
    /// a <c>gitdir:</c> pointer) for a linked worktree — either form marks the
    /// root of exactly one working tree. This deliberately returns that root
    /// rather than the shared repository identity (which the linked-worktree
    /// pointer resolves to, and which every worktree of the same repository
    /// has in common) — the risk this guards against is two *physical*
    /// checkouts diverging, and a main checkout plus its own linked worktree
    /// are exactly such a pair despite sharing one repository.
    /// </remarks>
    public static string? FindRoot(string path)
    {
        var dir = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrEmpty(dir))
        {
            var gitEntry = Path.Combine(dir, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
                return dir;

            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return null;
    }
}
