using System.Linq;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

public class ExecutionActivityTests
{
    private static PerTableReadRow Row(string t, long seq = 0, long idx = 0, long ins = 0, long upd = 0, long del = 0)
        => new(t, seq, idx, ins, upd, del);

    [Fact]
    public void Build_Null_ReturnsEmpty() => Assert.Empty(ExecutionActivity.Build(null));

    [Fact]
    public void Build_OmitsReadOnlyTables()
    {
        // KARTOTEKA was only read → no entry. ZEST changed → the sole entry.
        var rows = new[]
        {
            Row("KARTOTEKA", seq: 28),
            Row("ZEST", seq: 197, ins: 14, upd: 28, del: 8),
        };

        var result = ExecutionActivity.Build(rows);

        var line = Assert.Single(result);
        Assert.Equal("ZEST", line.Table);
    }

    [Fact]
    public void Build_EmitsOnlyPresentChangeKinds_InInsertUpdateDeleteOrder()
    {
        var line = Assert.Single(ExecutionActivity.Build(new[] { Row("T", ins: 14, upd: 28, del: 8) }));

        Assert.Collection(line.Changes,
            c => { Assert.IsType<InsertChange>(c); Assert.Equal(14, c.Count); Assert.Equal("inserted", c.Verb); },
            c => { Assert.IsType<UpdateChange>(c); Assert.Equal(28, c.Count); Assert.Equal("updated", c.Verb); },
            c => { Assert.IsType<DeleteChange>(c); Assert.Equal(8, c.Count); Assert.Equal("deleted", c.Verb); });
    }

    [Fact]
    public void Build_DropsZeroCountKinds()
    {
        var line = Assert.Single(ExecutionActivity.Build(new[] { Row("T", upd: 3) }));
        var change = Assert.Single(line.Changes);
        Assert.IsType<UpdateChange>(change);
        Assert.Equal("3 updated", change.Text);
    }

    [Fact]
    public void Build_OrdersByTotalChangesDescThenName()
    {
        var rows = new[]
        {
            Row("SMALL", upd: 2),
            Row("BIG", ins: 50),
            Row("MID_B", ins: 5, del: 5),  // 10
            Row("MID_A", upd: 10),         // 10 — tie with MID_B, name wins
        };

        var order = ExecutionActivity.Build(rows).Select(l => l.Table).ToArray();

        Assert.Equal(new[] { "BIG", "MID_A", "MID_B", "SMALL" }, order);
    }

    [Fact]
    public void Build_ReadOnlyRun_ReturnsEmpty()
        => Assert.Empty(ExecutionActivity.Build(new[] { Row("A", seq: 100), Row("B", idx: 50) }));

    [Fact]
    public void BuildLogLines_IbExpertPhrasing_PerTablePerKind()
    {
        var lines = ExecutionActivity.BuildLogLines(new[] { Row("ORDERS", ins: 14, upd: 28, del: 8) });

        Assert.Equal(new[]
        {
            "14 inserted into ORDERS",
            "28 updated in ORDERS",
            "8 deleted from ORDERS",
        }, lines);
    }

    [Fact]
    public void BuildLogLines_OmitsReadOnlyTables_AndZeroKinds()
    {
        var lines = ExecutionActivity.BuildLogLines(new[]
        {
            Row("READ_ONLY", seq: 100),   // dropped — no changes
            Row("ORDERS", upd: 5),        // only the update line
        });

        var line = Assert.Single(lines);
        Assert.Equal("5 updated in ORDERS", line);
    }

    [Fact]
    public void BuildLogLines_NoChanges_ReturnsEmpty()
        => Assert.Empty(ExecutionActivity.BuildLogLines(new[] { Row("A", seq: 100) }));
}
