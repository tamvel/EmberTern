using System.Collections.Generic;

namespace EmberTern.Core.Sql;

/// <summary>One local declared inside a procedure body — a variable, a cursor, or
/// a local subprogram. <see cref="Detail"/> is the type text (variables), the
/// literal <c>CURSOR</c>, or <c>PROCEDURE</c>/<c>FUNCTION</c> (subprograms).
/// <see cref="Source"/> is the full declaration text (used by the cursor /
/// subprogram split-view source editors).</summary>
public sealed record ProcedureLocal(string Name, string Detail, string Source = "");

/// <summary>Top-level locals extracted from a procedure body for read-only display.</summary>
public sealed class ProcedureBodyMetadata
{
    public List<ProcedureLocal> Variables { get; } = new();
    public List<ProcedureLocal> Cursors { get; } = new();
    public List<ProcedureLocal> Subprograms { get; } = new();
}

/// <summary>
/// Lightweight scanner over a procedure body (the text after <c>AS</c>) that lists
/// the TOP-LEVEL declarations: <c>DECLARE [VARIABLE] x …</c>, <c>DECLARE x CURSOR …</c>,
/// and FB3 local subprograms <c>DECLARE PROCEDURE|FUNCTION …</c>. Read-only metadata —
/// the body editor remains the single edit surface. Pure + testable without a DB.
/// Subprogram-body skipping goes through the shared CASE-aware block scanner
/// (<see cref="SqlScanHelpers.SkipToEndOfBlock"/>), so a <c>CASE … END</c> inside a
/// subprogram no longer miscounts the nesting (gotchas #117 / #128).
/// </summary>
public static class ProcedureBodyScanner
{
    public static ProcedureBodyMetadata Scan(string? body)
    {
        var result = new ProcedureBodyMetadata();
        if (string.IsNullOrWhiteSpace(body)) return result;
        var s = body!;
        int i = 0;

        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (i >= s.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(s, ref i)) continue;
            if (!SqlScanHelpers.IsIdentifierChar(s[i])) { i++; continue; }

            int wordStart = i;
            var kw = SqlScanHelpers.ReadWord(s, ref i);
            var up = kw.ToUpperInvariant();

            if (up == "BEGIN")
            {
                // Main body starts — declaration section is over.
                break;
            }

            if (up != "DECLARE")
            {
                // Anything else at top level means we've left the declaration zone.
                break;
            }

            SqlScanHelpers.SkipTrivia(s, ref i);
            int peek = i;
            var next = SqlScanHelpers.ReadWord(s, ref peek);
            var nextUp = next.ToUpperInvariant();

            if (nextUp == "VARIABLE")
            {
                i = peek;
                SqlScanHelpers.SkipTrivia(s, ref i);
                var vn = SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty;
                var type = SqlScanHelpers.ReadUntilSemicolon(s, ref i);
                result.Variables.Add(new ProcedureLocal(vn, type.Trim(), Slice(s, wordStart, i)));
            }
            else if (nextUp == "PROCEDURE" || nextUp == "FUNCTION")
            {
                i = peek;
                SqlScanHelpers.SkipTrivia(s, ref i);
                var pn = SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty;
                SkipSubprogramBody(s, ref i);
                result.Subprograms.Add(new ProcedureLocal(pn, nextUp == "FUNCTION" ? "FUNCTION" : "PROCEDURE", Slice(s, wordStart, i)));
            }
            else
            {
                // DECLARE <name> …; — FB3 variable (no VARIABLE keyword) or a cursor.
                // 'next' is the name (peek not yet committed).
                var declName = next;
                i = peek;
                var rest = SqlScanHelpers.ReadUntilSemicolon(s, ref i);
                var src = Slice(s, wordStart, i);
                if (SqlScanHelpers.ContainsWord(rest, "CURSOR"))
                {
                    result.Cursors.Add(new ProcedureLocal(declName, "CURSOR", src));
                }
                else
                {
                    result.Variables.Add(new ProcedureLocal(declName, rest.Trim(), src));
                }
            }

            // Defensive: never loop without advancing.
            if (i <= wordStart) i = wordStart + 1;
        }

        return result;
    }

    private static string Slice(string s, int start, int end)
        => start >= 0 && end <= s.Length && end > start ? s.Substring(start, end - start).Trim() : string.Empty;

    // ─── Outer BEGIN…END body (Comment Body / Uncomment Body) ──────────────

    /// <summary>Locates the content between the OUTERMOST <c>BEGIN</c> and its
    /// matching <c>END</c> (the procedure body to disable/enable). Returns the
    /// inner-content range <c>[Start, End)</c> — the text strictly between the
    /// outer BEGIN and END — or null when there's no top-level BEGIN…END.
    /// CASE-aware (a <c>CASE … END</c> in the body doesn't close the block early),
    /// string + comment aware. Delegates to the shared
    /// <see cref="SqlScanHelpers.FindOuterBeginEndContent"/>. Pure + testable.</summary>
    public static (int Start, int End)? FindOuterBodyContent(string? text)
        => SqlScanHelpers.FindOuterBeginEndContent(text);

    /// <summary>Wraps the outer procedure body in <c>/* … */</c> (Comment Body /
    /// "disable body"). Returns the transformed full text, or null when there's no
    /// body to wrap or it's already wrapped (idempotent). Inner comments untouched.</summary>
    public static string? CommentBody(string? text)
    {
        if (text is null) return null;
        if (FindOuterBodyContent(text) is not { } range) return null;
        var content = text.Substring(range.Start, range.End - range.Start);
        var trimmed = content.Trim();
        if (trimmed.StartsWith("/*", System.StringComparison.Ordinal) && trimmed.EndsWith("*/", System.StringComparison.Ordinal))
            return null; // already wrapped — do nothing
        var wrapped = "\n/*" + content + "*/\n";
        return text.Substring(0, range.Start) + wrapped + text.Substring(range.End);
    }

    /// <summary>Removes only the OUTER <c>/* … */</c> wrapper from the procedure
    /// body (Uncomment Body / "enable body"). Returns the transformed full text, or
    /// null when the body isn't wrapped. Inner comments are preserved.</summary>
    public static string? UncommentBody(string? text)
    {
        if (text is null) return null;
        if (FindOuterBodyContent(text) is not { } range) return null;
        var content = text.Substring(range.Start, range.End - range.Start);

        int open = content.IndexOf("/*", System.StringComparison.Ordinal);
        int close = content.LastIndexOf("*/", System.StringComparison.Ordinal);
        if (open < 0 || close < 0 || close <= open) return null; // not wrapped

        // Everything before the first /* must be whitespace (it's the OUTER wrapper).
        if (content.Substring(0, open).Trim().Length != 0) return null;

        var inner = content.Substring(open + 2, close - (open + 2));
        var tail = content.Substring(close + 2);
        // Drop the newline we inserted right after /* and right before */ (best-effort).
        inner = inner.TrimStart('\r', '\n');
        if (inner.EndsWith("\n", System.StringComparison.Ordinal)) inner = inner.TrimEnd('\r', '\n');
        var rebuilt = inner + tail.TrimEnd('\r', '\n');
        return text.Substring(0, range.Start) + "\n" + rebuilt + "\n" + text.Substring(range.End);
    }

    // Consumes the subprogram's BEGIN…END body (and an optional trailing ';') via the
    // shared CASE-aware scanner — a CASE…END inside the body no longer ends it early.
    private static void SkipSubprogramBody(string s, ref int i)
        => SqlScanHelpers.SkipToEndOfBlock(s, ref i);
}
