using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Navigation;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Matching;

/// <summary>
/// Boxes every whole-word occurrence of the currently-selected identifier (the classic "select a
/// word → all matches highlighted" QoL). Pure text — needs no <see cref="SemanticModel"/>, so it also
/// works in the read-only DDL-preview editor. Ported verbatim from the former occurrence highlighter.
/// </summary>
public sealed class SelectionOccurrenceProducer : IRelatedElementProducer
{
    public void Collect(MatchContext ctx, ICollection<TextSpan> into)
    {
        var word = ctx.Selection;
        if (!IsIdentifier(word)) return;
        var text = ctx.Text;
        int i = 0;
        while (i <= text.Length - word!.Length)
        {
            int idx = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            int end = idx + word.Length;
            bool boundaryLeft = idx == 0 || !IsIdentChar(text[idx - 1]);
            bool boundaryRight = end >= text.Length || !IsIdentChar(text[end]);
            if (boundaryLeft && boundaryRight)
            {
                into.Add(new TextSpan(idx, word.Length));
            }
            i = idx + 1;
        }
    }

    private static bool IsIdentifier(string? s)
    {
        if (string.IsNullOrEmpty(s) || s!.Length < 2) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
        {
            if (!IsIdentChar(c)) return false;
        }
        return true;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}

/// <summary>
/// Boxes every occurrence of the script-local symbol under the caret (an alias / variable / parameter /
/// cursor / CTE / NEW-OLD record alias). Semantic — driven by the <see cref="SemanticModel"/>, not text.
/// Extracted from <c>NavigationController</c>'s former reference highlighter (Stage 8 / M1); schema
/// objects and columns are excluded on purpose (boxing every use of a table/column would be noise).
/// </summary>
public sealed class CaretSymbolReferenceProducer : IRelatedElementProducer
{
    public void Collect(MatchContext ctx, ICollection<TextSpan> into)
    {
        var spans = Compute(ctx.Model, ctx.Caret);
        // Only light up a symbol that actually appears more than once — a lone declaration boxing itself
        // would be noise (preserves the old renderer's >= 2 gate).
        if (spans.Count < 2) return;
        foreach (var span in spans) into.Add(span);
    }

    /// <summary>The local-reference spans of the symbol at <paramref name="caret"/> — empty unless the
    /// offset is on a script-local symbol. The un-gated computation (used by the headless test seam);
    /// the >= 2 draw gate lives in <see cref="Collect"/>.</summary>
    public static IReadOnlyList<TextSpan> Compute(SemanticModel? model, int caret)
    {
        if (model is null) return Array.Empty<TextSpan>();
        var symbol = model.ReferenceAt(caret)?.Symbol;
        if (symbol is null || !IsLocalHighlightSymbol(symbol)) return Array.Empty<TextSpan>();
        return NavigationEngine.LocalReferences(model, caret);
    }

    private static bool IsLocalHighlightSymbol(Symbol symbol) => symbol
        is TableReferenceSymbol or VariableSymbol or ParameterSymbol
        or CursorSymbol or CteSymbol or RecordAliasSymbol;
}

/// <summary>
/// Highlights the matching partner of the bracket at the caret — <c>()</c>, <c>[]</c>, or <c>{}</c>,
/// caret-adjacent (immediately before or after the bracket), modern-IDE style. Generic over a bracket
/// <see cref="Pairs"/> table (adding a pair is data, not code). Tokenizes the current text with the ONE
/// shared <see cref="SqlLexer"/> — never an ad-hoc scan — so a bracket inside a string / comment /
/// quoted identifier is not a bracket token and is correctly never matched. Model-independent and always
/// in sync with the text (works in the DDL-preview editor too).
/// </summary>
public sealed class BracketMatchProducer : IRelatedElementProducer
{
    private static readonly (char Open, char Close)[] Pairs = { ('(', ')'), ('[', ']'), ('{', '}') };

    public void Collect(MatchContext ctx, ICollection<TextSpan> into)
    {
        var text = ctx.Text;
        if (string.IsNullOrEmpty(text)) return;
        int caret = ctx.Caret;

        // Cheap gate: only tokenize when the caret is actually adjacent to a bracket CHARACTER. Prefer the
        // char after the caret, then the one before — so "(|)" resolves to the '(' pair deterministically.
        int bracketPos = -1;
        if (caret < text.Length && IsBracketChar(text[caret])) bracketPos = caret;
        else if (caret > 0 && IsBracketChar(text[caret - 1])) bracketPos = caret - 1;
        if (bracketPos < 0) return;

        var tokens = SqlLexer.Tokenize(text);

        // Find the bracket TOKEN starting exactly at that position. If there is none, the character lives
        // inside a string / comment / identifier — not a bracket — so there is nothing to match.
        int at = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Start == bracketPos && TryClassify(tokens[i], out _, out _)) { at = i; break; }
            if (tokens[i].Start > bracketPos) break;
        }
        if (at < 0) return;

        TryClassify(tokens[at], out int family, out bool isOpen);
        int partner = isOpen
            ? ScanForward(tokens, at, family)
            : ScanBackward(tokens, at, family);
        if (partner < 0) return;

