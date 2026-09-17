using MddBooster.Core.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MddBooster.Tests.Generation;

/// <summary>
/// Contract of <see cref="SourceLiteral"/>: whatever model text goes in, the emitted literal is
/// valid source in its target language and denotes exactly that text.
/// </summary>
public class SourceLiteralTests
{
    public static TheoryData<string> Texts => new()
    {
        "주문번호",
        "Payor type\nLine one\nLine two",
        "CRLF\r\nline",
        "say \"hi\" and 'bye'",
        @"C:\path\to",
        "tab\there",
        "nul\0 then 1",
        "separators\u2028and\u2029",
        "next-line\u0085end",
        "a & b",
        "",
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void CSharpString_RoundTripsThroughTheCompiler(string text)
    {
        var literal = SourceLiteral.CSharpString(text);

        var tree = CSharpSyntaxTree.ParseText($"class C {{ string s = {literal}; }}");
        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var token = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token;
        Assert.Equal(text, token.ValueText);
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void TypeScriptString_HasNoRawLineTerminatorAndDecodesBack(string text)
    {
        var literal = SourceLiteral.TypeScriptString(text);

        Assert.StartsWith("'", literal);
        Assert.EndsWith("'", literal);
        Assert.DoesNotContain('\n', literal);
        Assert.DoesNotContain('\r', literal);
        Assert.DoesNotContain('\u2028', literal);
        Assert.DoesNotContain('\u2029', literal);
        Assert.Equal(text, DecodeJsSingleQuoted(literal));
    }

    [Fact]
    public void TypeScriptString_KeepsReadableTextUnchanged()
    {
        Assert.Equal("'주문번호'", SourceLiteral.TypeScriptString("주문번호"));
        Assert.Equal("'a\\nb'", SourceLiteral.TypeScriptString("a\nb"));
        Assert.Equal("'it\\'s'", SourceLiteral.TypeScriptString("it's"));
    }

    [Fact]
    public void CSharpString_KeepsReadableTextUnchanged()
    {
        Assert.Equal("\"품목명\"", SourceLiteral.CSharpString("품목명"));
        Assert.Equal("\"a\\nb\"", SourceLiteral.CSharpString("a\nb"));
    }

    [Theory]
    [InlineData("주문번호", "label=\"주문번호\"")]
    [InlineData(@"C:\dir", "label=\"C:\\dir\"")]      // no escape syntax in a JSX attribute string
    [InlineData("say \"hi\"", "label={'say \"hi\"'}")]
    [InlineData("a\nb", "label={'a\\nb'}")]
    [InlineData("R&D", "label={'R&D'}")]               // & would start an HTML entity
    public void JsxStringAttribute_UsesPlainFormOnlyWhenTheTextSurvivesIt(string text, string expected)
    {
        Assert.Equal(expected, SourceLiteral.JsxStringAttribute("label", text));
    }

    /// <summary>
    /// Minimal decoder for the escapes <see cref="SourceLiteral.TypeScriptString"/> emits, per
    /// ECMAScript SingleEscapeCharacter and UnicodeEscapeSequence. Rejects anything else, so an
    /// escape this decoder does not know is a test failure rather than a silent pass.
    /// </summary>
    private static string DecodeJsSingleQuoted(string literal)
    {
        var body = literal[1..^1];
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            Assert.NotEqual('\'', c); // an unescaped quote would end the literal early
            if (c != '\\') { sb.Append(c); continue; }

            var e = body[++i];
            switch (e)
            {
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'u':
                    sb.Append((char)Convert.ToInt32(body.Substring(i + 1, 4), 16));
                    i += 4;
                    break;
                default:
                    Assert.Fail($"Unexpected escape \\{e}");
                    break;
            }
        }
        return sb.ToString();
    }
}
