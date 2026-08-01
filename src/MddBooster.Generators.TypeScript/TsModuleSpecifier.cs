namespace MddBooster.Generators.TypeScript;

/// <summary>
/// Turns a pair of output directories into the relative module specifier one
/// generated file uses to import another.
/// </summary>
/// <remarks>
/// The generator writes to two independently configured directories
/// (<c>outputPath</c> and <c>formsOutputPath</c>) and then has the forms import
/// the types. Before this existed the forms carried the literal
/// <c>'../types/…'</c>, which only resolves when the consumer happens to lay the
/// two directories out as siblings named that way — a coincidence the tool never
/// checked and could not have relied on. A wrong pair produced files the
/// generator reported as written and the consumer's compiler rejected, in a
/// project this code never runs in.
/// </remarks>
internal static class TsModuleSpecifier
{
    /// <summary>
    /// The module specifier prefix that addresses <paramref name="targetDir"/>
    /// from a file sitting in <paramref name="fromDir"/> — e.g. <c>"../types"</c>,
    /// or <c>"."</c> when both are the same directory.
    /// </summary>
    /// <param name="fromDir">Absolute path of the importing file's directory.</param>
    /// <param name="targetDir">Absolute path of the imported files' directory.</param>
    /// <param name="fromOption">Name of the option that supplied <paramref name="fromDir"/>, for the error message.</param>
    /// <param name="targetOption">Name of the option that supplied <paramref name="targetDir"/>, for the error message.</param>
    /// <exception cref="InvalidOperationException">
    /// The two directories have no relative path between them (different roots or
    /// drives). Emitting an absolute path would produce a module specifier no
    /// bundler resolves, so generation stops here rather than writing files that
    /// cannot compile.
    /// </exception>
    public static string RelativeBase(
        string fromDir, string targetDir, string fromOption, string targetOption)
    {
        var relative = Path.GetRelativePath(fromDir, targetDir);

        // GetRelativePath falls back to returning the target unchanged when no
        // relative path exists (different drive/root). That is the failure case.
        if (Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"TypeScript 타깃: '{fromOption}'와 '{targetOption}'이 서로 다른 루트에 있어 " +
                $"생성 파일이 서로를 상대경로로 임포트할 수 없습니다. " +
                $"('{fromOption}' = {fromDir}, '{targetOption}' = {targetDir}) " +
                $"두 경로를 같은 프로젝트 트리 안에 두십시오.");
        }

        var specifier = relative.Replace('\\', '/');

        // A bare segment ("types") is a *package* specifier in TypeScript, not a
        // sibling directory. Only "./" makes it relative.
        if (!specifier.StartsWith('.'))
            specifier = "./" + specifier;

        return specifier;
    }
}
