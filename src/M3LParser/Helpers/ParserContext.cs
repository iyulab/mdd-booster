namespace M3LParser.Helpers;

/// <summary>
/// Maintains state and context during the parsing process
/// </summary>
public class ParserContext
{
    /// <summary>
    /// The lines of the content being parsed
    /// </summary>
    public string[] Lines { get; }

    /// <summary>
    /// The current line index being processed
    /// </summary>
    public int CurrentLineIndex { get; set; }

    /// <summary>
    /// The document being built during parsing
    /// </summary>
    public M3LDocument Document { get; }

    /// <summary>
    /// Initialize a new parsing context with the given content
    /// </summary>
    public ParserContext(string content)
    {
        Lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        Document = new M3LDocument();
        CurrentLineIndex = 0;
    }

    /// <summary>
    /// Get the current line being processed
    /// </summary>
    public string CurrentLine => CurrentLineIndex < Lines.Length ? Lines[CurrentLineIndex] : string.Empty;

    /// <summary>
    /// Get the trimmed version of the current line
    /// </summary>
    public string CurrentLineTrimmed => CurrentLine.Trim();

    /// <summary>
    /// Advance to the next line
    /// </summary>
    public bool NextLine()
    {
        CurrentLineIndex++;
        return CurrentLineIndex < Lines.Length;
    }

    /// <summary>
    /// Check if there are more lines to process
    /// </summary>
    public bool HasMoreLines => CurrentLineIndex < Lines.Length;

    /// <summary>
    /// Peek at the next line without advancing
    /// </summary>
    public string PeekNextLine()
    {
        return (CurrentLineIndex + 1 < Lines.Length) ? Lines[CurrentLineIndex + 1] : string.Empty;
    }

    /// <summary>
    /// Get a range of lines from the content
    /// </summary>
    public string[] GetLineRange(int startIndex, int count)
    {
        if (startIndex < 0 || startIndex >= Lines.Length)
            return Array.Empty<string>();

        count = Math.Min(count, Lines.Length - startIndex);
        string[] result = new string[count];
        Array.Copy(Lines, startIndex, result, 0, count);
        return result;
    }
}