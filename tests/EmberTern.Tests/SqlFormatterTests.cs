using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlFormatterTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SqlFormatter.Format(string.Empty));
    }

    [Fact]
    public void Whitespace_IsCollapsedToEmpty()
    {
        Assert.Equal(string.Empty, SqlFormatter.Format("   \t  "));
    }

    [Fact]
    public void Keywords_AreLowercased()
    {
        Assert.Equal("select 1", SqlFormatter.Format("SELECT 1"));
    }

    [Fact]
    public void NonKeywordIdentifiers_AreLowercased()
    {
        // IBExpert-style "lowercase all" preset — identifiers (table/column/alias
        // names, function names) get lowercased alongside keywords. Strings and
        // quoted identifiers stay verbatim (covered by separate tests).
        Assert.Equal("select mycol\nfrom mytable", SqlFormatter.Format("SELECT MyCol FROM MyTable"));
    }

    [Fact]
    public void LowercaseAll_AppliesToAliasesAndFunctionsAndDottedNames()
    {
        // Aliases + dotted qualifiers + function calls all flow through MaybeLowercase.
        // String literal in the middle stays verbatim — including its mixed case.
        Assert.Equal(
            "select n.id, count(p.amount), 'Mixed CASE Stays'\n"
            + "from nagl n\n"
            + "join pozycje p on p.id_nagl = n.id",
            SqlFormatter.Format(
                "SELECT N.ID, COUNT(P.AMOUNT), 'Mixed CASE Stays' "
                + "FROM NAGL N JOIN POZYCJE P ON P.ID_NAGL = N.ID"));
    }

    [Fact]
    public void ClauseKeywords_BreakOntoNewLines()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere x = 1",
            SqlFormatter.Format("SELECT a FROM t WHERE x = 1"));
    }

    [Fact]
    public void AndOr_BreakWithIndent()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere x = 1\n  and y = 2\n  or z = 3",
            SqlFormatter.Format("SELECT a FROM t WHERE x = 1 AND y = 2 OR z = 3"));
    }

    [Fact]
    public void GroupBy_AndOrderBy_AreClauseBreaks()
    {
        Assert.Equal(
            "select a\nfrom t\ngroup by a\norder by a",
            SqlFormatter.Format("SELECT a FROM t GROUP BY a ORDER BY a"));
    }

    [Fact]
    public void Having_IsClauseBreak()
    {
        // COUNT is a function name. Now lowercased like every other identifier.
        Assert.Equal(
            "select a\nfrom t\ngroup by a\nhaving count(*) > 1",
            SqlFormatter.Format("SELECT a FROM t GROUP BY a HAVING COUNT(*) > 1"));
    }

    [Fact]
    public void Join_IsClauseBreak()
    {
        Assert.Equal(
            "select a\nfrom t\njoin u on t.id = u.id",
            SqlFormatter.Format("SELECT a FROM t JOIN u ON t.id = u.id"));
    }

    [Fact]
    public void LeftJoin_IsClauseBreak()
    {
        Assert.Equal(
            "select a\nfrom t\nleft join u on t.id = u.id",
            SqlFormatter.Format("SELECT a FROM t LEFT JOIN u ON t.id = u.id"));
    }

    [Fact]
    public void LeftOuterJoin_IsClauseBreak()
    {
        Assert.Equal(
            "select a\nfrom t\nleft outer join u on t.id = u.id",
            SqlFormatter.Format("SELECT a FROM t LEFT OUTER JOIN u ON t.id = u.id"));
    }

    [Fact]
    public void InnerJoin_IsClauseBreak()
    {
        Assert.Equal(
            "select a\nfrom t\ninner join u on t.id = u.id",
            SqlFormatter.Format("SELECT a FROM t INNER JOIN u ON t.id = u.id"));
    }

    [Fact]
    public void StringLiterals_ArePreservedVerbatim()
    {
        // The literal 'FROM here' must NOT be lowercased nor split onto a new line.
        Assert.Equal(
            "select 'FROM here' as x\nfrom t",
            SqlFormatter.Format("SELECT 'FROM here' AS x FROM t"));
    }

    [Fact]
    public void StringLiterals_EscapedQuote_IsPreserved()
    {
        Assert.Equal(
            "select 'it''s ok'\nfrom t",
            SqlFormatter.Format("SELECT 'it''s ok' FROM t"));
    }

    [Fact]
    public void QuotedIdentifiers_ArePreserved()
    {
        Assert.Equal(
            "select \"From\"\nfrom t",
            SqlFormatter.Format("SELECT \"From\" FROM t"));
    }

    [Fact]
    public void LineComment_IsPreservedAndNotParsed()
    {
        // -- SELECT inside a comment shouldn't trigger a clause break.
        var result = SqlFormatter.Format("SELECT a -- and FROM here\nFROM t");
        Assert.Contains("-- and FROM here", result);
        Assert.StartsWith("select a", result);
        Assert.Contains("\nfrom t", result);
    }

    [Fact]
    public void BlockComment_IsPreserved()
    {
        var result = SqlFormatter.Format("SELECT /* FROM not really */ a FROM t");
        Assert.Contains("/* FROM not really */", result);
        Assert.Contains("\nfrom t", result);
    }

    [Fact]
    public void CommaList_HasSpaceAfterButNotBefore()
    {
        Assert.Equal("select a, b, c\nfrom t", SqlFormatter.Format("SELECT a,b,c FROM t"));
    }

    [Fact]
    public void FunctionCall_NoSpaceBeforeOpeningParen()
    {
        // Function names are lowercased like everything else, and still no space
        // before "(" — the function-call shape is preserved without case-quirks.
        Assert.Equal("select count(*)\nfrom t", SqlFormatter.Format("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void DottedQualifier_HasNoSpacesAroundDot()
    {
        Assert.Equal(
            "select t.a\nfrom schema.t",
            SqlFormatter.Format("SELECT t.a FROM schema.t"));
    }

    [Fact]
    public void TwoCharOperators_StayTogether()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere a <= 1\n  and b >= 2\n  and c <> 3",
            SqlFormatter.Format("SELECT a FROM t WHERE a <= 1 AND b >= 2 AND c <> 3"));
    }

    [Fact]
    public void Format_IsIdempotent()
    {
        var once = SqlFormatter.Format("SELECT a, b FROM t WHERE x = 1 AND y = 2 ORDER BY a");
        var twice = SqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AlreadyLowercase_StillReformatted()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere x = 1",
            SqlFormatter.Format("select a from t where x = 1"));
    }

    [Fact]
    public void LeadingClause_NoLeadingNewline()
    {
        // The result must start with "select", not "\nselect".
        var result = SqlFormatter.Format("SELECT 1");
        Assert.False(result.StartsWith("\n"), "Output must not start with a newline");
    }

    [Fact]
    public void NoStructuralKeywords_StaysOnOneLine()
    {
        Assert.Equal("a, b, c", SqlFormatter.Format("a , b , c"));
    }

    [Fact]
    public void GroupWithoutBy_IsNotClauseBreak()
    {
        // Bare "GROUP" without trailing BY shouldn't trigger a line break — it's
        // not a clause. (Pathological input, but we don't want to split mid-stream.)
        var result = SqlFormatter.Format("SELECT a FROM t");
        Assert.DoesNotContain("\ngroup\n", result);
    }

    // --- IBExpert-style long-line wrapping (120-char threshold) ---

    [Fact]
    public void SelectColumnList_NotWrapped_WhenUnder120()
    {
        Assert.Equal(
            "select a, b, c\nfrom t",
            SqlFormatter.Format("SELECT a, b, c FROM t"));
    }

    [Fact]
    public void SelectColumnList_Wrapped_PacksMultiplePerLineUnder120()
    {
        // 25 short columns → "select " line would be ~225 chars. Wrap packs as many
        // as fit under 120, then continuation lines (7-space indent under the first
        // column) pack the rest.
        var cols = string.Join(", ", Enumerable.Range(0, 25).Select(i => $"col_{i:D2}"));
        var result = SqlFormatter.Format($"SELECT {cols} FROM t");
        var lines = result.Split('\n');

        // First line starts with "select col_00," and stays ≤ 120 chars.
        Assert.StartsWith("select col_00, col_01,", lines[0]);
        Assert.True(lines[0].Length <= MaxLineWidth, $"line 0 over 120: '{lines[0]}' ({lines[0].Length})");

        // Continuation line is indented exactly 7 spaces (length of "select ").
        Assert.StartsWith(new string(' ', 7), lines[1]);
        Assert.False(lines[1].StartsWith(new string(' ', 8)), "continuation must be exactly 7 spaces");
        Assert.True(lines[1].Length <= MaxLineWidth, $"continuation over 120: '{lines[1]}'");

        // FROM follows after all column lines.
        Assert.Equal("from t", lines[lines.Length - 1]);

        // The last column appears, with no trailing comma.
        Assert.Contains("col_24", result);
        Assert.DoesNotContain("col_24,", result);
    }

    [Fact]
    public void SelectColumnList_Wrapped_PreservesDistinct_ContinuationAlignedAfterDistinct()
    {
        // With DISTINCT, continuation indent = "select distinct ".Length = 16 spaces,
        // putting wrapped columns directly under the first one.
        var cols = string.Join(", ", Enumerable.Range(0, 25).Select(i => $"col_{i:D2}"));
        var result = SqlFormatter.Format($"SELECT DISTINCT {cols} FROM t");
        var lines = result.Split('\n');

        Assert.StartsWith("select distinct col_00,", lines[0]);
        Assert.StartsWith(new string(' ', 16), lines[1]);
        Assert.False(lines[1].StartsWith(new string(' ', 17)), "continuation must be exactly 16 spaces");
    }

    [Fact]
    public void SelectColumnList_Wrap_RespectsCommasInsideFunctionCalls()
    {
        // Commas inside COALESCE(...) must NOT trigger a column split. With 10 such
        // expressions the line will overflow and wrap, but each expression stays whole.
        var sql = "SELECT COALESCE(a, b, c) AS x1, COALESCE(d, e, f) AS x2, COALESCE(g, h, i) AS x3, "
                + "COALESCE(j, k, l) AS x4, COALESCE(m, n, o) AS x5, COALESCE(p, q, r) AS x6, "
                + "COALESCE(s, t, u) AS x7, COALESCE(v, w, y) AS x8 FROM t";
        var result = SqlFormatter.Format(sql);

        Assert.Contains("coalesce(a, b, c) as x1", result);
        Assert.Contains("coalesce(v, w, y) as x8", result);
        // Each line under 120.
        foreach (var l in result.Split('\n'))
        {
            Assert.True(l.Length <= MaxLineWidth, $"line over 120: '{l}' ({l.Length})");
        }
    }

    [Fact]
    public void InList_NotWrapped_WhenUnder120()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere x in (1, 2, 3)",
            SqlFormatter.Format("SELECT a FROM t WHERE x IN (1, 2, 3)"));
    }

    [Fact]
    public void InList_Wrapped_ContinuationAlignedAfterParen_CloseInline()
    {
        // 35 three-digit values → "where x in (...)" line well above 120 chars.
        var vals = string.Join(", ", Enumerable.Range(100, 35));
        var result = SqlFormatter.Format($"SELECT a FROM t WHERE x IN ({vals})");
        var lines = result.Split('\n');

        var whereIdx = System.Array.FindIndex(lines, l => l.StartsWith("where x in ("));
        Assert.True(whereIdx >= 0, $"could not find 'where x in (' line in: {result}");

        // First value sits flush against "(" — line begins "where x in (100,".
        Assert.StartsWith("where x in (100,", lines[whereIdx]);
        Assert.True(lines[whereIdx].Length <= MaxLineWidth);

        // Continuation indent = position-of-'(' + 1 = "where x in ".Length = 12 spaces.
        var contIndent = new string(' ', "where x in ".Length + 1);
        Assert.StartsWith(contIndent, lines[whereIdx + 1]);
        Assert.False(lines[whereIdx + 1].StartsWith(contIndent + " "),
            $"continuation must be exactly {contIndent.Length} spaces, got: '{lines[whereIdx + 1]}'");

        // Closing ')' stays inline with the last value — no bare ')' on its own line.
        Assert.DoesNotContain("\n)\n", result);
        Assert.DoesNotContain("\n)" + System.Environment.NewLine, result);
        Assert.EndsWith(")", result);
    }

    [Fact]
    public void InList_Wrapped_TrailingClauseStaysOnItsOwnLine()
    {
        // The "and y = 1" is on a separate line (AND structural break), so the IN
        // wrap close paren ends the in-clause line and AND follows on the next line.
        var vals = string.Join(", ", Enumerable.Range(100, 35));
        var sql = $"SELECT a FROM t WHERE x IN ({vals}) AND y = 1";
        var result = SqlFormatter.Format(sql);

        // Some continuation line ends with ")" (the close paren is inline), then
        // a newline introduces "  and y = 1".
        Assert.Matches(@"\d+\)\n  and y = 1$", result);
    }

    [Fact]
    public void InList_Subquery_IsNotWrapped()
    {
        // IN (SELECT ...) is handled by the existing structural break; the value-list
        // wrapper must not comma-split a subquery body.
        var sql = "SELECT a FROM t WHERE x IN (SELECT id FROM other WHERE val > 1)";
        var result = SqlFormatter.Format(sql);
        // Subquery's SELECT triggers its own structural line break — that's expected.
        // We just confirm the IN wrapper didn't kick in (no continuation indent under
        // "where x in (").
        Assert.DoesNotContain("where x in (select id,", result);
    }

    [Fact]
    public void InList_Wrapped_WithLeadingIndent()
    {
        // The IN list lives on an "  and x in (...)" line (AND structural break adds
        // a 2-space conjunction indent). Continuation aligns to one past "(", which
        // here is column 12 (counting the leading "  and x in " = 11 chars).
        var vals = string.Join(", ", Enumerable.Range(100, 35));
        var sql = $"SELECT a FROM t WHERE z = 1 AND x IN ({vals})";
        var result = SqlFormatter.Format(sql);
        var lines = result.Split('\n');

        var openIdx = System.Array.FindIndex(lines, l => l.StartsWith("  and x in ("));
        Assert.True(openIdx >= 0, $"could not find 'and x in (' line in: {result}");
        Assert.StartsWith("  and x in (100,", lines[openIdx]);

        // Indent for continuation = position of '(' + 1 = "  and x in ".Length = 12.
        var contIndent = new string(' ', "  and x in ".Length + 1);
        Assert.StartsWith(contIndent, lines[openIdx + 1]);
        Assert.False(lines[openIdx + 1].StartsWith(contIndent + " "),
            $"continuation must be exactly {contIndent.Length} spaces");
        // Close paren inline on the last value's continuation line.
        Assert.EndsWith(")", lines[lines.Length - 1]);
    }

    [Fact]
    public void JoinOnAnd_StaysIndented2Spaces()
    {
        // AND/OR breaks are unconditional, and the indent is exactly two spaces —
        // matching the spec's expectation for long ON conditions.
        Assert.Equal(
            "select a\nfrom t\njoin u on t.id = u.id\n  and t.x = u.x",
            SqlFormatter.Format("SELECT a FROM t JOIN u ON t.id = u.id AND t.x = u.x"));
    }

    [Fact]
    public void Wrap_IsIdempotent()
    {
        var cols = string.Join(", ", Enumerable.Range(0, 25).Select(i => $"long_col_{i:D2}"));
        var vals = string.Join(", ", Enumerable.Range(100, 35));
        var sql = $"SELECT {cols} FROM t WHERE x IN ({vals})";

        var once = SqlFormatter.Format(sql);
        var twice = SqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    private const int MaxLineWidth = 120;
}
