using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 Krok 0 — <b>Formatter Safety</b>. The paramount rule (§0): <i>EmberTern never modifies code it
/// cannot reproduce 1:1.</i> The formatter re-emits from a flattened token model rather than by copying
/// the source span, so — unlike the AST's byte-for-byte token overlay — each emit path is individually
/// responsible for reproducing every token. This suite proves the guarantee holds even for
/// <b>malformed / incomplete / mid-edit</b> SQL and PSQL, which is exactly where a structure-aware
/// emitter is tempted to drop a token it cannot place.
/// <para>
/// The contract: for ANY input, the formatted output either (a) reproduces every lexeme of the input —
/// significant tokens (words case-insensitive; strings / numbers / quoted identifiers / punctuation
/// exact) and every comment — or (b) leaves the fragment / whole document unchanged. Never a token
/// dropped, added, reordered, or mangled. This is verified here token-for-token over an adversarial
/// corpus; <c>SqlFormatterInvariantsTests</c> covers the same invariant over the well-formed corpus.
/// </para>
/// </summary>
public class SqlFormatterSafetyTests
{
    // Deliberately broken / incomplete / mid-typing SQL & PSQL. Each case exercises a path where the
    // block structurer or clause emitter could fail to place a token.
    public static IEnumerable<object[]> MalformedCorpus() => new[]
    {
        // Stray / unmatched END — the originally-reported §0 leak (an extra END past the block close).
        new object[] { "begin x = 1; end end" },
        new object[] { "begin x = 1; end; end" },
        new object[] { "end" },
        new object[] { "end;" },
        new object[] { "x = 1; end" },
        new object[] { "begin end end end" },
        // Unbalanced BEGIN (more opens than closes) — truncated mid-edit.
        new object[] { "begin x = 1;" },
        new object[] { "begin begin x = 1; end" },
        new object[] { "begin if (a = 1) then begin x = 1; end end" },
        // Incomplete control-flow headers/bodies.
        new object[] { "begin if (a = 1) then end" },
        new object[] { "begin while (x > 0) do end" },
        new object[] { "begin for select a from t into :a do end" },
        new object[] { "begin if (a) then b = 1; else end" },
        // Truncated local subprogram (DECLARE PROCEDURE / bare FUNCTION in a package body).
        new object[] { "begin declare procedure p end" },
        new object[] { "begin declare function f returns integer end" },
        // Half-typed EXECUTE BLOCK / definitions.
        new object[] { "execute block as begin end end" },
        new object[] { "create procedure p as begin" },
        new object[] { "create or alter procedure p as begin insert into t (a) values (1); end end" },
        new object[] { "create trigger" },
        new object[] { "alter" },
        // Stray words / punctuation inside a body.
        new object[] { "begin foo bar baz; end" },
        new object[] { "begin x = (1; end" },
        new object[] { ") ) )" },
        new object[] { "select from where" },
        // Comments interleaved with malformed structure — comments must survive too.
        new object[] { "begin -- note\n end end" },
        new object[] { "/* leading */ create procedure p as begin end" },
        new object[] { "begin /* c1 */ end /* c2 */ end" },
        new object[] { "end -- trailing" },
        // Literals inside malformed input must pass through byte-exact.
        new object[] { "begin x = 'it''s'; end end" },
        new object[] { "begin \"Quoted\" = 0x1F; end end end" },
    };

    [Theory]
    [MemberData(nameof(MalformedCorpus))]
    public void MalformedInput_NeverLosesALexeme(string sql)
    {
        // The core §0 guarantee: input and output carry the identical ordered lexeme sequence
        // (significant tokens + comments). No drop, add, reorder, or mangle — even on broken input.
        Assert.Equal(Lexemes(sql), Lexemes(SqlFormatter.Format(sql)));
    }

    [Theory]
    [MemberData(nameof(MalformedCorpus))]
    public void MalformedInput_NeverThrows(string sql)
    {
        Assert.Null(Record.Exception(() => SqlFormatter.Format(sql)));
    }

    [Theory]
    [MemberData(nameof(MalformedCorpus))]
    public void MalformedInput_IsIdempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // ── Concrete regression pins ────────────────────────────────────────────────────────────────

    [Fact]
    public void StrayEnd_IsNotDropped()
    {
        // The exact §0 leak this Krok closes: a second, unmatched END past the block close used to
        // vanish (the anti-stall guard advanced the index without emitting the token). It must now
        // appear in the output.
        var outp = SqlFormatter.Format("begin x = 1; end end");
        Assert.Equal(2, CountWord(outp, "end"));
        // And nothing else was lost.
        Assert.Equal(Lexemes("begin x = 1; end end"), Lexemes(outp));
    }

    [Fact]
    public void LeadingComment_BeforeProcedureDefinition_IsKept()
    {
        // A comment before CREATE PROCEDURE lives in the first token's leading trivia — outside the
        // verbatim header span. It used to be dropped by the header/body path; it must survive now.
        var outp = SqlFormatter.Format("/* keep me */ create procedure p as begin x = 1; end");
        Assert.Contains("/* keep me */", outp);
    }

    [Fact]
    public void GrosslyMalformed_FallsBackWithoutLoss()
    {
        // Even input the formatter cannot structure at all round-trips its lexemes.
        const string sql = "begin declare foo procedure ( ; end while end";
        Assert.Equal(Lexemes(sql), Lexemes(SqlFormatter.Format(sql)));
    }

    // ── §0 lexeme extraction (mirrors SqlFormatter's internal invariant) ────────────────────────

    // The ordered lexeme sequence of a document: every comment (line comments trailing-trimmed, block
    // comments exact) and every significant token (words + parameters upper-cased since the formatter
    // lowercases them; strings / numbers / quoted identifiers / punctuation exact), interleaved in
    // source order — the exact quantity §0 requires the formatter to preserve.
    private static List<string> Lexemes(string sql)
    {
        var list = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind == TriviaKind.LineComment) list.Add("c:" + tr.Text.TrimEnd());
                else if (tr.Kind == TriviaKind.BlockComment) list.Add("c:" + tr.Text.TrimEnd());
            }
            if (t.Kind == TokenKind.EndOfFile) continue;
            list.Add(t.Kind switch
            {
                TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter => "w:" + t.Text.ToUpperInvariant(),
                _ => "x:" + t.Text,
            });
        }
        return list;
    }

    private static int CountWord(string text, string word)
    {
        int n = 0;
        foreach (var t in SqlLexer.Tokenize(text))
        {
            if (t.Kind is TokenKind.Keyword or TokenKind.Identifier
                && string.Equals(t.Text, word, StringComparison.OrdinalIgnoreCase))
            {
                n++;
            }
        }
        return n;
    }
}
