using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.Core.Trace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Activity Monitor engine layer (M2.5): fingerprinter, grouper, bounded ring
/// buffer, and the streaming accumulator (whose output must be identical to a batch
/// parse — the cross-message-folding guarantee the live service relies on).
/// </summary>
public class TraceEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 3, 19, 10, 0, TimeSpan.Zero);

    private static TraceEvent Ev(long id, TraceEventKind kind, long? tx = null, string token = "CTX",
        string? sql = null, int durMs = 0, int startMs = 0)
        => new()
        {
            Id = id,
            Sequence = id,
            Kind = kind,
            StartTime = T0.AddMilliseconds(startMs),
            Duration = TimeSpan.FromMilliseconds(durMs),
            TransactionId = tx,
            ContextToken = token,
            Sql = sql,
        };

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Trace", name));

    // ---------------------------------------------------------------- fingerprinter

    [Fact]
    public void Fingerprint_IgnoresWhitespaceCaseAndParamValues()
    {
        var a = TraceStatementFingerprinter.Fingerprint("SELECT WARTOSC FROM KONFFIRMY WHERE (NUMERPARAM = ?)");
        var b = TraceStatementFingerprinter.Fingerprint("select   wartosc\nfrom konffirmy\nwhere ( numerparam = 3234 )");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_StringLiteralsAndNumbersBecomePlaceholders()
    {
        var a = TraceStatementFingerprinter.Fingerprint("WHERE NAME = 'Alice' AND ID IN (1, 2, 3)");
        var b = TraceStatementFingerprinter.Fingerprint("where name = 'Bob' and id in (?, ?, ?)");
        Assert.Equal(a, b);
        Assert.DoesNotContain("Alice", a);
    }

    [Fact]
    public void Fingerprint_DropsComments()
        => Assert.Equal(
            TraceStatementFingerprinter.Fingerprint("SELECT A FROM T"),
            TraceStatementFingerprinter.Fingerprint("SELECT /* pick */ A -- col\nFROM T"));

    [Fact]
    public void Fingerprint_DifferentColumnsStayDistinct()
        => Assert.NotEqual(
            TraceStatementFingerprinter.Fingerprint("SELECT WARTOSC FROM T"),
            TraceStatementFingerprinter.Fingerprint("SELECT T.WARTOSC FROM T"));

    [Fact]
    public void Fingerprint_QuotedIdentifiersAreCaseSensitiveButKeywordsAreNot()
    {
        Assert.Equal(
            TraceStatementFingerprinter.Fingerprint("select \"Col\" from t"),
            TraceStatementFingerprinter.Fingerprint("SELECT \"Col\" FROM T"));
        Assert.NotEqual(
            TraceStatementFingerprinter.Fingerprint("SELECT \"Col\" FROM T"),
            TraceStatementFingerprinter.Fingerprint("SELECT \"COL\" FROM T"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fingerprint_BlankInputIsEmpty(string? sql)
        => Assert.Equal(string.Empty, TraceStatementFingerprinter.Fingerprint(sql));

    // ---------------------------------------------------------------- group by transaction

    [Fact]
    public void GroupByTransaction_KeepsFirstAppearanceOrderAndAggregates()
    {
        var events = new[]
        {
            Ev(1, TraceEventKind.Statement, tx: 100, durMs: 5, startMs: 0),
            Ev(2, TraceEventKind.Trigger,   tx: 200, durMs: 1, startMs: 10),
            Ev(3, TraceEventKind.Statement, tx: 100, durMs: 3, startMs: 20),
        };

        var groups = TraceEventGrouper.GroupByTransaction(events);

        Assert.Equal(2, groups.Count);
        Assert.Equal(100, groups[0].TransactionId);   // 100 appeared first
        Assert.Equal(200, groups[1].TransactionId);
        Assert.Equal(2, groups[0].EventCount);
        Assert.Equal(2, groups[0].StatementCount);
        Assert.Equal(TimeSpan.FromMilliseconds(8), groups[0].TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(23), groups[0].Span); // start 0 .. last SpanEnd (start 20 + 3ms dur)
    }

    [Fact]
    public void GroupByTransaction_NullTransactionFormsTrailingGroup()
    {
        var events = new[]
        {
            Ev(1, TraceEventKind.Statement, tx: 5),
            Ev(2, TraceEventKind.System, tx: null),
        };
        var groups = TraceEventGrouper.GroupByTransaction(events);
        Assert.Equal(2, groups.Count);
        Assert.Equal(5, groups[0].TransactionId);
        Assert.Null(groups[1].TransactionId); // the null-tx (system) event trails
    }

    // ---------------------------------------------------------------- group by fingerprint

    [Fact]
    public void GroupByFingerprint_CollapsesIdenticalQueries_WithAggregates()
    {
        var events = new[]
        {
            Ev(1, TraceEventKind.Statement, sql: "SELECT A FROM T WHERE ID = 1", durMs: 10),
            Ev(2, TraceEventKind.Statement, sql: "select a from t where id = 2", durMs: 30),
            Ev(3, TraceEventKind.Statement, sql: "SELECT A FROM T WHERE ID = 999", durMs: 20),
            Ev(4, TraceEventKind.Procedure, sql: null, durMs: 100), // excluded (no SQL)
        };

        var group = Assert.Single(TraceEventGrouper.GroupByFingerprint(events));
        Assert.Equal(3, group.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(60), group.TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(20), group.AverageDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(30), group.MaxDuration);
    }

    [Fact]
    public void GroupByFingerprint_OrdersByTotalDurationDescending()
    {
        var events = new[]
        {
            Ev(1, TraceEventKind.Statement, sql: "SELECT A FROM T", durMs: 5),
            Ev(2, TraceEventKind.Statement, sql: "SELECT B FROM U", durMs: 40),
            Ev(3, TraceEventKind.Statement, sql: "SELECT B FROM U", durMs: 40),
        };
        var groups = TraceEventGrouper.GroupByFingerprint(events);
        Assert.Equal(2, groups.Count);
        Assert.Contains("U", groups[0].RepresentativeSql); // the 80ms group is first
    }

    // ---------------------------------------------------------------- call hierarchy

    [Fact]
    public void WithCallHierarchy_NestsRoutinesUnderTheCurrentStatement()
    {
        var events = new[]
        {
            Ev(10, TraceEventKind.Statement, token: "A"),
            Ev(11, TraceEventKind.Trigger,   token: "A"),
            Ev(12, TraceEventKind.Function,  token: "A"),
            Ev(20, TraceEventKind.Statement, token: "A"), // new parent
            Ev(21, TraceEventKind.Procedure, token: "A"),
        };

        var h = TraceEventGrouper.WithCallHierarchy(events);

        Assert.Null(h[0].ParentEventId); Assert.Equal(0, h[0].Depth);
        Assert.Equal(10, h[1].ParentEventId); Assert.Equal(1, h[1].Depth);
        Assert.Equal(10, h[2].ParentEventId); Assert.Equal(1, h[2].Depth);
        Assert.Null(h[3].ParentEventId);      // the second statement resets
        Assert.Equal(20, h[4].ParentEventId); Assert.Equal(1, h[4].Depth);
    }

    [Fact]
    public void WithCallHierarchy_IsScopedPerContextToken()
    {
        var events = new[]
        {
            Ev(1, TraceEventKind.Statement, token: "A"),
            Ev(2, TraceEventKind.Trigger,   token: "B"), // different context → no parent from A
        };
        var h = TraceEventGrouper.WithCallHierarchy(events);
        Assert.Null(h[1].ParentEventId);
    }

    // ---------------------------------------------------------------- ring buffer

    [Fact]
    public void RingBuffer_DropsOldestWhenFull_AndCountsDrops()
    {
        var buf = new TraceEventRingBuffer(2);
        Assert.True(buf.Add(Ev(1, TraceEventKind.Statement)));
        Assert.True(buf.Add(Ev(2, TraceEventKind.Statement)));
        Assert.False(buf.Add(Ev(3, TraceEventKind.Statement))); // full → drop oldest

        Assert.Equal(2, buf.Count);
        Assert.Equal(1, buf.DroppedCount);
        var snap = buf.Snapshot();
        Assert.Equal(new long[] { 2, 3 }, snap.Select(e => e.Id)); // oldest (1) evicted
    }

    [Fact]
    public void RingBuffer_ClearKeepsDroppedUnlessReset()
    {
        var buf = new TraceEventRingBuffer(1);
        buf.Add(Ev(1, TraceEventKind.Statement));
        buf.Add(Ev(2, TraceEventKind.Statement)); // drop 1
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.Equal(1, buf.DroppedCount);
        buf.Clear(resetDropped: true);
        Assert.Equal(0, buf.DroppedCount);
    }

    [Fact]
    public void RingBuffer_RejectsNonPositiveCapacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TraceEventRingBuffer(0));

    // ---------------------------------------------------------------- streaming == batch

    [Theory]
    [InlineData("mixed_sequence.trace")]
    [InlineData("procedure_pair.trace")]
    [InlineData("preamble_and_first.trace")]
    public void StreamAccumulator_LineByLine_MatchesBatchParse(string fixture)
    {
        var text = Fixture(fixture);
        var batch = TraceLogParser.Parse(text);

        var acc = new TraceStreamAccumulator();
        var streamed = new List<TraceEvent>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            streamed.AddRange(acc.Append(line)); // one ServiceOutput message per line
        streamed.AddRange(acc.Flush());          // close the final block

        Assert.Equal(batch.Count, streamed.Count);
        for (int i = 0; i < batch.Count; i++)
            Assert.Equal(batch[i], streamed[i]); // record equality — identical events, incl. folded START/FINISH
    }

    [Fact]
    public void StreamAccumulator_WholeChunk_AlsoMatchesBatch()
    {
        var text = Fixture("mixed_sequence.trace");
        var acc = new TraceStreamAccumulator();
        var streamed = acc.Append(text).ToList();
        streamed.AddRange(acc.Flush());
        Assert.Equal(TraceLogParser.Parse(text), streamed);
    }
}
