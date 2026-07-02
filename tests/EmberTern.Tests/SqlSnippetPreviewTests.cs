using EmberTern.App.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlSnippetPreviewTests
{
    [Fact]
    public void ShortText_Unchanged()
    {
        const string sql = "SELECT * FROM CUSTOMERS";
        Assert.Equal(sql, SqlSnippetDropTarget.TruncatePreview(sql, maxLines: 12, maxChars: 500));
    }

    [Fact]
    public void Empty_Unchanged()
        => Assert.Equal("", SqlSnippetDropTarget.TruncatePreview("", 12, 500));

    [Fact]
    public void AtLineLimit_NotTruncated()
    {
        var sql = string.Join("\n", "abc", "def", "ghi"); // 3 lines
        Assert.Equal(sql, SqlSnippetDropTarget.TruncatePreview(sql, maxLines: 3, maxChars: 500));
    }

    [Fact]
    public void OverLineLimit_KeepsFirstLinesAndAppendsEllipsis()
    {
        var sql = string.Join("\n", "l1", "l2", "l3", "l4", "l5");
        var result = SqlSnippetDropTarget.TruncatePreview(sql, maxLines: 3, maxChars: 500);
        Assert.Equal("l1\nl2\nl3\n…", result);
    }

    [Fact]
    public void OverCharLimit_CutsAndAppendsEllipsis()
    {
        var sql = new string('x', 50);
        var result = SqlSnippetDropTarget.TruncatePreview(sql, maxLines: 12, maxChars: 10);
        Assert.EndsWith("…", result);
        // 10 chars of body (+ the "\n…" marker); never longer than the cap + marker.
        Assert.True(result.Length <= 10 + 2);
    }

    [Fact]
    public void LineLimitAppliedBeforeCharLimit()
    {
        // 5 short lines: line cap (2) fires; the kept text is well under the char cap.
        var sql = string.Join("\n", "aa", "bb", "cc", "dd", "ee");
        var result = SqlSnippetDropTarget.TruncatePreview(sql, maxLines: 2, maxChars: 500);
        Assert.Equal("aa\nbb\n…", result);
    }
}
