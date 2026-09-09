namespace MddBooster.Tests.Docs;

/// <summary>
/// Mechanical drift guards for <c>docs/M3L.md</c>, the copy of the language specification
/// this package bundles.
/// </summary>
/// <remarks>
/// The copy is not a second home for the specification — it is the document that says
/// <em>where this generator stops</em>. Every section that the generator implements only
/// partially carries a "What this generator implements" note, and those notes are the copy's
/// reason to exist.
/// <para>
/// Two failures reached published packages before these guards existed, and neither was
/// visible to the build, the tests, or a diff stat: the notes were absent entirely, so the
/// copy read as a plain specification and quietly overstated what the generator does; and an
/// unclosed code fence inverted every heading after it, so most of the document rendered as
/// one code block on nuget.org. Both are countable, which is the whole argument for checking
/// them here rather than trusting a reader to notice.
/// </para>
/// <para>
/// These guards deliberately do not compare the copy against the upstream specification.
/// Divergence in wording is expected — the copy annotates, it does not mirror — so a
/// text-equality check would fail constantly and be silenced. What must not drift is the
/// copy's <em>character</em>, and that is what is asserted.
/// </para>
/// </remarks>
public class BundledSpecCopyDriftTests
{
    /// <summary>
    /// The label the notes were standardised on, so they are findable by searching the document
    /// itself rather than by knowing where to look.
    /// </summary>
    private const string ImplementationNoteMarker = "What this generator implements";

    /// <summary>
    /// Current number of implementation notes. This is a ratchet, not a constant: adding or
    /// removing a note is a legitimate change, and updating this number in the same commit is
    /// how that change is made deliberate rather than accidental.
    /// </summary>
    private const int ExpectedImplementationNoteCount = 14;

    private static readonly string SpecCopyPath =
        Path.Combine(AppContext.BaseDirectory, "contract", "M3L.md");

    private static string[] ReadSpecCopyLines()
    {
        Assert.True(
            File.Exists(SpecCopyPath),
            $"The bundled specification copy is missing from the test output at '{SpecCopyPath}'. " +
            "It ships via the contract link item in the test project — check that docs/M3L.md is " +
            "still tracked and that the item still points at it. A guard that stops finding its " +
            "subject asserts nothing.");

        return File.ReadAllLines(SpecCopyPath);
    }

    [Fact]
    public void Bundled_spec_copy_still_says_where_this_generator_stops()
    {
        var actual = ReadSpecCopyLines().Count(line => line.Contains(ImplementationNoteMarker, StringComparison.Ordinal));

        Assert.True(
            actual == ExpectedImplementationNoteCount,
            $"docs/M3L.md carries {actual} '{ImplementationNoteMarker}' notes, expected " +
            $"{ExpectedImplementationNoteCount}. " +
            (actual == 0
                ? "Zero means the copy has reverted to being a plain specification copy: it now " +
                  "describes the language rather than this generator, and every gap between the " +
                  "two reads as implemented. That is the exact state this document was rewritten " +
                  "to leave."
                : "If the generator started or stopped implementing a section, updating the note " +
                  "and this count belong in the same commit — that is what makes the change " +
                  "deliberate. If neither happened, a note was lost in an edit."));
    }

    [Fact]
    public void Bundled_spec_copy_has_no_unclosed_code_fence()
    {
        var lines = ReadSpecCopyLines();

        var openedAtLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal)) continue;
            openedAtLine = openedAtLine == 0 ? i + 1 : 0;
        }

        Assert.True(
            openedAtLine == 0,
            $"docs/M3L.md has a code fence opened at line {openedAtLine} that is never closed. " +
            "Everything after it renders as one code block — headings included — which is how a " +
            "previously published copy inverted its own structure without failing any build. " +
            "Close the fence, or remove the stray ``` line.");
    }
}
