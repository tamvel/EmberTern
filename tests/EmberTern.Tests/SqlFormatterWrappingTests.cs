using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 long-line wrapping — now ONE mechanism at the token level (inside <c>Emit</c>: SELECT column
/// lists and IN value lists laid out by the shared adaptive builders). The former string-level
/// post-pass and its char scanners are gone. These tests pin the consequences that matter: wrapping is
/// the same whether a SELECT sits at the top level or inside a PSQL body (one mechanism, not two), an
/// INSERT … SELECT source wraps too, and everything stays idempotent and lexeme-lossless.
/// <para>The exhaustive SELECT/IN wrapping behavior (indent, packing, close-paren-inline, subquery
/// exclusion) is pinned by <c>SqlFormatterTests</c>; this file focuses on the unification.</para>
/// </summary>
public class SqlFormatterWrappingTests
{
    private const int MaxLineWidth = 120;

    private static string ManyCols(int n) =>
        string.Join(", ", Enumerable.Range(1, n).Select(i => $"long_column_name_{i:D2}"));

    [Fact]
    public void SelectColumns_Wrap_TheSameWay_TopLevel_And_InsideExecuteBlock()
    {
        var cols = ManyCols(20);

        var top = SqlFormatter.Format($"SELECT {cols} FROM t");
        var body = SqlFormatter.Format($"EXECUTE BLOCK AS BEGIN FOR SELECT {cols} FROM t INTO :x DO BEGIN END; END");

        // Both wrap the column list (more than one line for the select), packing multiple per line.
        Assert.Contains(top.Split('\n'), l => l.TrimStart().StartsWith("long_column_name_") && CountCommas(l) >= 2);
        Assert.Contains(body.Split('\n'), l => l.TrimStart().StartsWith("long_column_name_") && CountCommas(l) >= 2);

        // The top-level select columns respect the width budget on every line.
        Assert.All(top.Split('\n'), l => Assert.True(l.Length <= MaxLineWidth, $"top line over 120: {l}"));
    }

    [Fact]
    public void InsertSelect_LongColumns_Wrap()
    {
        var cols = ManyCols(20);
        var outp = SqlFormatter.Format($"INSERT INTO t (a) SELECT {cols} FROM s");

        // The INSERT … SELECT source's column list wraps (packed multiple per line), via the same Emit.
        Assert.Contains(outp.Split('\n'), l => l.TrimStart().StartsWith("long_column_name_") && CountCommas(l) >= 2);
        Assert.All(outp.Split('\n'), l => Assert.True(l.Length <= MaxLineWidth, $"line over 120: {l}"));
        Assert.Equal(Lexemes($"INSERT INTO t (a) SELECT {cols} FROM s"), Lexemes(outp));
    }

    [Fact]
    public void WrappedSelect_IsIdempotent_TopLevel_And_InBody()
    {
        var cols = ManyCols(20);

        var top = SqlFormatter.Format($"SELECT {cols} FROM t");
        Assert.Equal(top, SqlFormatter.Format(top));

        var body = SqlFormatter.Format($"EXECUTE BLOCK AS BEGIN FOR SELECT {cols} FROM t INTO :x DO BEGIN END; END");
        Assert.Equal(body, SqlFormatter.Format(body));
    }

    [Fact]
    public void InList_Wraps_AndIsIdempotent()
    {
        var vals = string.Join(", ", Enumerable.Range(100, 40));
        var outp = SqlFormatter.Format($"SELECT a FROM t WHERE x IN ({vals})");

        Assert.All(outp.Split('\n'), l => Assert.True(l.Length <= MaxLineWidth, $"line over 120: {l}"));
        Assert.EndsWith(")", outp); // close paren stays inline with the last value
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    private static int CountCommas(string s) => s.Count(c => c == ',');

    private static List<string> Lexemes(string sql)
    {
        var list = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind is TriviaKind.LineComment or TriviaKind.BlockComment) list.Add("c:" + tr.Text.TrimEnd());
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
}
