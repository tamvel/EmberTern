using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 follow-up — a call's argument list rides the SAME shared adaptive builder
/// (<c>FormatAdaptiveList</c> / <c>PackWithContinuation</c>) as INSERT / VALUES / MATCHING / SELECT /
/// IN. ONE mechanism for every "identifier ( comma-list )": EXECUTE PROCEDURE, function/procedure
/// calls, and any other call — no per-construct argument formatter. Short lists stay inline
/// (byte-identical to before); long lists pack under the opening paren instead of sitting on one line.
/// </summary>
public class SqlFormatterCallArgumentTests
{
    [Fact]
    public void ExecuteProcedure_LongArgList_WrapsUnderParen()
    {
        var args = string.Join(", ", Enumerable.Range(1, 26).Select(i => $":a_long_argument_name_{i:D2}"));
        var outp = SqlFormatter.Format($"EXECUTE PROCEDURE my_procedure_name({args})");
        var lines = outp.Split('\n');

        Assert.True(lines.Length > 1, "expected the long arg list to wrap");
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"line exceeds 120 ({l.Length}): {l}"));
        // Packed, not one-per-line: at least one line carries several args.
        Assert.Contains(lines, l => l.Count(ch => ch == ',') >= 2);
        // Continuation is aligned under the '(' (all wrapped lines share the same indent).
        var indent = new string(' ', "execute procedure my_procedure_name(".Length);
        Assert.All(lines.Skip(1), l => Assert.StartsWith(indent, l));
        // §0 + idempotent.
        Assert.Equal(Lexemes($"EXECUTE PROCEDURE my_procedure_name({args})"), Lexemes(outp));
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Fact]
    public void ExecuteProcedure_ShortArgList_StaysInline()
    {
        Assert.Equal(
            "execute procedure my_proc(1, 'x', :p)",
            SqlFormatter.Format("EXECUTE PROCEDURE my_proc(1, 'x', :p)"));
    }

    [Fact]
    public void FunctionCall_ShortArgs_StayInline()
    {
        Assert.Equal(
            "select coalesce(a, 0), foo(x, y)\nfrom t",
            SqlFormatter.Format("SELECT COALESCE(A, 0), FOO(X, Y) FROM T"));
    }

    [Fact]
    public void FunctionCall_LongArgs_WrapUnderParen()
    {
        var args = string.Join(", ", Enumerable.Range(1, 24).Select(i => $":parameter_number_{i:D2}"));
        var outp = SqlFormatter.Format($"SELECT my_function({args}) FROM t");
        var lines = outp.Split('\n');
        Assert.True(lines.Length > 2, "expected the long call to wrap");
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"line exceeds 120 ({l.Length}): {l}"));
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Fact]
    public void NestedFunctionCall_CommaInsideInnerCall_NotSplitAcrossArgs()
    {
        // The nesting-aware splitter keeps coalesce(x, 0) as ONE argument of the outer call.
        Assert.Equal(
            "select wrap(coalesce(x, 0), y)\nfrom t",
            SqlFormatter.Format("SELECT WRAP(COALESCE(X, 0), Y) FROM T"));
    }

    [Fact]
    public void QualifiedCall_PackageFunction_Wraps()
    {
        var args = string.Join(", ", Enumerable.Range(1, 20).Select(i => $":argument_value_{i:D2}"));
        var outp = SqlFormatter.Format($"EXECUTE PROCEDURE my_package.my_routine({args})");
        Assert.Contains("my_package.my_routine(", outp);
        Assert.All(outp.Split('\n'), l => Assert.True(l.Length <= 120, $"line exceeds 120: {l}"));
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Fact]
    public void Call_InsidePsqlBody_WrapsConsistently()
    {
        var args = string.Join(", ", Enumerable.Range(1, 24).Select(i => $":a_procedure_argument_{i:D2}"));
        var outp = SqlFormatter.Format($"begin execute procedure inner_proc({args}); end");
        var lines = outp.Split('\n');
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"line exceeds 120 ({l.Length}): {l}"));
        Assert.StartsWith("  execute procedure inner_proc(", lines[1]); // indented by the block, still wraps
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Theory]
    [InlineData("EXECUTE PROCEDURE p(1, 2, 3)")]
    [InlineData("SELECT gen_id(g, 1), coalesce(a, b, c) FROM t")]
    [InlineData("EXECUTE PROCEDURE p")]                  // no arg list — untouched
    [InlineData("EXECUTE PROCEDURE p 1, 2")]             // Firebird parens-less form — untouched
    public void ShortAndParenless_AreStableAndIdempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
        Assert.Equal(Lexemes(sql), Lexemes(once));
    }

    private static List<string> Lexemes(string sql)
    {
        var list = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
                if (tr.Kind is TriviaKind.LineComment or TriviaKind.BlockComment) list.Add("c:" + tr.Text.TrimEnd());
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
