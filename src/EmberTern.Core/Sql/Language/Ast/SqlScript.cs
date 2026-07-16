using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// The root of a parsed SQL/PSQL script: the ordered <see cref="Statements"/> plus the complete,
/// lossless token stream they were built from. The script owns the original source
/// (<see cref="Text"/>) and every <see cref="SqlToken"/> (including the trailing
/// <see cref="TokenKind.EndOfFile"/> token that carries any trailing trivia).
/// <para>
/// <see cref="ToSourceString"/> reconstructs the original input byte-for-byte from the token
/// stream — the machine-checkable §0 (Paramount Law) invariant. It depends only on the lexer's
/// losslessness, never on how deeply the parser modelled each statement, so the round-trip holds
/// for unrecognised (<see cref="RawStatement"/>) and partially-modelled statements alike.
/// </para>
/// </summary>
public sealed class SqlScript : SqlNode
{
    /// <summary>The original source text this script was parsed from.</summary>
    public string Text { get; }

    /// <summary>The complete lossless token stream (significant tokens + trailing end-of-file).</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <summary>The top-level statements, in source order.</summary>
    public IReadOnlyList<SqlStatement> Statements { get; }

    public SqlScript(string text, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlStatement> statements)
        : base(0, text.Length)
    {
        Text = text;
        Tokens = tokens;
        Statements = statements;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Statements;

    /// <summary>
    /// Reconstructs the original source from the token stream (each token's leading trivia
    /// followed by its text). Guaranteed byte-for-byte equal to <see cref="Text"/> — the §0
    /// round-trip invariant.
    /// </summary>
    public string ToSourceString()
    {
        var sb = new StringBuilder(Text.Length);
        foreach (var token in Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                sb.Append(trivia.Text);
            }
            sb.Append(token.Text);
        }
        return sb.ToString();
    }
}
