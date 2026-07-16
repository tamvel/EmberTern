using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The single Firebird-aware SQL/PSQL lexer — Etap 1 of the editor rebuild. Turns text into an
/// immutable stream of significant <see cref="SqlToken"/>s (with whitespace/comments attached as
/// each token's <see cref="SqlToken.LeadingTrivia"/>), ending with an
/// <see cref="TokenKind.EndOfFile"/> token that carries any trailing trivia.
/// <para>
/// It is <b>lossless</b>: concatenating, for every token in order, its leading-trivia text
/// followed by its <see cref="SqlToken.Text"/> reproduces the input byte-for-byte. This is the
/// foundation for the §0 Paramount Law (never lose information) that the parser and formatter
/// will rely on. Firebird specifics handled: <c>''</c>-escaped string literals, <c>"…"</c>
/// quoted identifiers, <c>--</c> line and <c>/* */</c> block comments, <c>?</c>/<c>:name</c>/
/// <c>@name</c> parameters, <c>$</c> in identifiers (<c>RDB$…</c>), hex/exponent numbers, and
/// multi-character operators. Keyword vs identifier classification is driven by the single
/// <see cref="FirebirdSyntax"/> catalog.
/// </para>
/// <para>Pure — no Avalonia, no Firebird driver — and offline unit-testable.</para>
/// </summary>
public static class SqlLexer
{
    /// <summary>Tokenizes <paramref name="text"/> into a lossless significant-token stream.</summary>
    public static IReadOnlyList<SqlToken> Tokenize(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var tokens = new List<SqlToken>();
        int i = 0;
        while (true)
        {
            var trivia = ReadLeadingTrivia(text, ref i);
            if (i >= text.Length)
            {
                tokens.Add(new SqlToken(TokenKind.EndOfFile, i, 0, string.Empty, trivia));
                return tokens;
            }

            int start = i;
            TokenKind kind = ScanToken(text, ref i);
            tokens.Add(new SqlToken(kind, start, i - start, text.Substring(start, i - start), trivia));
        }
    }

    // ── Trivia ────────────────────────────────────────────────────────────────────────────

    private static readonly SqlTrivia[] NoTrivia = Array.Empty<SqlTrivia>();

    private static IReadOnlyList<SqlTrivia> ReadLeadingTrivia(string s, ref int i)
    {
        List<SqlTrivia>? pieces = null;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                int st = i;
                do { i++; } while (i < s.Length && char.IsWhiteSpace(s[i]));
                (pieces ??= new()).Add(new SqlTrivia(TriviaKind.Whitespace, st, i - st, s.Substring(st, i - st)));
            }
            else if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                int st = i;
                i += 2;
                while (i < s.Length && s[i] != '\n') i++;
                (pieces ??= new()).Add(new SqlTrivia(TriviaKind.LineComment, st, i - st, s.Substring(st, i - st)));
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int st = i;
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i = i + 1 < s.Length ? i + 2 : s.Length; // consume the closing */, or run to end if unterminated
                (pieces ??= new()).Add(new SqlTrivia(TriviaKind.BlockComment, st, i - st, s.Substring(st, i - st)));
            }
            else
            {
                break;
            }
        }
        return pieces ?? (IReadOnlyList<SqlTrivia>)NoTrivia;
    }

    // ── Tokens ────────────────────────────────────────────────────────────────────────────

    private static TokenKind ScanToken(string s, ref int i)
    {
        char c = s[i];

        if (c == '\'') { SkipQuoted(s, ref i, '\''); return TokenKind.StringLiteral; }
        if (c == '"') { SkipQuoted(s, ref i, '"'); return TokenKind.QuotedIdentifier; }

        if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
        {
            ScanNumber(s, ref i);
            return TokenKind.Number;
        }

        if (IsIdentifierStart(c))
        {
            int st = i;
            i++;
            while (i < s.Length && IsIdentifierPart(s[i])) i++;
            var word = s.Substring(st, i - st);
            return FirebirdSyntax.IsKeyword(word) ? TokenKind.Keyword : TokenKind.Identifier;
        }

        switch (c)
        {
            case '?':
                i++;
                return TokenKind.Parameter;
            case ':':
                if (i + 1 < s.Length && s[i + 1] == ':') { i += 2; return TokenKind.Operator; } // ::
                if (i + 1 < s.Length && IsIdentifierStart(s[i + 1]))
                {
                    i += 2;
                    while (i < s.Length && IsIdentifierPart(s[i])) i++;
                    return TokenKind.Parameter; // :name
                }
                i++;
                return TokenKind.Operator; // lone ':'
            case '@':
                if (i + 1 < s.Length && IsIdentifierStart(s[i + 1]))
                {
                    i += 2;
                    while (i < s.Length && IsIdentifierPart(s[i])) i++;
                    return TokenKind.Parameter; // @name
                }
                i++;
                return TokenKind.Operator; // lone '@'
            case ',': i++; return TokenKind.Comma;
            case '.': i++; return TokenKind.Dot;
            case ';': i++; return TokenKind.Semicolon;
            case '(': i++; return TokenKind.LParen;
            case ')': i++; return TokenKind.RParen;
        }

        if (TryScanMultiCharOperator(s, ref i))
        {
            return TokenKind.Operator;
        }

        if (IsOperatorChar(c))
        {
            i++;
            return TokenKind.Operator;
        }

        // Anything else — a single character we don't classify. Kept as its own token so the
        // stream stays lossless (never drop input).
        i++;
        return TokenKind.Unknown;
    }

    private static void SkipQuoted(string s, ref int i, char quote)
    {
        i++; // opening quote
        while (i < s.Length)
        {
            if (s[i] == quote)
            {
                if (i + 1 < s.Length && s[i + 1] == quote) { i += 2; continue; } // doubled escape
                i++;
                return;
            }
            i++;
        }
        // Unterminated — consumed to end (lossless).
    }

    private static void ScanNumber(string s, ref int i)
    {
        // Hex: 0x… / 0X…
        if (s[i] == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            i += 2;
            while (i < s.Length && IsHexDigit(s[i])) i++;
            return;
        }

        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            if (j < s.Length && char.IsDigit(s[j]))
            {
                i = j + 1;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
        }
    }

    private static bool TryScanMultiCharOperator(string s, ref int i)
    {
        char c = s[i];
        char d = i + 1 < s.Length ? s[i + 1] : '\0';
        // <=  <>  >=  !=  ||   (::  is handled in the ':' branch)
        bool two =
            (c == '<' && (d == '=' || d == '>')) ||
            (c == '>' && d == '=') ||
            (c == '!' && d == '=') ||
            (c == '|' && d == '|');
        if (two)
        {
            i += 2;
            return true;
        }
        return false;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsOperatorChar(char c) => c switch
    {
        '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>' or '!' or '|' or '&' or '~' or '^'
            or '[' or ']' or '{' or '}' => true,
        _ => false,
    };
}
