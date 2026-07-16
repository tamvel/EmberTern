using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Constructs;

/// <summary>Where the caret sits, grammatically, for the purpose of arming a construct — the coarse,
/// deterministic classification the arming gate uses.</summary>
public enum ConstructPosition
{
    /// <summary>Neither a statement boundary nor a clause continuation — arm nothing (e.g. an
    /// expression/value slot where the developer is naming things; IntelliSense handles it).</summary>
    None,

    /// <summary>A statement / PSQL-body-statement boundary — <see cref="ConstructCategory.Statement"/>
    /// constructs may begin here.</summary>
    StatementStart,

    /// <summary>Just after something that completes a table/expression — <see cref="ConstructCategory.Clause"/>
    /// constructs may follow here.</summary>
    Clause,
}

/// <summary>
/// The grammar-aware arming gate for Language Completion — deliberately <b>simple and deterministic</b>
/// (design §5, per the "95% simple beats 99% complex" directive): it classifies a caret position from the
/// <b>single previous significant token</b>, not a full AST walk. Pure and synchronous (a cheap local
/// lex of the text before the caret) so arming can never depend on timing.
/// <list type="bullet">
///   <item>Start of text, <c>;</c>, or a boundary keyword (<c>begin</c>/<c>then</c>/<c>do</c>/<c>else</c>/
///   <c>as</c>/<c>union</c>) → <see cref="ConstructPosition.StatementStart"/>.</item>
///   <item>A token that completes a name/value (identifier, quoted identifier, number, string, parameter,
///   or <c>)</c>) → <see cref="ConstructPosition.Clause"/>.</item>
///   <item>Anything else (a non-boundary keyword like <c>select</c>/<c>from</c>/<c>where</c>, an operator,
///   a comma, a dot) → <see cref="ConstructPosition.None"/>.</item>
/// </list>
/// The consequence set is exactly the everyday behaviour: <c>where</c> arms after <c>from CUSTOMER</c> but
/// not between <c>select</c> and <c>from</c>; <c>if</c>/<c>select</c> arm at a statement boundary but not
/// after a table name. Edge cases (statements split by newline without <c>;</c>, subqueries after
/// <c>(</c>, literals) fall through to "no arm" — the shown-hint + explicit-Tab contract makes that safe,
/// and the grammar can be made smarter later without changing this shape.
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

        // The previous significant token is the last non-EOF token of the text up to the position. The
        // position is always a word boundary (a construct's first char), so the slice never splits a token.
        SqlToken? pst = null;
        foreach (var t in SqlLexer.Tokenize(text.Substring(0, position)))
        {
            if (t.IsEndOfFile) break;
            pst = t;
        }
        if (pst is null) return ConstructPosition.StatementStart;

        return pst.Kind switch
        {
            TokenKind.Semicolon => ConstructPosition.StatementStart,
            TokenKind.Keyword when BoundaryKeywords.Contains(pst.Text) => ConstructPosition.StatementStart,
            TokenKind.Identifier or TokenKind.QuotedIdentifier or TokenKind.Number
                or TokenKind.StringLiteral or TokenKind.Parameter or TokenKind.RParen => ConstructPosition.Clause,
            _ => ConstructPosition.None,
        };
    }

    /// <summary>Whether a construct of <paramref name="category"/> may arm at <paramref name="position"/>.</summary>
    public static bool Allows(ConstructCategory category, ConstructPosition position) => category switch
    {
        ConstructCategory.Statement => position == ConstructPosition.StatementStart,
        ConstructCategory.Clause => position == ConstructPosition.Clause,
        _ => false,
    };
}
