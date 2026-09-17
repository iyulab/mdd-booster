using System.Globalization;
using System.Text;

namespace MddBooster.Core.Generation;

/// <summary>
/// The one place model text becomes a string literal in generated source.
/// <para>
/// Model text is free text — a description may span lines (an indented blockquote is a
/// multi-line description), and a label may contain quotes. Every target language has its own
/// rules for what may appear raw inside a literal, and a generator that escapes only the quote
/// character produces code that does not compile the moment such text arrives. Each renderer
/// used to carry its own two-character <c>Replace</c> chain; they drifted apart and none of them
/// handled a line break. Renderers call these instead.
/// </para>
/// </summary>
public static class SourceLiteral
{
    /// <summary>
    /// A C# regular string literal, quotes included: <c>"…"</c>.
    /// </summary>
    /// <remarks>
    /// A regular literal may not contain a line terminator (CR, LF, NEL, LS, PS); those and every
    /// other control character are written as escapes. Non-ASCII text (Korean labels, etc.) is
    /// kept as-is so the generated source stays readable.
    /// </remarks>
    public static string CSharpString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\0': sb.Append(@"\0"); break;
                case '\a': sb.Append(@"\a"); break;
                case '\b': sb.Append(@"\b"); break;
                case '\f': sb.Append(@"\f"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\v': sb.Append(@"\v"); break;
                default:
                    if (NeedsUnicodeEscape(c)) AppendUnicodeEscape(sb, c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// A TypeScript/JavaScript single-quoted string literal, quotes included: <c>'…'</c>.
    /// </summary>
    /// <remarks>
    /// A string literal may not contain a raw CR or LF. U+2028/U+2029 are legal since ES2019 but
    /// are escaped anyway, since older toolchains still reject them. <c>\0</c> is written as
    /// <c>\u0000</c> so a following digit can never turn it into a legacy octal escape.
    /// </remarks>
    public static string TypeScriptString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\'': sb.Append(@"\'"); break;
                case '\b': sb.Append(@"\b"); break;
                case '\f': sb.Append(@"\f"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\v': sb.Append(@"\v"); break;
                default:
                    if (NeedsUnicodeEscape(c)) AppendUnicodeEscape(sb, c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>
    /// A JSX attribute carrying a string: <c>name="…"</c>, or <c>name={'…'}</c> when the value
    /// cannot be written as a JSX attribute string.
    /// </summary>
    /// <remarks>
    /// A JSX attribute string has no escape syntax: a <c>"</c> ends it, a line break inside it is
    /// handled differently by different compilers, and <c>&amp;</c> starts an HTML entity that is
    /// decoded. Values free of those characters keep the plain, readable form; anything else goes
    /// through an expression container holding a <see cref="TypeScriptString"/> literal, which
    /// renders the same string.
    /// </remarks>
    public static string JsxStringAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        return IsPlainJsxAttributeText(value)
            ? $"{name}=\"{value}\""
            : $"{name}={{{TypeScriptString(value)}}}";
    }

    private static bool IsPlainJsxAttributeText(string value)
    {
        foreach (var c in value)
        {
            if (c is '"' or '&' || char.IsControl(c) || c is '\u2028' or '\u2029')
                return false;
        }
        return true;
    }

    private static bool NeedsUnicodeEscape(char c) =>
        char.IsControl(c) || c is '\u2028' or '\u2029';

    private static void AppendUnicodeEscape(StringBuilder sb, char c) =>
        sb.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
}
