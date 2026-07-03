using System;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

public class ExecutionSummaryTests
{
    private static ExecutionSummary S(long ins, long upd, long del, bool measured = true, int? affected = null, long ms = 93)
        => new()
        {
            Inserts = ins,
            Updates = upd,
            Deletes = del,
            ChangesMeasured = measured,
            RecordsAffected = affected,
            Elapsed = TimeSpan.FromMilliseconds(ms),
        };

    private static ExecutionSummary D(long ins, long upd, long del, long read, bool changesMeasured = true, bool readsMeasured = true, int? affected = null, long ms = 93)
        => new()
        {
            Inserts = ins,
            Updates = upd,
            Deletes = del,
            RowsRead = read,
            ChangesMeasured = changesMeasured,
            ReadsMeasured = readsMeasured,
            RecordsAffected = affected,
            Elapsed = TimeSpan.FromMilliseconds(ms),
        };

    [Fact]
    public void Measured_AllThree_ListsInsertUpdateDelete()
        => Assert.Equal("inserted 8 · updated 16 · deleted 8 in 93 ms", S(8, 16, 8).BuildMessage());

    [Fact]
    public void Measured_OmitsZeroTerms()
        => Assert.Equal("updated 16 in 93 ms", S(0, 16, 0).BuildMessage());

    [Fact]
    public void Measured_LargeCount_PlainInteger_NoGrouping()
        => Assert.Equal("inserted 12345 in 93 ms", S(12_345, 0, 0).BuildMessage());

    [Fact]
    public void NoChangesMeasured_FallsBackToRecordsAffected()
        => Assert.Equal("42 rows affected in 5 ms", S(0, 0, 0, measured: false, affected: 42, ms: 5).BuildMessage());

    [Fact]
    public void MeasuredButNothingChanged_FallsBackToAffected_ZeroWhenNull()
    {
        // A procedure/block that measured a delta but changed nothing (e.g. pure computation).
        Assert.Equal("0 rows affected in 4 ms", S(0, 0, 0, measured: true, affected: 0, ms: 4).BuildMessage());
        Assert.Equal("0 rows affected in 4 ms", S(0, 0, 0, measured: true, affected: null, ms: 4).BuildMessage());
    }

    [Fact]
    public void TotalChanges_Sums()
        => Assert.Equal(32, S(8, 16, 8).TotalChanges);

    // ---- BuildDetailedMessage (Procedure/Function exec-info bar) --------------------
    [Fact]
    public void Detailed_ChangesAndReads_MultiLine()
    {
        var msg = D(ins: 8, upd: 16, del: 8, read: 20_552).BuildDetailedMessage();
        Assert.Equal(
            "Executed in 93 ms\n\n8 rows inserted\n16 rows updated\n8 rows deleted\n\n20552 rows read",
            msg);
    }

    [Fact]
    public void Detailed_ReadsOnly_SaysNoModifications()
    {
        // Significant work (reads) but nothing modified — never the misleading "0 rows affected".
        var msg = D(ins: 0, upd: 0, del: 0, read: 20_552, ms: 21).BuildDetailedMessage();
        Assert.Equal("Executed in 21 ms\n\n20552 rows read\n\nNo data modifications detected.", msg);
    }

    [Fact]
    public void Detailed_OmitsZeroChangeTerms_AndSingularizes()
    {
        // One insert IS a change → no "No modifications" line; singular "1 row inserted".
        var msg = D(ins: 1, upd: 0, del: 0, read: 0, ms: 5).BuildDetailedMessage();
        Assert.Equal("Executed in 5 ms\n\n1 row inserted", msg);
    }

    [Fact]
    public void Detailed_NotMeasured_FallsBackToAffectedLine()
    {
        var msg = D(0, 0, 0, 0, changesMeasured: false, readsMeasured: false, affected: 42, ms: 5).BuildDetailedMessage();
        Assert.Equal("Executed in 5 ms · 42 rows affected", msg);
    }

    [Fact]
    public void Detailed_ChangesWithoutReadsMeasured_OmitsReadLine()
    {
        var msg = D(ins: 0, upd: 3, del: 0, read: 0, readsMeasured: false, ms: 10).BuildDetailedMessage();
        Assert.Equal("Executed in 10 ms\n\n3 rows updated", msg);
    }

    // ---- BuildCompactLine (collapsed exec-info Expander header) ---------------------
    [Fact]
    public void Compact_ChangesAndReads_SingleLineDotSeparated()
    {
        var line = D(ins: 14, upd: 28, del: 8, read: 376, ms: 54).BuildCompactLine();
        Assert.Equal("Executed in 54 ms · 14 inserted · 28 updated · 8 deleted · 376 read", line);
    }

    [Fact]
    public void Compact_OmitsZeroChangeTerms()
    {
        var line = D(ins: 0, upd: 3, del: 0, read: 0, readsMeasured: false, ms: 10).BuildCompactLine();
        Assert.Equal("Executed in 10 ms · 3 updated", line);
    }

    [Fact]
    public void Compact_ReadsOnly_ShowsReadTerm()
    {
        var line = D(ins: 0, upd: 0, del: 0, read: 20_552, ms: 21).BuildCompactLine();
        Assert.Equal("Executed in 21 ms · 20552 read", line);
    }

    [Fact]
    public void Compact_MeasuredButNoWork_JustTime()
    {
        var line = D(ins: 0, upd: 0, del: 0, read: 0, ms: 4).BuildCompactLine();
        Assert.Equal("Executed in 4 ms", line);
    }

    [Fact]
    public void Compact_NotMeasured_FallsBackToAffected()
    {
        var line = D(0, 0, 0, 0, changesMeasured: false, readsMeasured: false, affected: 42, ms: 5).BuildCompactLine();
        Assert.Equal("Executed in 5 ms · 42 rows affected", line);
    }

    [Fact]
    public void Compact_IsSingleLine_NoNewlines()
    {
        var line = D(ins: 1, upd: 2, del: 3, read: 4).BuildCompactLine();
        Assert.DoesNotContain("\n", line);
    }
}
