using System.Text;

namespace MddBooster.Generators.Api;

/// <summary>
/// Expands a consumer-supplied permission-key template such as
/// <c>"{entitySetLower}.{verb}"</c> into a concrete key.
/// </summary>
/// <remarks>
/// The shape of an authorization key — <c>orders.read</c>, <c>Order:Read</c>,
/// <c>perm.orders.view</c> — is the consuming application's convention, not something this
/// generator knows. Inventing one here would put an app-level concept inside the generator under a
/// generic-sounding name, and every consumer whose convention differed would get constants that
/// are not merely useless but <b>wrong</b>. So the pattern is input, and this only substitutes.
/// </remarks>
public static class PermissionKeyTemplate
{
    /// <summary>The placeholders a template may use.</summary>
    public static readonly IReadOnlyList<string> Placeholders =
        ["entitySet", "entitySetLower", "verb", "Verb"];

    /// <summary>
    /// Checks a template without expanding it. Returns the unknown placeholders it contains —
    /// empty when the template is usable.
    /// </summary>
    /// <remarks>
    /// A typo'd placeholder is left in the output verbatim by naive substitution, so every key
    /// silently becomes a literal like <c>"{entityset}.read"</c> that matches no policy. Failing
    /// the build is the only outcome that cannot be mistaken for working.
    /// </remarks>
    public static IReadOnlyList<string> Validate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var unknown = new List<string>();
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            if (open < 0) break;
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;

            var name = template[(open + 1)..close];
            if (!Placeholders.Contains(name, StringComparer.Ordinal)) unknown.Add(name);
            i = close + 1;
        }

        return unknown;
    }

    /// <summary>Expands <paramref name="template"/> for one entity set and verb.</summary>
    /// <param name="template">e.g. <c>"{entitySetLower}.{verb}"</c>.</param>
    /// <param name="entitySet">The OData entity set name as emitted, e.g. <c>"Orders"</c>.</param>
    /// <param name="verb">Lowercase verb, e.g. <c>"read"</c>.</param>
    public static string Expand(string template, string entitySet, string verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySet);
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);

        var sb = new StringBuilder(template);
        sb.Replace("{entitySetLower}", entitySet.ToLowerInvariant());
        sb.Replace("{entitySet}", entitySet);
        sb.Replace("{Verb}", char.ToUpperInvariant(verb[0]) + verb[1..]);
        sb.Replace("{verb}", verb);
        return sb.ToString();
    }
}
