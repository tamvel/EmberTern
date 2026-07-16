using System;
using System.Collections.Generic;
using EmberTern.Core.Trace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The pure, presentation-only SQL value inliner (V1.1). Verifies per-type formatting, the
/// count-mismatch fallback, placeholders inside literals/comments staying untouched, and that
/// BLOB/array params keep their placeholder. Never executes anything.
/// </summary>
public class TraceSqlInlinerTests
{
    private static RawTraceParam P(int i, string type, string? value) => new(i, type, value);

    [Fact]
    public void Inline_Integer_SubstitutesVerbatim()
    {
        var sql = "SELECT * FROM NAGL WHERE ID_NAGL = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "10036") });
        Assert.Equal("SELECT * FROM NAGL WHERE ID_NAGL = 10036", result);
    }

    [Fact]
    public void Inline_MultipleParams_InPositionalOrder()
    {
        var sql = "SELECT * FROM T WHERE A = ? AND B = ? AND C = ?";
        var result = TraceSqlInliner.Inline(sql, new[]
        {
            P(0, "integer", "1"),
            P(1, "varchar(50)", "abc"),
            P(2, "bigint", "999"),
        });
        Assert.Equal("SELECT * FROM T WHERE A = 1 AND B = 'abc' AND C = 999", result);
    }

    [Theory]
    [InlineData("varchar(50)", "O'Brien", "'O''Brien'")]        // char/text → quoted, ' doubled
    [InlineData("char(3)", "abc", "'abc'")]
    [InlineData("date", "2026-01-01", "'2026-01-01'")]
    [InlineData("timestamp", "2026-01-01 10:30:00", "'2026-01-01 10:30:00'")]
    // Firebird's trace emits the ISO 'T' separator; the inlined literal must use a space
    // (Firebird rejects 'T'), so the copied SQL is runnable.
    [InlineData("timestamp", "1899-12-30T00:00:00", "'1899-12-30 00:00:00'")]
    [InlineData("timestamp", "2026-01-01T10:30:00.0000", "'2026-01-01 10:30:00.0000'")]
    [InlineData("time", "10:30:00", "'10:30:00'")]
    [InlineData("integer", "42", "42")]
    [InlineData("bigint", "9000000000", "9000000000")]
    [InlineData("numeric(15,2)", "123.45", "123.45")]
    [InlineData("boolean", "TRUE", "TRUE")]
    public void Inline_FormatsPerType(string type, string value, string expectedLiteral)
    {
        var result = TraceSqlInliner.Inline("SELECT ?", new[] { P(0, type, value) });
        Assert.Equal("SELECT " + expectedLiteral, result);
    }

    [Fact]
    public void Inline_Null_BecomesNullKeyword_RegardlessOfType()
    {
        var result = TraceSqlInliner.Inline("WHERE X = ?", new[] { P(0, "varchar(10)", null) });
        Assert.Equal("WHERE X = NULL", result);
    }

    [Fact]
    public void Inline_Blob_LeavesPlaceholder()
    {
        var result = TraceSqlInliner.Inline("UPDATE T SET DATA = ? WHERE ID = ?",
            new[] { P(0, "blob subtype 1", "..."), P(1, "integer", "5") });
        Assert.Equal("UPDATE T SET DATA = ? WHERE ID = 5", result);
    }

    [Fact]
    public void Inline_PlaceholderInsideStringLiteral_IsNotSubstituted()
    {
        // The literal '?' is data; only the trailing bound '?' is a parameter.
        var sql = "SELECT '? literal' FROM T WHERE X = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "7") });
        Assert.Equal("SELECT '? literal' FROM T WHERE X = 7", result);
    }

    [Fact]
    public void Inline_PlaceholderInsideComment_IsNotSubstituted()
    {
        var sql = "SELECT X /* ? not a param */ FROM T WHERE X = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "7") });
        Assert.Equal("SELECT X /* ? not a param */ FROM T WHERE X = 7", result);
    }

    [Fact]
    public void Inline_CountMismatch_ReturnsSourceUnchanged()
    {
        // Truncated SQL (one '?') but two params → faithful source, no substitution.
        var sql = "SELECT * FROM T WHERE A = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "1"), P(1, "integer", "2") });
        Assert.Equal(sql, result);
    }

    [Fact]
    public void Inline_NoParams_ReturnsSourceUnchanged()
    {
        var sql = "SELECT * FROM T WHERE A = ?";
        Assert.Equal(sql, TraceSqlInliner.Inline(sql, Array.Empty<RawTraceParam>()));
        Assert.Equal(sql, TraceSqlInliner.Inline(sql, null));
    }

    [Fact]
    public void Inline_NoPlaceholders_ReturnsSourceUnchanged()
    {
        var sql = "SELECT * FROM T";
        Assert.Equal(sql, TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "1") }));
    }

    [Fact]
    public void Inline_EmptyOrNullSql_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TraceSqlInliner.Inline(null, new[] { P(0, "integer", "1") }));
        Assert.Equal(string.Empty, TraceSqlInliner.Inline(string.Empty, new[] { P(0, "integer", "1") }));
    }

    // §0 (Etap 1 lexer migration): only the positional '?' markers are substituted — named
    // ':name' / '@name' parameters and everything else pass through byte-for-byte.
    [Fact]
    public void Inline_NamedParameters_AreNeverSubstituted()
    {
        // One positional '?' (count matches the one supplied value); :d and @e stay verbatim.
        var sql = "SELECT * FROM T WHERE a = ? AND b = :d AND c = @e";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "1") });
        Assert.Equal("SELECT * FROM T WHERE a = 1 AND b = :d AND c = @e", result);
    }

    [Fact]
    public void Inline_QuestionMarkInsideQuotedIdentifier_IsNotSubstituted()
    {
        // The '?' inside the "we?rd" quoted identifier is data; only the trailing bound '?' is a param.
        var sql = "SELECT \"we?rd\" FROM T WHERE X = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "9") });
        Assert.Equal("SELECT \"we?rd\" FROM T WHERE X = 9", result);
    }

    [Fact]
    public void Inline_CountMismatch_PreservesLiteralsCommentsAndParamsVerbatim()
    {
        // Count mismatch (two params, one real '?') → faithful source, nothing altered anywhere,
        // including the '?' inside the string literal and the block comment.
        var sql = "SELECT '? in string' /* ? in comment */, :n, @m FROM T WHERE X = ?";
        var result = TraceSqlInliner.Inline(sql, new[] { P(0, "integer", "1"), P(1, "integer", "2") });
        Assert.Equal(sql, result);
    }
}
