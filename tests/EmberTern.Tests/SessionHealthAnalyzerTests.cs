using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Session Manager V1 diagnostic engine — the intelligence that names the
/// problematic session, the GC-blocking transaction, and the heavy user. Pure, no DB.</summary>
public class SessionHealthAnalyzerTests
{
    private static readonly DateTime Now = new(2026, 7, 4, 18, 30, 0, DateTimeKind.Unspecified);

    private static SessionInfo Session(long id, bool self = false, long reads = 0, long writes = 0) => new()
    {
        AttachmentId = id,
        User = "USER" + id,
        Application = @"C:\Prestiz\PCbiznes.exe",
        Host = "10.0.0." + id,
        StateCode = 1,
        RecordReads = reads,
        RecordWrites = writes,
        IsSelf = self,
    };

    private static TransactionInfo Tx(long id, long att, int isolation, DateTime? started, int state = 1) => new()
    {
        TransactionId = id,
        AttachmentId = att,
        StateCode = state,
        StartedAt = started,
        IsolationModeCode = isolation,
        IsolationMode = isolation is 0 or 1 ? "Snapshot" : "Read Committed",
    };

    private static DatabaseTransactionState Db(long oit, long oat, long ost, long next) => new()
    {
        OldestTransaction = oit,
        OldestActive = oat,
        OldestSnapshot = ost,
        NextTransaction = next,
    };

    // --- Gap arithmetic ---------------------------------------------------------------------

    [Fact]
    public void DatabaseState_ComputesLagAndSweepGap_ClampedNonNegative()
    {
        var db = Db(oit: 1000, oat: 2000, ost: 2100, next: 50_102);
        Assert.Equal(48_102, db.OldestActiveLag);
        Assert.Equal(1000, db.SweepGap);

        var weird = Db(oit: 5000, oat: 3000, ost: 3000, next: 1000);
        Assert.Equal(0, weird.OldestActiveLag); // next < oat clamps
        Assert.Equal(0, weird.SweepGap);         // oat < oit clamps
    }

    // --- Healthy baseline -------------------------------------------------------------------

    [Fact]
    public void AllReadCommittedAndYoung_IsHealthy_NoFindings()
    {
        var sessions = new[] { Session(10), Session(11) };
        var txs = new[]
        {
            Tx(79990, 10, isolation: 2, started: Now.AddSeconds(-3)),
            Tx(79991, 11, isolation: 2, started: Now.AddSeconds(-1)),
        };
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, Db(79980, 79990, 79990, 79995), Now);

