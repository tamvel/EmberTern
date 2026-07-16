using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Constructs;

namespace EmberTern.Core.Sql.Language.Ergonomics;

/// <summary>
/// A document edit that materialises a keyword pair: replace <see cref="Length"/> characters at
/// <see cref="Start"/> with <see cref="InsertText"/>, then place the caret at
/// <see cref="Start"/> + <see cref="CaretOffset"/>. Pure data — the App applies it verbatim.
/// </summary>
public sealed record PairEdit(int Start, int Length, string InsertText, int CaretOffset);

/// <summary>
/// <b>Typing Ergonomics</b> — the keyword delimiter pair <c>begin … end</c> (design
/// <c>editor-language-expansion.md</c> §3.1). Pure, synchronous, timing-free: a function of
/// (text, caret) only.
/// <para><b>Enter is still just an indented newline.</b> The caret lands exactly where a plain
/// Enter + auto-indent would have put it; the closer simply appears on the line <i>below</i> it. Enter
/// does not jump the caret by grammar and carries no hidden meaning (§1) — this is the multi-line
/// analogue of typing <c>(</c> and getting <c>()</c> with the caret between.</para>
/// <para><b>Why Enter and not the final <c>n</c> of "begin"</b> (the trigger §3.1 left open): pairing the
/// instant the word completes would fire while typing an identifier that merely starts with those
/// letters — <c>begin_date = current_date;</c> is a perfectly ordinary PSQL statement, and the developer
/// would be deleting a generated block (Rule 0). The boundary keystroke is the safe trigger, and Enter is
/// the one developers actually type after <c>begin</c>.</para>
/// <para><b>The generated block is formatter-style</b>: the body is indented by
/// <see cref="SqlFormatter.PsqlIndentUnit"/> and the closer aligns with its opener, which is exactly what
/// <see cref="SqlFormatter"/> emits — so Alt+F never rewrites a block the editor just created, and the
/// three tools speak one formatting language. The indent is therefore a <b>Core</b> decision (like
/// casing), read from the formatter, not taken from the editor's tab settings.</para>
/// </summary>
public static class KeywordPairing
{
    /// <summary>
    /// The edit for pressing Enter at <paramref name="caret"/>, or null when this is an ordinary
    /// newline (the overwhelmingly common case — the App then lets the editor handle Enter normally).
    /// </summary>
    /// <param name="newLine">The document's newline sequence — the one thing only the App knows.</param>
    public static PairEdit? OnNewLine(string text, int caret, string newLine)
    {
        var indentUnit = SqlFormatter.PsqlIndentUnit;
        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length) return null;

        // Never split a line: with code after the caret the closer would land in front of it.
        if (!RestOfLineIsBlank(text, caret)) return null;

        var tokens = BlockStructure.Significant(text);
        var opener = BlockStructure.PreviousSignificant(tokens, caret);
        if (opener is null || opener.Kind != TokenKind.Keyword) return null;

        var pair = BlockStructure.PairOpenedBy(opener.Text);
        if (pair is null) return null;

        // Grammar: only where a statement may begin — so a quoted "BEGIN" column, or the word in an
        // expression, never pairs. Reuses the ONE definition of "statement position" the editor already
        // has, rather than a second copy of the rule.
        if (!ConstructContext.Allows(ConstructCategory.Statement, ConstructContext.Classify(text, opener.Start)))
        {
            return null;
        }

        // Only pair when the document actually lacks a closer. Without this, pressing Enter after the
        // `begin` of an already-complete `begin … end` would bolt on a SECOND `end`.
        if (!BlockStructure.HasUnclosedOpener(tokens)) return null;

        // The block's indent is STRUCTURAL — one level per enclosing unclosed block — not "wherever the
        // opener happens to sit". That is what the formatter does: a block under `then` aligns with its
        // `if`, not with the `then`-body's statement indent, so deriving it from the nesting is the only
        // way the generated block matches Alt+F once auto-indent starts placing the caret.
        var indent = BlockStructure.Repeat(indentUnit, BlockStructure.DepthBefore(tokens, opener.Start));
        // The closer matches how the opener was typed (BEGIN → END), via the same rule identifier and
        // construct completion use — the App never re-implements casing.
        var closer = CaseMatcher.Match(opener.Text, pair.Closer, SqlCaseStyleDetector.Detect(text));

        // When the opener is the first thing on its line, re-render the line at the structural indent, so
        // the whole block is exactly formatter output. Otherwise (`… as begin`) leave the opener where it
        // is and only append the block — never reflow code the developer wrote around it.
        int lineStart = BlockStructure.LineStartOf(text, opener.Start);
        bool openerStartsLine = IsBlank(text, lineStart, opener.Start);
        var head = openerStartsLine ? indent + opener.Text : string.Empty;
        int start = openerStartsLine ? lineStart : opener.End;

        var insert = head + newLine + indent + indentUnit + newLine + indent + closer;
        // Replaces any whitespace the developer left between the opener and the caret, so `begin   ` +
        // Enter produces the same clean block as `begin` + Enter.
        return new PairEdit(
            start,
            caret - start,
            insert,
            head.Length + newLine.Length + indent.Length + indentUnit.Length);
    }

    private static bool IsBlank(string text, int from, int to)
    {
        for (int i = from; i < to && i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i])) return false;
        }
        return true;
    }

    private static bool RestOfLineIsBlank(string text, int caret)
    {
        for (int i = caret; i < text.Length; i++)
        {
            if (text[i] == '\n') return true;
            if (!char.IsWhiteSpace(text[i])) return false;
        }
        return true;
    }
}
