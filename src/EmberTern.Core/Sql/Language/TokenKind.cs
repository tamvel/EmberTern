namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The lexical kind of a <see cref="SqlToken"/>. One enum for the whole application — the
/// Firebird-aware lexer produces these; the parser (Etap 2) and every other language client
/// consume them. Whitespace and comments are NOT tokens: they are attached to the following
/// significant token as <see cref="SqlToken.LeadingTrivia"/>.
/// </summary>
public enum TokenKind
{
    /// <summary>The synthetic end-of-input token (carries any trailing trivia).</summary>
    EndOfFile,

    /// <summary>An unquoted word that is NOT in the <see cref="FirebirdSyntax"/> catalog.</summary>
    Identifier,

    /// <summary>A <c>"…"</c>-quoted identifier (doubled <c>""</c> escape).</summary>
    QuotedIdentifier,

    /// <summary>An unquoted word that IS in the <see cref="FirebirdSyntax"/> catalog.</summary>
    Keyword,

    /// <summary>A <c>'…'</c> string literal (doubled <c>''</c> escape).</summary>
    StringLiteral,

    /// <summary>A numeric literal (integer, decimal, exponent, or <c>0x…</c> hex).</summary>
    Number,

    /// <summary>A parameter marker: positional <c>?</c>, named <c>:name</c>, or <c>@name</c>.</summary>
    Parameter,

    /// <summary>A comma <c>,</c>.</summary>
    Comma,

    /// <summary>A dot <c>.</c> (member access / qualifier separator).</summary>
    Dot,

    /// <summary>A statement terminator <c>;</c>.</summary>
    Semicolon,

    /// <summary>An opening parenthesis <c>(</c>.</summary>
    LParen,

    /// <summary>A closing parenthesis <c>)</c>.</summary>
    RParen,

    /// <summary>An operator or other punctuation (<c>= &lt; &gt; &lt;= &gt;= &lt;&gt; != || :: + - * / % …</c>).</summary>
    Operator,

    /// <summary>A single character the lexer does not otherwise recognise (kept for losslessness).</summary>
    Unknown,
}
