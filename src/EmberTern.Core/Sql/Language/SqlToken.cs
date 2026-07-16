using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// One significant token from the <see cref="SqlLexer"/>: its lexical <see cref="Kind"/>, its
/// absolute source span, its verbatim <see cref="Text"/>, and the <see cref="LeadingTrivia"/>
/// (whitespace + comments) immediately before it. Concatenating, for every token in order, its
/// leading-trivia text followed by its <see cref="Text"/> reproduces the source exactly — the
/// round-trip guarantee the parser and formatter rely on (§0 Paramount Law).
/// </summary>
/// <param name="Kind">The lexical kind.</param>
/// <param name="Start">Absolute source offset where the token text begins (after its trivia).</param>
/// <param name="Length">Length of the token text in characters.</param>
/// <param name="Text">The exact source text of the token (empty for <see cref="TokenKind.EndOfFile"/>).</param>
/// <param name="LeadingTrivia">Whitespace/comments immediately preceding this token (never null; empty when none).</param>
public sealed record SqlToken(
    TokenKind Kind,
    int Start,
    int Length,
    string Text,
    IReadOnlyList<SqlTrivia> LeadingTrivia)
{
    /// <summary>Absolute source offset just past the token text.</summary>
    public int End => Start + Length;

    /// <summary>True when this is the synthetic end-of-input token.</summary>
    public bool IsEndOfFile => Kind == TokenKind.EndOfFile;

    /// <summary>
    /// The token's semantic value. For a <see cref="TokenKind.QuotedIdentifier"/> this is the
    /// inner name with the surrounding quotes removed and doubled <c>""</c> collapsed to a
    /// single <c>"</c>; for every other kind it is <see cref="Text"/> unchanged.
    /// </summary>
    public string Value => Kind == TokenKind.QuotedIdentifier ? DecodeQuotedIdentifier(Text) : Text;

    private static string DecodeQuotedIdentifier(string raw)
    {
        if (raw.Length < 1 || raw[0] != '"')
        {
            return raw;
        }
        var sb = new StringBuilder(raw.Length);
        int i = 1;
        while (i < raw.Length)
        {
            if (raw[i] == '"')
            {
                if (i + 1 < raw.Length && raw[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                break; // closing quote
            }
            sb.Append(raw[i]);
            i++;
        }
        return sb.ToString();
    }
}
