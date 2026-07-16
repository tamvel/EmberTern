using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ergonomics;

/// <summary>A character delimiter pair. <c>'</c> is <b>self-closing</b> — its opener and closer are the
/// same character, which is why the typed-character rules below check type-through before pairing.</summary>
public sealed record DelimiterPair(char Open, char Close)
{
    /// <summary>True when the pair's opener and closer are the same character (<c>' … '</c>).</summary>
    public bool IsSelfClosing => Open == Close;
}

/// <summary>
/// <b>Typing Ergonomics</b> — character delimiter pairing (design §3.3): typing an opener inserts its
/// closer with the caret between, typing the closer <i>types through</i> the one already there, and
/// backspace on an empty pair removes both. One rule family, shared with the keyword pair
/// <c>begin … end</c> (<see cref="KeywordPairing"/>).
/// <para>Pure, synchronous, timing-free: a function of (text, caret, typed char). Every decision is
/// re-derived, so nothing about "which closer did we insert" is remembered — the document is the state.</para>
/// </summary>
public static class DelimiterPairing
{
    /// <summary>The pairs, per design §3.3. Firebird's <c>"</c> quoted identifiers are deliberately
    /// absent: a developer types them around a name that already exists far more often than they open an
    /// empty one, so pairing would mostly be in the way.</summary>
    public static IReadOnlyList<DelimiterPair> All { get; } = new[]
    {
        new DelimiterPair('(', ')'),
        new DelimiterPair('[', ']'),
        new DelimiterPair('\'', '\''),
    };

    /// <summary>
    /// The edit for typing <paramref name="typed"/> at <paramref name="caret"/>, or null to insert the
    /// character normally. Covers both halves of the character rules, in the order they must be checked:
    /// <b>type-through first</b> (a self-closing pair's opener and closer are indistinguishable), then
    /// pairing.
    /// </summary>
    public static PairEdit? OnCharacterTyped(string text, int caret, char typed)
    {
        if (text is null || caret < 0 || caret > text.Length) return null;

        // Type-through: the closer the developer is about to type is already sitting there (we put it
        // there). Step over it rather than doubling it. Checked first, because for '…' the opener and the
        // closer are the same keystroke.
        if (caret < text.Length && text[caret] == typed && IsCloser(typed))
        {
            return new PairEdit(caret, 0, string.Empty, 1);
        }

        var pair = PairOpenedBy(typed);
        if (pair is null) return null;

        // Never pair inside a literal or comment: a '(' in a message string is just text, and a quote
        // there is closing or escaping — in both cases an inserted closer is something to delete (Rule 0).
        if (IsInsideLiteralOrComment(text, caret)) return null;

        // Never pair immediately before something the developer is wrapping: typing '(' at "|abc" means
        // "(abc", not "()abc". A closer/whitespace/EOL after the caret is a safe place to pair.
        if (!CanPairBefore(text, caret)) return null;

        return new PairEdit(caret, 0, new string(new[] { pair.Open, pair.Close }), 1);
    }

    /// <summary>
    /// The edit for Backspace at <paramref name="caret"/> when it sits between an <b>empty</b> pair
    /// (<c>(▌)</c>), which removes both — the pair was created by one keystroke, so it dies by one.
    /// Null otherwise (ordinary Backspace).
    /// </summary>
    public static PairEdit? OnBackspace(string text, int caret)
    {
        if (text is null || caret <= 0 || caret >= text.Length) return null;
        foreach (var p in All)
        {
            if (text[caret - 1] == p.Open && text[caret] == p.Close) return new PairEdit(caret - 1, 2, string.Empty, 0);
        }
        return null;
    }

    private static DelimiterPair? PairOpenedBy(char c)
    {
        foreach (var p in All)
        {
            if (p.Open == c) return p;
        }
        return null;
    }

    private static bool IsCloser(char c)
    {
        foreach (var p in All)
        {
            if (p.Close == c) return true;
        }
        return false;
    }

    // Pair only when what follows the caret cannot be "the thing being wrapped": end of text, whitespace,
    // or a closing/separator character. Anything else (a letter, digit, quote, opener) means the developer
    // is typing in front of existing content.
    private static bool CanPairBefore(string text, int caret)
    {
        if (caret >= text.Length) return true;
        char next = text[caret];
        if (char.IsWhiteSpace(next)) return true;
        if (IsCloser(next)) return true;
        return next is ',' or ';';
    }

    // Whether the caret sits inside a string literal, a quoted identifier, or a comment — asked of the
    // LEXER, never of a hand-rolled scanner, so the '' escape and comment forms are handled once and
    // correctly (the gotcha #117 family: hand-rolled SQL scanners get exactly this wrong).
    private static bool IsInsideLiteralOrComment(string text, int caret)
    {
        foreach (var t in SqlLexer.Tokenize(text))
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind == TriviaKind.Whitespace) continue;
                if (caret > tr.Start && caret < tr.End) return true;
                // A line comment's span stops just before the newline, so the caret at its End is still at
                // the end of the COMMENT — everything typed there stays commented. A block comment is the
                // opposite: its End is past the `*/`, which is code again.
                if (caret == tr.End && tr.Kind == TriviaKind.LineComment) return true;
            }
            if (t.IsEndOfFile) break;
            if (t.Kind is not (TokenKind.StringLiteral or TokenKind.QuotedIdentifier)) continue;
            if (caret > t.Start && caret < t.End) return true;
            // A literal the developer hasn't closed yet runs to the end of the text, so the caret sitting
            // at its end is still INSIDE it — the common mid-typing state, and the one where an inserted
            // quote would be most annoying.
            if (caret == t.End && IsUnterminated(t)) return true;
        }
        return false;
    }

    // A literal is still open when it holds an ODD number of quote characters. Counting parity (rather
    // than looking at the last character) is what makes the '' escape come out right: `'it''` is three
    // quotes — an open literal whose last char is nevertheless a quote — while `'it''s'` is four.
    private static bool IsUnterminated(SqlToken t)
    {
        char q = t.Text.Length > 0 ? t.Text[0] : '\'';
        int count = 0;
        foreach (var c in t.Text)
        {
            if (c == q) count++;
        }
        return count % 2 != 0;
    }
}