        Assert.Equal(HealthGrade.Healthy, report.Verdict.Grade);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.Counters.GcRisks);
        Assert.Equal(0, report.Counters.LongTransactions);
        Assert.Equal(2, report.Counters.Sessions);
    }

    // --- GC blocker (the flagship) ----------------------------------------------------------

    [Fact]
    public void OldSnapshotHoldingOat_IsGcBlocker_Critical_WithImpactCount()
    {
        var sessions = new[] { Session(23), Session(24) };
        var txs = new[]
        {
            Tx(79195, 23, isolation: 1, started: Now.AddHours(-2)),   // snapshot, oldest active
            Tx(79240, 24, isolation: 2, started: Now.AddSeconds(-5)), // young read-committed
        };
        var db = Db(oit: 79190, oat: 79195, ost: 79195, next: 127_297); // lag ≈ 48,102
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, db, Now);

        Assert.Equal(HealthGrade.AtRisk, report.Verdict.Grade);
        var gc = Assert.Single(report.Findings, f => f.Kind == SessionHealthKind.GarbageCollectionRisk);
        Assert.Equal(SessionHealthSeverity.Critical, gc.Severity);
        Assert.Equal(23, gc.AttachmentId);
        Assert.Equal(79195, gc.TransactionId);
        Assert.Contains("48,102", gc.Impact); // the honest blocked-GC count

        Assert.Equal(1, report.Counters.GcRisks);
        Assert.Equal(SessionRisk.GcBlocker, report.EntryFor(23).Risk);
        Assert.True(report.EntryForTransaction(79195).IsGcBlocker);
        Assert.Equal(127_297 - 79195, report.EntryForTransaction(79195).GcImpact);
    }

    // --- Long-running transaction that is NOT the OAT --------------------------------------

    [Fact]
    public void OldSnapshotNotOat_IsLongTransaction_NotGcBlocker()
    {
        var sessions = new[] { Session(30), Session(31) };
        var txs = new[]
        {
            Tx(80000, 30, isolation: 1, started: Now.AddSeconds(-15)), // OAT holder, young → not flagged
            Tx(80010, 31, isolation: 1, started: Now.AddMinutes(-30)), // old snapshot, but not OAT
        };
        var db = Db(oit: 79990, oat: 80000, ost: 80000, next: 80020); // small lag → OAT not GC-flagged
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, db, Now);

        Assert.DoesNotContain(report.Findings, f => f.Kind == SessionHealthKind.GarbageCollectionRisk);
        var lng = Assert.Single(report.Findings, f => f.Kind == SessionHealthKind.LongRunningTransaction);
        Assert.Equal(80010, lng.TransactionId);
        Assert.True(report.EntryForTransaction(80010).IsLong);
        Assert.Equal(SessionRisk.LongTransaction, report.EntryFor(31).Risk);
        Assert.Equal(1, report.Counters.LongTransactions);
    }

    [Fact]
    public void YoungSnapshot_IsNotFlagged_ScaleBeforeAlarm()
    {
        var sessions = new[] { Session(40) };
        var txs = new[] { Tx(90000, 40, isolation: 1, started: Now.AddSeconds(-10)) };
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, Db(89990, 90000, 90000, 90005), Now);

        Assert.Empty(report.Findings);
        Assert.Equal(SessionRisk.None, report.EntryFor(40).Risk);
    }

    [Fact]
    public void OldReadCommittedNotOat_IsNotFlagged()
    {
        // Read-committed does not pin GC and isn't the OAT here → not a finding regardless of age.
        var sessions = new[] { Session(50), Session(51) };
        var txs = new[]
        {
            Tx(90000, 51, isolation: 2, started: Now.AddSeconds(-2)),      // OAT, young
            Tx(90010, 50, isolation: 2, started: Now.AddMinutes(-45)),     // old but read-committed, not OAT
        };
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, Db(89990, 90000, 90000, 90020), Now);
        Assert.Empty(report.Findings);
    }

    // Heavy-user detection is deferred to V2 (needs an inter-poll rate, not the cumulative
    // MON$RECORD_STATS total) — no heavy test in V1.

    // --- Self exclusion ---------------------------------------------------------------------

    [Fact]
    public void SelfAttachments_ExcludedFromFindingsAndCounters()
    {
        var sessions = new[]
        {
            Session(1, self: true, reads: 9_000_000), // EmberTern's own lane — never blamed
            Session(70),
        };
        var txs = new[]
        {
            Tx(80000, 1, isolation: 1, started: Now.AddHours(-3)), // our own old snapshot — ignored
            Tx(80005, 70, isolation: 2, started: Now.AddSeconds(-1)),
        };
        var db = Db(oit: 79990, oat: 80000, ost: 80000, next: 500_000); // OAT is our own tx
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, db, Now);

        Assert.Empty(report.Findings);               // self tx is not a finding
        Assert.Equal(1, report.Counters.Sessions);   // self not counted
        Assert.Equal(1, report.Counters.Transactions);
        Assert.Equal(SessionRisk.None, report.EntryFor(1).Risk);
        Assert.Equal(500_000 - 80_000, report.Counters.OldestActiveLag); // gap stays honest (DB-wide)
    }

    // --- Per-session derived counts ---------------------------------------------------------

    [Fact]
    public void SessionEntry_CountsActiveTransactionsAndOldestAge()
    {
        var sessions = new[] { Session(80) };
        var txs = new[]
        {
            Tx(90000, 80, 2, Now.AddSeconds(-120), state: 1),
            Tx(90001, 80, 2, Now.AddSeconds(-10), state: 1),
            Tx(90002, 80, 2, Now.AddSeconds(-5), state: 0), // idle
        };
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, Db(89990, 90000, 90000, 90010), Now);

        var e = report.EntryFor(80);
        Assert.Equal(2, e.ActiveTransactionCount);
        Assert.NotNull(e.OldestTransactionAgeSeconds);
        Assert.Equal(120, e.OldestTransactionAgeSeconds!.Value, precision: 0);
    }

    // --- Ordering ---------------------------------------------------------------------------

    [Fact]
    public void Findings_OrderedCriticalFirst()
    {
        var sessions = new[] { Session(90), Session(91) };
        var txs = new[]
        {
            Tx(80000, 90, isolation: 1, started: Now.AddHours(-2)),   // OAT snapshot → GC critical
            Tx(80010, 91, isolation: 1, started: Now.AddMinutes(-3)), // long warning
        };
        var db = Db(oit: 79990, oat: 80000, ost: 80000, next: 130_000);
        var report = SessionHealthAnalyzer.Analyze(sessions, txs, db, Now);

        Assert.True(report.Findings.Count >= 2);
        Assert.Equal(SessionHealthSeverity.Critical, report.Findings[0].Severity);
    }

    // --- ApplicationName leaf ---------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Prestiz\PCbiznes.exe", "PCbiznes")]
    [InlineData("/opt/app/reporter", "reporter")]
    [InlineData("BIConnector", "BIConnector")]
    [InlineData("", "")]
    public void ApplicationName_IsExecutableLeaf(string app, string expected)
    {
        var s = new SessionInfo { AttachmentId = 1, Application = app };
        Assert.Equal(expected, s.ApplicationName);
    }
}
