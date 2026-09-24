using System.Text.RegularExpressions;
using MddBooster.Cli.Config;

namespace MddBooster.Cli.Commands;

/// <summary>
/// Decides, per diagnostic code, whether a warning is reported, promoted to an error, or
/// suppressed — the <c>treatWarningsAsErrors</c> / <c>warningsAsErrors</c> / <c>noWarn</c> keys of
/// <c>mdd.json</c>, named and ordered after their MSBuild counterparts.
/// </summary>
/// <remarks>
/// <para>
/// Without it a consumer who wants a warning-free build has only the console to go on, and the
/// console text is this tool's to change — a check written against it passes silently the day the
/// wording moves. The codes are the stable part, so the policy is stated in codes.
/// </para>
/// <para>
/// It covers the coded warnings: this generator's semantic analysis (<c>MDDnnn</c>) and the m3l
/// parser and validator (<c>M3L-Wnnn</c>). <c>noWarn</c> wins over both promotion keys, as in
/// MSBuild. A code in both lists, or a string that is not a code at all, is a configuration error —
/// a mistyped code would otherwise be a rule that silently never fires.
/// </para>
/// </remarks>
public sealed partial class WarningPolicy
{
    private readonly bool _all;
    private readonly HashSet<string> _promote;
    private readonly HashSet<string> _suppress;

    private WarningPolicy(bool all, IEnumerable<string> promote, IEnumerable<string> suppress)
    {
        _all = all;
        _promote = new HashSet<string>(promote, StringComparer.Ordinal);
        _suppress = new HashSet<string>(suppress, StringComparer.Ordinal);
    }

    /// <summary>What happens to a warning with a given code.</summary>
    public enum Disposition
    {
        /// <summary>Reported and the build continues.</summary>
        Warn,

        /// <summary>Reported as an error; the build stops before generating anything.</summary>
        Error,

        /// <summary>Not reported.</summary>
        Suppress,
    }

    /// <summary>Builds the policy from <paramref name="config"/>, or lists why it cannot.</summary>
    public static WarningPolicy From(MddJsonConfig config, out IReadOnlyList<string> violations)
    {
        var found = new List<string>();
        var promote = config.WarningsAsErrors ?? [];
        var suppress = config.NoWarn ?? [];

        foreach (var (key, codes) in new[] { ("warningsAsErrors", promote), ("noWarn", suppress) })
        {
            foreach (var code in codes.Where(c => !CodeShape().IsMatch(c)))
                found.Add($"{key} 의 '{code}' 는 경고 코드가 아닙니다 — MDDnnn 또는 M3L-Wnnn 형식입니다.");
        }

        foreach (var code in promote.Intersect(suppress, StringComparer.Ordinal))
            found.Add($"'{code}' 가 warningsAsErrors 와 noWarn 에 함께 있습니다 — 둘 중 하나만 두세요.");

        violations = found;
        return new WarningPolicy(config.TreatWarningsAsErrors, promote, suppress);
    }

    /// <summary>The disposition of a warning carrying <paramref name="code"/>.</summary>
    public Disposition For(string code)
    {
        if (_suppress.Contains(code)) return Disposition.Suppress;
        if (_all || _promote.Contains(code)) return Disposition.Error;
        return Disposition.Warn;
    }

    [GeneratedRegex(@"^(MDD\d{3}|M3L-W\d{3})$")]
    private static partial Regex CodeShape();
}
