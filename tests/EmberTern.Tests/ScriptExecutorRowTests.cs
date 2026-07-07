using System;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Script Executor results-grid row presentation (headless — no VM services).</summary>
public class ScriptExecutorRowTests
{
    private static ScriptResultRowViewModel Row(
        ScriptStatementKind kind, bool success, int? recordsAffected = null, int? rowCount = null,
        string? error = null, int index = 0, int offset = 0, int length = 3, string text = "select 1")
        => new(new ScriptStatementResult(index, text, kind, success, recordsAffected, rowCount, TimeSpan.FromMilliseconds(12), error),
               offset, length);

    [Fact]
    public void SuccessRow_ShowsOkAndNotFailed()
    {
        var row = Row(ScriptStatementKind.Dml, success: true, recordsAffected: 5);
        Assert.Equal(UiStrings.BatchResultOk, row.Result);
        Assert.False(row.IsFailed);
        Assert.Equal(1, row.Line);           // Index 0 → display line 1
        Assert.Equal("5", row.RowsText);     // RecordsAffected surfaced
        Assert.Equal(string.Empty, row.Error);
    }

    [Fact]
    public void FailedRow_ShowsFailedAndError()
    {
        var row = Row(ScriptStatementKind.Ddl, success: false, error: "boom");
        Assert.Equal(UiStrings.BatchResultFailed, row.Result);
        Assert.True(row.IsFailed);
        Assert.Equal("boom", row.Error);
    }

    [Fact]
    public void RowsText_PrefersRowCountThenRecordsAffected_ElseEmpty()
    {
        Assert.Equal("42", Row(ScriptStatementKind.Select, true, recordsAffected: 1, rowCount: 42).RowsText);
        Assert.Equal("7", Row(ScriptStatementKind.Dml, true, recordsAffected: 7).RowsText);
        Assert.Equal(string.Empty, Row(ScriptStatementKind.Ddl, true).RowsText);
    }

    [Theory]
    [InlineData(ScriptStatementKind.Ddl, "DDL")]
    [InlineData(ScriptStatementKind.Dml, "DML")]
    [InlineData(ScriptStatementKind.Select, "SELECT")]
    [InlineData(ScriptStatementKind.ExecuteProcedure, "EXECUTE PROCEDURE")]
    [InlineData(ScriptStatementKind.ExecuteBlock, "EXECUTE BLOCK")]
    public void TypeText_MapsKindToLabel(ScriptStatementKind kind, string expected)
        => Assert.Equal(expected, Row(kind, true).TypeText);

    [Fact]
    public void Statement_IsFlattenedAndElided()
    {
        var multiline = "update t\n  set x = 1\n  where id = 5";
        var row = Row(ScriptStatementKind.Dml, true, text: multiline);
        Assert.DoesNotContain("\n", row.Statement);
        Assert.Equal("update t set x = 1 where id = 5", row.Statement);
    }

    [Fact]
    public void SourceRange_NegativeOffsetMeansNoRange()
    {
        Assert.False(Row(ScriptStatementKind.Dml, true, offset: -1).HasSourceRange);
        var located = Row(ScriptStatementKind.Dml, true, offset: 10, length: 4);
        Assert.True(located.HasSourceRange);
        Assert.Equal(10, located.SourceOffset);
        Assert.Equal(4, located.SourceLength);
    }

    [Fact]
    public void FormatDuration_UsesMsUnderOneSecond_ElseSeconds()
    {
        Assert.EndsWith("ms", ScriptExecutorTabViewModel.FormatDuration(TimeSpan.FromMilliseconds(250)));
        Assert.EndsWith("s", ScriptExecutorTabViewModel.FormatDuration(TimeSpan.FromSeconds(2.5)));
    }
}
