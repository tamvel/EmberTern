using System;
using System.IO;
using System.Linq;
using EmberTern.Core.Trace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Parser tests built against BYTE-EXACT real snippets extracted from a live ERP
/// trace (Fixtures/Trace/*.trace) — not idealised strings. Covers every event
/// shape in the capture (statement / procedure pair / function pair / trigger /
/// TRACE_INIT), the whitespace-sensitive per-table block, missing optional fields,
/// multi-line SQL, config-preamble + mid-stream junk tolerance, and the
/// START/FINISH folding that yields the curated <see cref="TraceEvent"/> stream.
/// </summary>
public class TraceLogParserTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Trace", name));

    // ---------------------------------------------------------------- ParseRecords (faithful)

    [Fact]
    public void ParseRecords_SkipsPreambleAndJunk_ParsesInitPlusStatement()
    {
        var records = TraceLogParser.ParseRecords(Fixture("preamble_and_first.trace"));

        // The "IBE>" config preamble + banners before the first header are discarded;
        // the trailing "Error creating trace session…" junk (no header) is ignored.
        Assert.Equal(2, records.Count);
        Assert.Equal("TRACE_INIT", records[0].RawEventType);
        Assert.Equal("EXECUTE_STATEMENT_FINISH", records[1].RawEventType);
    }

    [Fact]
    public void ParseRecords_Statement_ExtractsAttachmentTransactionSqlParamAndPerf()
    {
        var r = TraceLogParser.ParseRecords(Fixture("preamble_and_first.trace"))[1];

        Assert.Equal(3093, r.AttachmentId);
        Assert.Equal("SYSSTR", r.UserName);
        Assert.Equal("NONE", r.RoleName);
        Assert.Equal("WIN1250", r.Charset);
        Assert.Equal(218699, r.TransactionId);
        Assert.Equal(967310, r.StatementId);
        Assert.Equal("C:\\Prestiz\\PCbiznes.exe", r.ProcessName);
        Assert.Equal(10856, r.ClientProcessId);

        Assert.NotNull(r.Sql);
        Assert.StartsWith("SELECT WARTOSC", r.Sql);
        Assert.Contains("FROM KONFFIRMY", r.Sql);
        Assert.DoesNotContain("records fetched", r.Sql);
        Assert.DoesNotContain("param0", r.Sql);
        Assert.DoesNotContain("---", r.Sql); // the "-----" separator is not SQL

        Assert.Equal(1, r.RecordsFetched);
        Assert.Equal(0, r.DurationMs);
        Assert.Equal(3, r.Fetches);

        var p = Assert.Single(r.Parameters);
        Assert.Equal(0, p.Index);
        Assert.Equal("integer", p.DataType);
        Assert.Equal("3234", p.Value);
    }

    [Fact]
    public void ParseRecords_PerTableBlock_UsesHeaderOffsets_KonffirmyIsIndexedOnly()
    {
        var r = TraceLogParser.ParseRecords(Fixture("preamble_and_first.trace"))[1];
        var t = Assert.Single(r.TableReads);
        Assert.Equal("KONFFIRMY", t.TableName);
        Assert.Equal(0, t.Natural);
        Assert.Equal(1, t.Indexed); // the lone "1" sits under the Index column, not Natural
        Assert.Equal(1, t.RecordReads);
    }

    [Fact]
    public void ParseRecords_MultilineSql_TerminatedByRecordsWithNoBlankLine_NaturalScan()
    {
        var r = Assert.Single(TraceLogParser.ParseRecords(Fixture("statement_multiline.trace")));

        Assert.NotNull(r.Sql);
        Assert.Contains("JOIN FILTRDEF_USER", r.Sql);
        Assert.Contains("ORDER BY FU.LP", r.Sql); // last SQL line, directly followed by "0 records fetched"
        Assert.DoesNotContain("records fetched", r.Sql);
        Assert.Equal(0, r.RecordsFetched);

        var t = Assert.Single(r.TableReads);
        Assert.Equal("FILTRDEF", t.TableName);
        Assert.Equal(1, t.Natural); // this "1" sits under Natural (a full scan) — the opposite of KONFFIRMY
        Assert.Equal(0, t.Indexed);
    }

    [Fact]
    public void ParseRecords_ProcedurePair_TwoRecords_FinishCarriesResultAndReads()
    {
        var records = TraceLogParser.ParseRecords(Fixture("procedure_pair.trace"));
        Assert.Equal(2, records.Count);
        Assert.Equal("EXECUTE_PROCEDURE_START", records[0].RawEventType);
        Assert.Equal("EXECUTE_PROCEDURE_FINISH", records[1].RawEventType);

        var finish = records[1];
        Assert.Equal("ILOSC_PLAN_PRZERZUTOW_Z", finish.ObjectName);
        Assert.Equal(2, finish.Parameters.Count);
        Assert.Equal(1, finish.RecordsFetched);
        Assert.Equal(1, finish.PageReads);   // "0 ms, 1 read(s), 2 fetch(es)"
        Assert.Equal(2, finish.Fetches);
        Assert.Null(finish.Sql);             // procedures have no statement text
    }

    [Fact]
    public void ParseRecords_FunctionPair_FinishCarriesReturnsAndNullParam()
    {
        var records = TraceLogParser.ParseRecords(Fixture("function_pair.trace"));
        Assert.Equal(2, records.Count);
        var finish = records[1];

        Assert.Equal("SIGN", finish.ObjectName);
        var input = Assert.Single(finish.Parameters);
        Assert.Equal("double precision", input.DataType);
        Assert.Null(input.Value); // "<NULL>" → null

        var ret = Assert.Single(finish.ReturnValues);
        Assert.Equal("integer", ret.DataType);
        Assert.Equal("0", ret.Value);
    }

    [Fact]
    public void ParseRecords_Trigger_CapturesNameEventAndDuration()
    {
        var r = Assert.Single(TraceLogParser.ParseRecords(Fixture("trigger.trace")));
        Assert.Equal("EXECUTE_TRIGGER_FINISH", r.RawEventType);
        Assert.Equal("XXX_WS_TRANS_ON_COMMIT", r.ObjectName);
        Assert.Equal("ON TRANSACTION_COMMIT", r.TriggerEvent);
        Assert.Equal(0, r.DurationMs);
        Assert.Equal(220476, r.TransactionId);
        Assert.Null(r.Sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no header lines here\njust junk\nIBE> preamble\n")]
    public void ParseRecords_EmptyOrHeaderless_YieldsNothing(string text)
        => Assert.Empty(TraceLogParser.ParseRecords(text));

    // ---------------------------------------------------------------- Parse (folded TraceEvent)

    [Fact]
    public void Parse_FoldsProcedureStartFinish_IntoOneEvent()
    {
        var events = TraceLogParser.Parse(Fixture("procedure_pair.trace"));
        var e = Assert.Single(events);
        Assert.Equal(TraceEventKind.Procedure, e.Kind);
        Assert.Equal("ILOSC_PLAN_PRZERZUTOW_Z", e.ObjectName);
        Assert.Equal(1, e.RowsFetched);
        Assert.Null(e.Sql);
        // Folded span: START..FINISH timestamps (identical for a 0 ms op) + perf duration.
        Assert.Equal(TimeSpan.Zero, e.Duration);
    }

    [Fact]
    public void Parse_FoldsFunctionStartFinish_IntoOneEvent()
    {
        var e = Assert.Single(TraceLogParser.Parse(Fixture("function_pair.trace")));
        Assert.Equal(TraceEventKind.Function, e.Kind);
        Assert.Equal("SIGN", e.ObjectName);
    }

    [Fact]
    public void Parse_StatementEvent_MapsReadsFromPerTableAndKeepsSql()
    {
        var events = TraceLogParser.Parse(Fixture("preamble_and_first.trace"));
        // TRACE_INIT (System) + the statement.
        Assert.Equal(2, events.Count);
        Assert.Equal(TraceEventKind.System, events[0].Kind);

        var stmt = events[1];
        Assert.Equal(TraceEventKind.Statement, stmt.Kind);
        Assert.StartsWith("SELECT WARTOSC", stmt.Sql);
        Assert.Equal(1, stmt.Reads);           // KONFFIRMY natural(0) + indexed(1)
        Assert.Equal(218699, stmt.TransactionId);
        Assert.Equal(3093, stmt.AttachmentId);
    }

    [Fact]
    public void Parse_MixedSequence_NoStartLeaksAndSequenceIsMonotonic()
    {
        var events = TraceLogParser.Parse(Fixture("mixed_sequence.trace"));
        Assert.NotEmpty(events);

        // Folding invariant: raw *_START markers are absorbed, never surfaced as events.
        Assert.Contains(events, e => e.Kind == TraceEventKind.Trigger);
        Assert.Contains(events, e => e.Kind == TraceEventKind.Function);

        // A folded event count must be < the raw record count (STARTs were absorbed).
        var rawCount = TraceLogParser.ParseRecords(Fixture("mixed_sequence.trace")).Count;
        Assert.True(events.Count < rawCount, $"folded {events.Count} should be < raw {rawCount}");

        // Sequence + id are 1..N monotonic; first event's delta is null.
        for (int i = 0; i < events.Count; i++)
            Assert.Equal(i + 1, events[i].Sequence);
        Assert.Null(events[0].DeltaMs);
        Assert.All(events.Skip(1), e => Assert.NotNull(e.DeltaMs));
    }

    [Fact]
    public void Parse_SelfActivity_FlaggedWhenAttachmentMatches()
    {
        var self = TraceLogParser.Parse(Fixture("preamble_and_first.trace"), new long[] { 3093 });
        Assert.Contains(self, e => e.AttachmentId == 3093 && e.IsSelfActivity);

        var other = TraceLogParser.Parse(Fixture("preamble_and_first.trace"), new long[] { 9999 });
        Assert.All(other, e => Assert.False(e.IsSelfActivity));
    }

    [Fact]
    public void Parse_ContextTokenPreserved_ForFutureHierarchyPass()
    {
        var e = Assert.Single(TraceLogParser.Parse(Fixture("trigger.trace")));
        Assert.False(string.IsNullOrEmpty(e.ContextToken));
    }
}
