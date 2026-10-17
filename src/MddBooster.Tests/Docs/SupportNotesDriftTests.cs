using System.Text.RegularExpressions;

namespace MddBooster.Tests.Docs;

/// <summary>
/// Mechanical drift guards for <c>docs/m3l-support.md</c>, the notes that say where this generator stops against
/// the M3L specification it pins.
/// </summary>
/// <remarks>
/// <para>
/// The notes used to live inside a full copy of the specification, and the copy drifted from its source: section
/// numbers moved upstream and the copy kept the old ones, so a note pointed a reader at the wrong section. Now each
/// note is keyed to the pinned specification's own numbering and the page links that version. What can drift is
/// what is checked here: the notes still exist (a count, so adding or removing one is deliberate), every one sits
/// under a numbered section key, the page names the version the project file actually pins, and it still renders.
/// </para>
/// <para>
/// Whether each key exists in the specification at that version is a cross-repository question; the workspace that
/// holds both checks it. This project does not carry a copy of the specification to check it against — that copy
/// is what drifted.
/// </para>
/// </remarks>
public class SupportNotesDriftTests
{
    /// <summary>The label the notes are standardised on, so they are findable by searching the page itself.</summary>
    private const string ImplementationNoteMarker = "What this generator implements";

    /// <summary>
    /// Current number of implementation notes. A ratchet: adding or removing a note is a legitimate change, and
    /// updating this number in the same commit is how that change is made deliberate rather than accidental.
    /// </summary>
    private const int ExpectedImplementationNoteCount = 27;

    private static readonly string NotesPath = Path.Combine(AppContext.BaseDirectory, "contract", "m3l-support.md");
    private static readonly string CoreProjectPath = Path.Combine(AppContext.BaseDirectory, "contract", "MddBooster.Core.csproj");

    private static string[] ReadNotes()
    {
        Assert.True(File.Exists(NotesPath),
            $"The M3L support notes are missing from the test output at '{NotesPath}'. They ship via the contract link " +
            "item in the test project — check that docs/m3l-support.md is still tracked and that the item still points " +
            "at it. A guard that stops finding its subject asserts nothing.");
        return File.ReadAllLines(NotesPath);
    }

    [Fact]
    public void The_support_notes_still_say_where_this_generator_stops()
    {
        var actual = ReadNotes().Count(line => line.Contains(ImplementationNoteMarker, StringComparison.Ordinal));
        Assert.True(actual == ExpectedImplementationNoteCount,
            $"docs/m3l-support.md carries {actual} '{ImplementationNoteMarker}' notes, expected {ExpectedImplementationNoteCount}. " +
            "If the generator started or stopped implementing a section, updating the note and this count belong in the " +
            "same commit — that is what makes the change deliberate. If neither happened, a note was lost in an edit.");
    }

    /// <summary>Every note sits under a section key — the pinned specification's number and title — so it points somewhere.</summary>
    [Fact]
    public void Every_note_is_keyed_to_a_numbered_section()
    {
        var lines = ReadNotes();
        var unkeyed = new List<string>();
        string? heading = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) heading = line;
            if (!line.Contains(ImplementationNoteMarker, StringComparison.Ordinal) || heading is null) continue;
            // The one list that is not a section of its own is named for what it lists.
            if (heading == "## Attributes no target reads") continue;
            if (!Regex.IsMatch(heading, @"^## §\d+(\.\d+)* \S")) unkeyed.Add(heading);
        }
        Assert.True(unkeyed.Count == 0, $"Notes under headings that name no section number: {string.Join(" · ", unkeyed)}");
    }

    /// <summary>
    /// The page names the specification version the notes were written against; the project file decides which version
    /// is actually read. Moving the pin without revisiting the notes is the drift the old copy suffered.
    /// </summary>
    [Fact]
    public void The_support_notes_name_the_specification_version_the_generator_pins()
    {
        var pin = Regex.Match(File.ReadAllText(CoreProjectPath), "Include=\"M3L\\.Native\" Version=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(pin), "The M3L.Native pin was not found in MddBooster.Core.csproj.");
        var text = string.Join("\n", ReadNotes());
        Assert.True(text.Contains($"**`M3L.Native` {pin}**", StringComparison.Ordinal) && text.Contains($"@ {pin}]", StringComparison.Ordinal),
            $"docs/m3l-support.md does not name the pinned version {pin} in both places (the pin line and the link text). " +
            "Revisit the notes against that version, then update the version, the link and its commit together.");
    }

    [Fact]
    public void The_support_notes_have_no_unclosed_code_fence()
    {
        var openedAtLine = UnclosedFence(ReadNotes());
        Assert.True(openedAtLine is null,
            $"docs/m3l-support.md has a code fence opened at line {openedAtLine} that is never closed. Everything after it " +
            "renders as one code block — headings included. Close the fence, or give an outer fence more backticks.");
    }

    /// <summary>
    /// The check follows CommonMark, not a toggle: a nested example inside a longer fence is the
    /// legitimate way to show a fenced block, and a toggle would call it unclosed.
    /// </summary>
    [Theory]
    [InlineData("````markdown|```sql|x|```|````|# Next", null)]   // four around three — closed
    [InlineData("```markdown|```sql|x|```|```|# Next", 5)]       // three around three — closes early
    [InlineData("```|x|`````", null)]                            // a longer closing run closes
    [InlineData("~~~|x|```", 1)]                                 // backticks do not close tildes
    [InlineData("```sql|x|``` trailing", 1)]                     // a closing line has nothing after the run
    [InlineData("    ```|x", null)]                              // four spaces in is code, not a fence
    [InlineData("Use ```x``` inline.", null)]                    // inline code is not a fence
    public void The_fence_check_tells_a_closed_nest_from_an_early_close(string text, int? expected)
        => Assert.Equal(expected, UnclosedFence(text.Split('|')));

    /// <summary>The 1-based line of a fence that is never closed, or null.</summary>
    private static int? UnclosedFence(IReadOnlyList<string> lines)
    {
        (char Ch, int Run, int Line)? open = null;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Fence(lines[i]) is not { } fence) continue;
            var (ch, run, hasInfo) = fence;
            if (open is null)
                open = (ch, run, i + 1);
            else if (ch == open.Value.Ch && run >= open.Value.Run && !hasInfo)
                open = null;
        }
        return open?.Line;
    }

    /// <summary>
    /// A CommonMark fence line — a run of three or more backticks or tildes, indented at most
    /// three spaces — as its character, run length, and whether anything follows the run.
    /// </summary>
    private static (char Ch, int Run, bool HasInfo)? Fence(string line)
    {
        var indent = line.Length - line.TrimStart(' ').Length;
        if (indent > 3) return null;
        var rest = line[indent..];
        if (rest.Length == 0 || rest[0] is not ('`' or '~')) return null;
        var ch = rest[0];
        var run = rest.TakeWhile(c => c == ch).Count();
        if (run < 3) return null;
        var after = rest[run..].Trim();
        // A backtick fence's info string may not contain a backtick — such a line is inline code.
        if (ch == '`' && after.Contains('`')) return null;
        return (ch, run, after.Length > 0);
    }
}
