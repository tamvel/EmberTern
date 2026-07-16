using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Constructs;

/// <summary>What may grammatically begin at the caret, for the purpose of arming a construct — the
/// coarse, deterministic classification the arming gate uses.
/// <para>A <b>set</b>, not a single verdict: one caret can legitimately be both. After a complete
/// statement followed by a blank line, the developer may equally be continuing the query
/// (<c>group by</c>) or starting a new one (<c>if</c>) — answering with only one of those is what made
/// <c>…= 1</c> + blank line + <c>if</c> refuse to arm. Widening never removes a position, so no
/// previously-arming caret can regress.</para></summary>
[Flags]
public enum ConstructPosition
{
    /// <summary>Neither a statement boundary nor a clause continuation — arm nothing (e.g. an
    /// expression/value slot where the developer is naming things; IntelliSense handles it).</summary>
    None = 0,

    /// <summary>A statement / PSQL-body-statement boundary — <see cref="ConstructCategory.Statement"/>
    /// constructs may begin here.</summary>
    StatementStart = 1 << 0,

    /// <summary>Just after something that completes a table/expression — <see cref="ConstructCategory.Clause"/>
    /// constructs may follow here.</summary>
    Clause = 1 << 1,
}

/// <summary>
/// The grammar-aware arming gate for Language Completion — deliberately <b>simple and deterministic</b>
/// (design §5, per the "95% simple beats 99% complex" directive): it classifies a caret position from the
/// <b>single previous significant token</b>, not a full AST walk. Pure and synchronous (a cheap local
/// lex of the text before the caret) so arming can never depend on timing.
/// <list type="bullet">
///   <item>Start of text, <c>;</c>, or a boundary keyword (<c>begin</c>/<c>then</c>/<c>do</c>/<c>else</c>/
///   <c>as</c>/<c>union</c>) → <see cref="ConstructPosition.StatementStart"/>.</item>
///   <item><c>(</c> → <see cref="ConstructPosition.StatementStart"/> — a subquery may open here
///   (<c>… in (select …)</c>, a CTE body, a derived table).</item>
///   <item>A token that completes a name/value (identifier, quoted identifier, number, string, parameter,
///   or <c>)</c>) → <see cref="ConstructPosition.Clause"/>.</item>
///   <item>Anything else (a non-boundary keyword like <c>select</c>/<c>from</c>/<c>where</c>, an operator,
///   a comma, a dot) → <see cref="ConstructPosition.None"/>.</item>
///   <item><b>Plus</b>: a <b>blank line</b> between the previous token and the caret ADDS
///   <see cref="ConstructPosition.StatementStart"/> to whatever the token rule decided. A developer who
///   leaves an empty line and starts typing is starting a new statement, whether or not they terminated
///   the last one with <c>;</c> — which the previous-token rule alone cannot see.</item>
/// </list>
/// The consequence set is exactly the everyday behaviour: <c>where</c> arms after <c>from CUSTOMER</c> but
/// not between <c>select</c> and <c>from</c>; <c>if</c>/<c>select</c> arm at a statement boundary but not
/// after a table name; and an unterminated statement followed by a blank line still arms the next
/// statement without losing the clause continuation.
/// <para><b>Why widen rather than narrow:</b> once Language Completion exclusively owns a construct (i.e.
/// IntelliSense no longer offers it), an under-arming gate is a dead zone — the developer gets nothing at
/// all. Over-arming merely shows a hint they can ignore. So where the coarse rule is uncertain, it arms.
/// </para>
/// </summary>
public static class ConstructContext
{
    private static readonly HashSet<string> BoundaryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "then", "do", "else", "as", "union",
    };

    /// <summary>Classifies the position immediately before <paramref name="position"/> (the offset where
    /// the construct the developer is typing begins).</summary>
    public static ConstructPosition Classify(string text, int position)
    {
        if (string.IsNullOrEmpty(text) || position <= 0) return ConstructPosition.StatementStart;
        if (position > text.Length) position = text.Length;

        // Tokens of the text up to the position. The position is always a word boundary (a construct's
        // first char), so the slice never splits a token. The last one is the previous significant token.
        var tokens = new List<SqlToken>();
        foreach (var t in SqlLexer.Tokenize(text.Substring(0, position)))
        {
            if (t.IsEndOfFile) break;
            tokens.Add(t);
        }
        if (tokens.Count == 0) return ConstructPosition.StatementStart;
        var pst = tokens[^1];

        var result = pst.Kind switch
        {
            TokenKind.Semicolon => ConstructPosition.StatementStart,
            TokenKind.Keyword when BoundaryKeywords.Contains(pst.Text) => ConstructPosition.StatementStart,
            TokenKind.LParen => ConstructPosition.StatementStart,
            TokenKind.Identifier or TokenKind.QuotedIdentifier or TokenKind.Number
                or TokenKind.StringLiteral or TokenKind.Parameter or TokenKind.RParen => ConstructPosition.Clause,
            _ => ConstructPosition.None,
        };

        // An empty line between the last token and the caret means "new thought" regardless of what that
        // token was — so it ADDS the statement start rather than replacing the token rule's verdict.
        if (HasBlankLineBetween(text, pst.End, position)) result |= ConstructPosition.StatementStart;

        // Inside an INSERT, a query may begin after the target / column list. The previous-token rule sees
        // only ')' or the table name — a Clause position — and would refuse `select`, which is a dead zone
        // now that IntelliSense no longer offers the word. No single token can carry this fact, so we look
        // back at the enclosing statement's first two tokens: bounded, synchronous, no AST (design §5's
        // "cheap local lex of the enclosing statement"). Gated on Clause so it only ever widens the exact
        // position that was wrong — a value slot inside VALUES(…) follows a comma and stays silent.
        if ((result & ConstructPosition.Clause) != 0 && BeginsWithInsertInto(text, tokens))
        {
            result |= ConstructPosition.StatementStart;
        }
        return result;
    }

    /// <summary>Whether a construct of <paramref name="category"/> may arm at <paramref name="position"/>.</summary>
    public static bool Allows(ConstructCategory category, ConstructPosition position) => category switch
    {
        ConstructCategory.Statement => (position & ConstructPosition.StatementStart) != 0,
        ConstructCategory.Clause => (position & ConstructPosition.Clause) != 0,
        _ => false,
    };

    // Whether the statement enclosing the caret opens with INSERT INTO. The statement begins after the
    // last ';' or the last blank line — the same two boundaries Classify itself treats as a statement
    // start, so "where a statement begins" has ONE meaning in this class.
    private static bool BeginsWithInsertInto(string text, List<SqlToken> tokens)
    {
        int start = 0;
        for (int i = tokens.Count - 1; i >= 1; i--)
        {
            if (tokens[i - 1].Kind == TokenKind.Semicolon
                || HasBlankLineBetween(text, tokens[i - 1].End, tokens[i].Start))
            {
                start = i;
                break;
            }
        }
        return start + 1 < tokens.Count
            && tokens[start].Kind == TokenKind.Keyword
            && tokens[start].Text.Equals("insert", StringComparison.OrdinalIgnoreCase)
            && tokens[start + 1].Text.Equals("into", StringComparison.OrdinalIgnoreCase);
    }

    // True when the gap between the previous token and the caret contains an empty line — i.e. two line
    // breaks with nothing but whitespace between them. Only whitespace can occur here in practice (the
    // previous SIGNIFICANT token was chosen), and a comment in the gap simply makes the blank line no
    // longer adjacent, which is the honest reading.
    private static bool HasBlankLineBetween(string text, int from, int to)
    {
        int breaks = 0;
        for (int i = Math.Max(0, from); i < to && i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (++breaks == 2) return true;
        }
        return false;
    }
}