        into.Add(new TextSpan(tokens[at].Start, tokens[at].Length));
        into.Add(new TextSpan(tokens[partner].Start, tokens[partner].Length));
    }

    // Depth scan within one bracket family (other families never affect this family's depth), so nested
    // parentheses match correctly for well-formed input.
    private static int ScanForward(IReadOnlyList<SqlToken> tokens, int from, int family)
    {
        int depth = 0;
        for (int i = from; i < tokens.Count; i++)
        {
            if (!TryClassify(tokens[i], out int f, out bool open) || f != family) continue;
            depth += open ? 1 : -1;
            if (depth == 0) return i;
        }
        return -1;
    }

    private static int ScanBackward(IReadOnlyList<SqlToken> tokens, int from, int family)
    {
        int depth = 0;
        for (int i = from; i >= 0; i--)
        {
            if (!TryClassify(tokens[i], out int f, out bool open) || f != family) continue;
            depth += open ? 1 : -1;
            if (depth == 0) return i;
        }
        return -1;
    }

    private static bool IsBracketChar(char c)
    {
        foreach (var (open, close) in Pairs)
        {
            if (c == open || c == close) return true;
        }
        return false;
    }

    /// <summary>Classifies a single-character punctuation token as a bracket of one <see cref="Pairs"/>
    /// family. Round <c>()</c> lex to <see cref="TokenKind.LParen"/>/<see cref="TokenKind.RParen"/>;
    /// <c>[]</c>/<c>{}</c> lex to <see cref="TokenKind.Operator"/> — so classify by the token's single
    /// character across those punctuation kinds, not by kind alone.</summary>
    private static bool TryClassify(SqlToken token, out int family, out bool isOpen)
    {
        family = -1;
        isOpen = false;
        if (token.Length != 1) return false;
        if (token.Kind is not (TokenKind.LParen or TokenKind.RParen or TokenKind.Operator)) return false;
        char c = token.Text[0];
        for (int f = 0; f < Pairs.Length; f++)
        {
            if (c == Pairs[f].Open) { family = f; isOpen = true; return true; }
            if (c == Pairs[f].Close) { family = f; isOpen = false; return true; }
        }
        return false;
    }
}

/// <summary>
/// Highlights the matching <c>BEGIN</c>/<c>END</c> when the caret is on/adjacent to either — for every
/// PSQL block: procedure / function / trigger / EXECUTE BLOCK / anonymous bodies AND the bodies of
/// <c>IF</c> / <c>WHILE</c> / <c>FOR</c> (their body is a <see cref="BlockStatement"/>). Source is the
/// AST (<see cref="SemanticModel.Syntax"/>), never a text scan; a <c>CASE … END</c> inside a block is not
/// a <see cref="BlockStatement"/>, so its <c>END</c> is correctly not matched here (a future CASE/END
/// producer would own it). Validates the tokens against the current text before drawing, so a momentarily
/// stale AST (between an edit and the debounced re-parse) can never box the wrong words.
/// </summary>
public sealed class BlockMatchProducer : IRelatedElementProducer
{
    public void Collect(MatchContext ctx, ICollection<TextSpan> into)
    {
        var model = ctx.Model;
        if (model is null) return;
        int caret = ctx.Caret;
        var text = ctx.Text;

        foreach (var block in model.Syntax.Descendants<BlockStatement>())
        {
            var begin = FirstKeyword(block, "begin");
            var end = LastKeyword(block, "end");
            if (begin is null || end is null) continue;

            var beginSpan = new TextSpan(begin.Start, begin.Length);
            var endSpan = new TextSpan(end.Start, end.Length);
            if (!CaretOn(caret, beginSpan) && !CaretOn(caret, endSpan)) continue;

            // Stale-AST guard: only draw if the text at these spans still reads begin/end.
            if (!TextEquals(text, beginSpan, "begin") || !TextEquals(text, endSpan, "end")) continue;

            into.Add(beginSpan);
            into.Add(endSpan);
            return; // a begin/end token belongs to exactly one block — nothing more to find
        }
    }

    // A block's own delimiters: the FIRST 'begin' and the LAST 'end' in its token slice (a nested block's
    // begin/end sit in between, so this reliably picks the outer pair for this node).
    private static SqlToken? FirstKeyword(BlockStatement block, string word)
    {
        foreach (var token in block.Tokens)
        {
            if (string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase)) return token;
        }
        return null;
    }

    private static SqlToken? LastKeyword(BlockStatement block, string word)
    {
        SqlToken? found = null;
        foreach (var token in block.Tokens)
        {
            if (string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase)) found = token;
        }
        return found;
    }

    // Caret-adjacent OR inside the keyword: [Start, End] inclusive at both ends.
    private static bool CaretOn(int caret, TextSpan span) => caret >= span.Start && caret <= span.End;

    private static bool TextEquals(string text, TextSpan span, string expected)
        => span.Start >= 0 && span.End <= text.Length
        && string.Compare(text, span.Start, expected, 0, expected.Length, StringComparison.OrdinalIgnoreCase) == 0
        && span.Length == expected.Length;
}
