using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ergonomics;

/// <summary>
/// <b>Typing Ergonomics</b> — structural auto-indent (design §3.2). A new line inherits the indent its
/// structure implies, at <see cref="SqlFormatter.PsqlIndentUnit"/>, so typing never produces layout that
/// fights the formatter.
/// <para><b>Enter is still just a newline</b>: only its leading whitespace is smart. Nothing here moves
/// the caret by grammar (§1). Pure and synchronous — a function of (text, line start).</para>
/// <para><b>Deliberately simpler than the formatter.</b> It indents the constructs a developer writes
/// interactively — <c>begin/end</c>, <c>if/then</c>, <c>while/do</c>, <c>for … do</c>, <c>else</c> — and
/// does <b>not</b> attempt the formatter's parenthesis/column alignment (a continuation line aligned under
/// an opening paren). That layout is a function of the whole statement's width, which a line-at-a-time
/// indenter cannot know; guessing would produce indentation that fights Alt+F, which is the one thing this
/// must never do. Inside a paren continuation the previous line's indent stands and Alt+F does the rest.</para>
/// </summary>
public static class AutoIndent
{
    /// <summary>Keywords that introduce a single-statement body on the following line. The formatter puts
    /// that statement one level deeper (<c>if (x) then</c> ⏎ <c>  y = 1;</c>).</summary>
    private static readonly HashSet<string> BodyIntroducers = new(StringComparer.OrdinalIgnoreCase)
    {
        "then", "do", "else",
    };

    /// <summary>
    /// The indentation the line beginning at <paramref name="lineStart"/> should carry.
    /// </summary>
    /// <param name="text">The whole document.</param>
    /// <param name="lineStart">Offset of the first character of the line (its indentation included).</param>
    public static string ForLine(string text, int lineStart)
    {
        if (string.IsNullOrEmpty(text) || lineStart < 0 || lineStart > text.Length) return string.Empty;

        var tokens = BlockStructure.Significant(text);
        int depth = BlockStructure.DepthBefore(tokens, lineStart);

        // `then` / `do` / `else` introduce a body statement one level deeper. A BLOCK opened there is a
        // different shape — the formatter aligns `begin` with its `if` — but that is not this rule's
        // problem: KeywordPairing re-indents the block structurally when it pairs, so a `begin` typed at
        // this body indent still lands where the formatter wants it.
        var prev = BlockStructure.PreviousSignificant(tokens, lineStart);
        if (prev is { Kind: TokenKind.Keyword } && BodyIntroducers.Contains(prev.Text)) depth++;

        // A line that STARTS with a closer belongs to the level it closes, not the level it contains.
        if (StartsWithCloser(tokens, text, lineStart)) depth--;

        return BlockStructure.Repeat(SqlFormatter.PsqlIndentUnit, Math.Max(0, depth));
    }

    // Whether the first token on this line is a block closer (`end`). Only the line's OWN first token
    // counts — a token further down the document says nothing about this line's indent.
    private static bool StartsWithCloser(IReadOnlyList<SqlToken> tokens, string text, int lineStart)
    {
        foreach (var t in tokens)
        {
            if (t.End <= lineStart) continue;
            if (t.Start < lineStart) return false;          // a token spanning the line start
            if (!OnSameLine(text, lineStart, t.Start)) return false;
            return t.Kind == TokenKind.Keyword && BlockStructure.IsCloser(t.Text);
        }
        return false;
    }

    private static bool OnSameLine(string text, int lineStart, int pos)
    {
        for (int i = lineStart; i < pos && i < text.Length; i++)
        {
            if (text[i] == '\n') return false;
        }
        return true;
    }
}
