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
}
