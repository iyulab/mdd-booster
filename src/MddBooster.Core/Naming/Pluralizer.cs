namespace MddBooster.Core.Naming;

/// <summary>
/// Shared English pluralization helper used by every generator that emits
/// user-facing names (DbSet properties, OData entity sets, GraphQL query
/// fields). Keeps naming conventions consistent across the toolchain.
/// </summary>
/// <remarks>
/// Rules handled:
/// <list type="bullet">
///   <item>Sibilant endings (<c>ss</c>, <c>sh</c>, <c>ch</c>, <c>x</c>, <c>z</c>)
///     → add <c>es</c> (e.g. <c>Address</c>→<c>Addresses</c>, <c>Box</c>→<c>Boxes</c>).</item>
///   <item>Single trailing <c>s</c> (not <c>ss</c>) → leave unchanged
///     (e.g. <c>Status</c>, <c>News</c>, <c>Series</c>).</item>
///   <item>Consonant + <c>y</c> → <c>ies</c>.</item>
///   <item>Default → append <c>s</c>.</item>
/// </list>
/// Irregular plurals and non-English languages are out of scope; the design spec
/// defers custom overrides to a future <c>mdd.json</c> option.
/// </remarks>
public static class Pluralizer
{
    public static string Pluralize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // Sibilant endings: ss, sh, ch, x, z → +es
        if (name.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            return name + "es";
        }

        // Bare trailing "s" (e.g. Status, News) — already looks plural, leave alone.
        if (name.EndsWith("s", StringComparison.Ordinal)) return name;

        // Consonant + y → ies
        if (name.Length >= 2
            && name.EndsWith("y", StringComparison.Ordinal)
            && !IsVowel(name[^2]))
        {
            return name[..^1] + "ies";
        }

        return name + "s";
    }

    private static bool IsVowel(char c) => "aeiouAEIOU".Contains(c);
}
