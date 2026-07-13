using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 INSERT layout. IBExpert-standard: "insert into &lt;target&gt; (cols)" on one line, "values (…)"
/// (or "select …") on its own line, with the column and value lists laid out by the shared adaptive
/// list builder (§F) — inline while they fit the width limit, else packed across lines (multiple items
/// per line, readability-driven — NOT one item per line). "INSERT INTO" is kept as one construct.
/// </summary>
public class SqlFormatterInsertTests
{
    [Fact]
    public void ColumnsAndValues_ShortListsInline_ValuesOnOwnLine()
    {
        Assert.Equal(
            "insert into kontrahent (id, nazwa, nip)\nvalues (1, 'ACME', '123')",
            SqlFormatter.Format("INSERT INTO KONTRAHENT (ID, NAZWA, NIP) VALUES (1, 'ACME', '123')"));
    }

    [Fact]
    public void NoColumnList_ValuesOnOwnLine()
    {
        Assert.Equal(
            "insert into t\nvalues (1, 2)",
            SqlFormatter.Format("INSERT INTO T VALUES (1, 2)"));
    }

    [Fact]
    public void InsertSelect_QueryOnOwnLines()
    {
        Assert.Equal(
            "insert into t (a, b)\nselect x, y\nfrom s",
            SqlFormatter.Format("INSERT INTO T (A, B) SELECT X, Y FROM S"));
    }

    [Fact]
    public void Returning_OnOwnLine()
    {
        Assert.Equal(
            "insert into t (a)\nvalues (1)\nreturning id",
            SqlFormatter.Format("INSERT INTO T (A) VALUES (1) RETURNING ID"));
    }

    [Fact]
    public void DefaultValues_OnOwnLine()
    {
        Assert.Equal(
            "insert into t\ndefault values",
            SqlFormatter.Format("INSERT INTO T DEFAULT VALUES"));
    }

    [Fact]
    public void TrailingSemicolon_IsGluedAndKept_Across_MultiStatement()
    {
        Assert.Equal(
            "insert into t (a)\nvalues (1);\ninsert into u (b)\nvalues (2)",
            SqlFormatter.Format("INSERT INTO T (A) VALUES (1); INSERT INTO U (B) VALUES (2)"));
    }

    [Fact]
    public void CommaInsideFunctionCall_DoesNotSplitAValue()
    {
        // coalesce(a, 0) is ONE value — the nesting-aware splitter keeps its inner comma inside it.
        Assert.Equal(
            "insert into t (a, b)\nvalues (coalesce(x, 0), 2)",
            SqlFormatter.Format("INSERT INTO T (A, B) VALUES (COALESCE(X, 0), 2)"));
    }

    // ── Adaptive wrapping — the key requirement: pack to width, not one item per line ────────────

    [Fact]
    public void LongColumnAndValueLists_WrapAdaptively_PackingMultiplePerLine()
    {
        var cols = string.Join(", ", Enumerable.Range(1, 20).Select(i => $"long_column_name_{i:D2}"));
        var vals = string.Join(", ", Enumerable.Range(1, 20).Select(i => $"value_expr_{i:D2}"));
        var sql = $"INSERT INTO T ({cols}) VALUES ({vals})";

        var outp = SqlFormatter.Format(sql);
        var lines = outp.Split('\n');

        // (a) Every line respects the width budget.
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"line exceeds 120 ({l.Length}): {l}"));
        // (b) Packing, not one-item-per-line: far fewer lines than the 40 items.
        Assert.True(lines.Length < 20, $"expected packed layout, got {lines.Length} lines");
        // (c) At least one wrapped line carries several items (proves it packs, not one per line).
        Assert.Contains(lines, l => l.Count(ch => ch == ',') >= 2);
        // (d) §0 preserved + idempotent.
        Assert.Equal(Lexemes(sql), Lexemes(outp));
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Fact]
    public void ShortInsert_IsIdempotent()
    {
        var once = SqlFormatter.Format("INSERT INTO T (A, B, C) VALUES (1, 'x', :p)");
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // ── UPDATE OR INSERT — same formatter as INSERT (differs only by verb + MATCHING) ───────────

    [Fact]
    public void UpdateOrInsert_MatchingClause_OnOwnLine()
    {
        Assert.Equal(
            "update or insert into t (a, b)\nvalues (1, 2)\nmatching (a)",
            SqlFormatter.Format("UPDATE OR INSERT INTO T (A, B) VALUES (1, 2) MATCHING (A)"));
    }

    [Fact]
    public void UpdateOrInsert_WithoutMatching()
    {
        Assert.Equal(
            "update or insert into t (a, b)\nvalues (1, 2)",
            SqlFormatter.Format("UPDATE OR INSERT INTO T (A, B) VALUES (1, 2)"));
    }

    [Fact]
    public void UpdateOrInsert_MatchingAndReturning_EachOnOwnLine()
    {
        Assert.Equal(
            "update or insert into t (a)\nvalues (1)\nmatching (a)\nreturning id",
            SqlFormatter.Format("UPDATE OR INSERT INTO T (A) VALUES (1) MATCHING (A) RETURNING ID"));
    }

    [Fact]
    public void UpdateOrInsert_IsIdempotent()
    {
        var once = SqlFormatter.Format("UPDATE OR INSERT INTO T (A, B) VALUES (1, 2) MATCHING (A, B)");
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // The ordered significant-token + comment sequence (words upper-cased since the formatter
    // lowercases them; everything else exact) — the §0 quantity the formatter must preserve.
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
