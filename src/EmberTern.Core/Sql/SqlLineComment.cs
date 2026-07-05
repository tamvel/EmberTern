namespace EmberTern.Core.Sql;

/// <summary>Result of a line-comment toggle: the new full text plus the selection
/// that should cover the transformed line block afterwards.</summary>
public readonly record struct LineCommentResult(string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// Pure, editor-agnostic "Comment / Uncomment selected lines" using the SQL line
/// comment token <c>--</c>. Standard editor behaviour (VS / IBExpert): prefixes each
/// line in the selection (or the caret's line when nothing is selected). It toggles —
/// if every non-blank line in the block is already commented, it uncomments instead.
/// Kept in Core so it's unit-testable without AvaloniaEdit; distinct from
/// <see cref="ProcedureBodyScanner"/>'s block (/* */) body-comment which wraps the
/// whole BEGIN…END.
/// </summary>
public enum LineCommentMode { Toggle, Comment, Uncomment }

public static class SqlLineComment
{
    private const string Token = "-- ";

    /// <summary>Force-comments the block (see <see cref="Apply"/>).</summary>
    public static LineCommentResult Comment(string? text, int selectionStart, int selectionLength)
        => Apply(text, selectionStart, selectionLength, LineCommentMode.Comment);

    /// <summary>Force-uncomments the block (see <see cref="Apply"/>).</summary>
    public static LineCommentResult Uncomment(string? text, int selectionStart, int selectionLength)
        => Apply(text, selectionStart, selectionLength, LineCommentMode.Uncomment);

    /// <summary>
    /// Toggles line comments over the block of full lines touched by
    /// [<paramref name="selectionStart"/>, +<paramref name="selectionLength"/>).
    /// A zero-length selection acts on the caret's line.
    /// </summary>
    public static LineCommentResult Toggle(string? text, int selectionStart, int selectionLength)
        => Apply(text, selectionStart, selectionLength, LineCommentMode.Toggle);

    /// <summary>
    /// Comments / uncomments / toggles the block of full lines touched by
    /// [<paramref name="selectionStart"/>, +<paramref name="selectionLength"/>).
    /// A zero-length selection acts on the caret's line.
    /// </summary>
    public static LineCommentResult Apply(string? text, int selectionStart, int selectionLength, LineCommentMode mode)
    {
        text ??= string.Empty;
        if (text.Length == 0) return new LineCommentResult(string.Empty, 0, 0);

        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        if (selectionLength < 0) selectionLength = 0;

        // The block spans the line containing selectionStart through the line
        // containing the last selected character (selectionLength==0 → caret line).
        int lastCharIndex = selectionLength > 0
            ? Math.Min(selectionStart + selectionLength - 1, text.Length - 1)
            : Math.Min(selectionStart, text.Length - 1);

        int blockStart = LineStart(text, selectionStart);
        int blockEnd = LineEnd(text, lastCharIndex); // exclusive, before the EOL

        string block = text.Substring(blockStart, blockEnd - blockStart);
        // Split into lines while preserving each line's trailing content; we only
        // ever touch leading columns, so a simple '\n' split keeps '\r' on the line.
        string[] lines = block.Split('\n');

        bool anyContent = false;
        bool allCommented = true;
        foreach (var line in lines)
        {
            if (IsBlank(line)) continue;
            anyContent = true;
            if (!IsCommented(line)) { allCommented = false; break; }
        }

        bool uncomment = mode switch
        {
            LineCommentMode.Comment => false,
            LineCommentMode.Uncomment => true,
            _ => anyContent && allCommented, // Toggle
        };
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsBlank(lines[i])) continue;
            lines[i] = uncomment ? UncommentLine(lines[i]) : Token + lines[i];
        }

        string newBlock = string.Join('\n', lines);
        string newText = text[..blockStart] + newBlock + text[blockEnd..];
        return new LineCommentResult(newText, blockStart, newBlock.Length);
    }

    private static bool IsBlank(string line)
    {
        foreach (var c in line)
            if (c != '\r' && !char.IsWhiteSpace(c)) return false;
        return true;
    }

    // A line counts as commented when its first non-whitespace run is "--".
    private static bool IsCommented(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i + 1 < line.Length && line[i] == '-' && line[i + 1] == '-';
    }

    // Removes the first "--" (and a single following space, if any) after the
    // leading whitespace — the inverse of the prepended token.
    private static string UncommentLine(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        if (i + 1 >= line.Length || line[i] != '-' || line[i + 1] != '-') return line;
        int removeTo = i + 2;
        if (removeTo < line.Length && line[removeTo] == ' ') removeTo++;
        return line[..i] + line[removeTo..];
    }

    private static int LineStart(string text, int index)
    {
        if (index > text.Length) index = text.Length;
        int i = index;
        while (i > 0 && text[i - 1] != '\n') i--;
        return i;
    }

    // Position of the line's terminating '\n' (exclusive), or end-of-text for the
    // last line. The '\n' stays in the untouched tail; a preceding '\r' rides along
    // on the split line (we only ever prepend at column 0, before any content).
    private static int LineEnd(string text, int index)
    {
        int i = index;
        while (i < text.Length && text[i] != '\n') i++;
        return i;
    }
}
