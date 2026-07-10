using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The single Firebird-aware lexer (Etap 1). The load-bearing guarantee is losslessness
/// (the token stream round-trips byte-for-byte — the §0 Paramount Law foundation); the rest
/// pins Firebird lexical shapes: strings/quoted identifiers with doubled-quote escapes,
/// comments-as-trivia, <c>?</c>/<c>:name</c>/<c>@name</c> parameters, <c>$</c> identifiers,
/// numbers, and multi-character operators.
/// </summary>
public class SqlLexerTests
{
    // A representative corpus: DML, PSQL, comments, strings, params, quoted idents, numbers,
    // operators, dotted names, and pathological/unterminated constructs.
    public static IEnumerable<object[]> Corpus() => new[]
    {
        new object[] { "" },
        new object[] { "   \t\r\n  " },
        new object[] { "SELECT * FROM NAGL N WHERE N.ID = 10" },
        new object[] { "select id, name from t where x <= 5 and y <> 'a''b'" },
        new object[] { "-- leading comment\r\nSELECT 1 FROM RDB$DATABASE" },
        new object[] { "SELECT /* inline */ COUNT(*) FROM \"My Table\" \"My Alias\"" },
        new object[] { "UPDATE T SET DATA = ? WHERE ID = ? AND NAME = :name" },
        new object[] { "EXECUTE PROCEDURE SP_X(1, 'text', @p)" },
        new object[] { "CREATE OR ALTER PROCEDURE P (A INTEGER = 1) AS\r\nDECLARE V INT;\r\nBEGIN\r\n  V = A + 1;\r\n  SUSPEND;\r\nEND" },
        new object[] { "SELECT CASE WHEN a >= b THEN 1 ELSE 0 END FROM t" },
        new object[] { "SELECT 0xFF, 1.5, .25, 1e10, 3.14E-2 FROM t" },
        new object[] { "SELECT a || b, c::int FROM t" },
        new object[] { "SELECT 'unterminated string" },
        new object[] { "SELECT \"unterminated ident" },
        new object[] { "SELECT 1 /* unterminated block comment" },
        new object[] { "RDB$RELATIONS.RDB$RELATION_NAME" },
        new object[] { "a--b\nc" },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Tokenize_RoundTripsByteForByte(string src)
    {
        var tokens = SqlLexer.Tokenize(src);
        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            foreach (var tr in t.LeadingTrivia) sb.Append(tr.Text);
            sb.Append(t.Text);
        }
        Assert.Equal(src, sb.ToString());
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Tokenize_SpansAreContiguousAndEndsWithEof(string src)
    {
        var tokens = SqlLexer.Tokenize(src);
        Assert.NotEmpty(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[^1].Kind);

        int cursor = 0;
        foreach (var t in tokens)
        {
            foreach (var tr in t.LeadingTrivia)
            {
                Assert.Equal(cursor, tr.Start);
                cursor = tr.End;
            }
            Assert.Equal(cursor, t.Start);
            cursor = t.End;
        }
        Assert.Equal(src.Length, cursor);
    }

    [Fact]
    public void EmptyInput_YieldsOnlyEof()
    {
        var tokens = SqlLexer.Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
        Assert.Empty(tokens[0].LeadingTrivia);
    }

    [Fact]
    public void WhitespaceOnly_IsTrailingTriviaOfEof()
    {
        var tokens = SqlLexer.Tokenize("   ");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
        Assert.Equal(TriviaKind.Whitespace, Assert.Single(tokens[0].LeadingTrivia).Kind);
    }

    [Fact]
    public void KnownWord_IsKeyword_UnknownWord_IsIdentifier()
    {
        var tokens = Significant("SELECT mycolumn");
        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("SELECT", tokens[0].Text);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("mycolumn", tokens[1].Text); // source case preserved
    }

    [Fact]
    public void QuotedIdentifier_DecodesValue_AndKeepsRawText()
    {
        var t = Assert.Single(Significant("\"My Table\""));
        Assert.Equal(TokenKind.QuotedIdentifier, t.Kind);
        Assert.Equal("\"My Table\"", t.Text);
        Assert.Equal("My Table", t.Value);
    }

    [Fact]
    public void QuotedIdentifier_DoubledQuoteEscape_IsCollapsedInValue()
    {
        var t = Assert.Single(Significant("\"a\"\"b\""));
        Assert.Equal("a\"b", t.Value);
    }

    [Fact]
    public void StringLiteral_WithEmbeddedQuestionMark_IsOneToken_NotAParameter()
    {
        var t = Assert.Single(Significant("'? literal'"));
        Assert.Equal(TokenKind.StringLiteral, t.Kind);
        Assert.Equal("'? literal'", t.Text);
    }

    [Fact]
    public void StringLiteral_DoubledQuoteEscape_IsOneToken()
    {
        var t = Assert.Single(Significant("'it''s'"));
        Assert.Equal(TokenKind.StringLiteral, t.Kind);
        Assert.Equal("'it''s'", t.Text);
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("1.5", "1.5")]
    [InlineData(".25", ".25")]
    [InlineData("1e10", "1e10")]
    [InlineData("3.14E-2", "3.14E-2")]
    [InlineData("0xFF", "0xFF")]
    public void Number_Forms_AreSingleTokens(string src, string expected)
    {
        var t = Assert.Single(Significant(src));
        Assert.Equal(TokenKind.Number, t.Kind);
        Assert.Equal(expected, t.Text);
    }

    [Fact]
    public void Parameters_PositionalNamedAndAt()
    {
        var t = Significant("? :name @p");
        Assert.All(t, x => Assert.Equal(TokenKind.Parameter, x.Kind));
        Assert.Equal(new[] { "?", ":name", "@p" }, t.Select(x => x.Text));
    }

    [Fact]
    public void DoubleColon_IsOperator_NotParameter()
    {
        var t = Significant("x::int");
        Assert.Equal(TokenKind.Identifier, t[0].Kind);
        Assert.Equal(TokenKind.Operator, t[1].Kind);
        Assert.Equal("::", t[1].Text);
        Assert.Equal(TokenKind.Keyword, t[2].Kind); // INT is a data-type keyword
    }

    [Theory]
    [InlineData("<=")]
    [InlineData(">=")]
    [InlineData("<>")]
    [InlineData("!=")]
    [InlineData("||")]
    public void MultiCharOperators_AreSingleTokens(string op)
    {
        var t = Assert.Single(Significant(op));
        Assert.Equal(TokenKind.Operator, t.Kind);
        Assert.Equal(op, t.Text);
    }

    [Fact]
    public void Punctuation_HasDistinctKinds()
    {
        var t = Significant("(a, b).c;");
        Assert.Equal(TokenKind.LParen, t[0].Kind);
        Assert.Equal(TokenKind.Comma, t[2].Kind);
        Assert.Equal(TokenKind.RParen, t[4].Kind);
        Assert.Equal(TokenKind.Dot, t[5].Kind);
        Assert.Equal(TokenKind.Semicolon, t[7].Kind);
    }

    [Fact]
    public void DottedName_IsIdentifierDotIdentifier()
    {
        // 'alpha'/'beta' are not catalog keywords, so both sides lex as identifiers.
        var t = Significant("alpha.beta");
        Assert.Equal(new[] { TokenKind.Identifier, TokenKind.Dot, TokenKind.Identifier },
            t.Select(x => x.Kind));
    }

    [Fact]
    public void DollarSign_ContinuesAnIdentifier()
    {
        var t = Assert.Single(Significant("RDB$RELATIONS"));
        Assert.Equal(TokenKind.Identifier, t.Kind);
        Assert.Equal("RDB$RELATIONS", t.Text);
    }

    [Fact]
    public void Comments_AttachAsLeadingTrivia_NotTokens()
    {
        var tokens = SqlLexer.Tokenize("-- note\n/* blk */ SELECT");
        // First significant token is SELECT; the comments + whitespace precede it as trivia.
        var select = tokens[0];
        Assert.Equal(TokenKind.Keyword, select.Kind);
        Assert.Equal("SELECT", select.Text);
        Assert.Contains(select.LeadingTrivia, tr => tr.Kind == TriviaKind.LineComment);
        Assert.Contains(select.LeadingTrivia, tr => tr.Kind == TriviaKind.BlockComment);
    }

    private static IReadOnlyList<SqlToken> Significant(string src)
        => SqlLexer.Tokenize(src).Where(t => t.Kind != TokenKind.EndOfFile).ToList();
}
