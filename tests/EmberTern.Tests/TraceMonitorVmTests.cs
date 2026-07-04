using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Trace;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The testable core of the Activity Monitor tab VM (the "one sacred chronological grid +
/// lenses" model): batch ingest, operation bands, cap-trim, hide-self / text / show-only
/// filtering, transaction + fingerprint lenses, and highlight. The live service + grid are
/// manual-smoke; here we drive <see cref="TraceMonitorTabViewModel.Ingest"/> directly.
/// </summary>
public class TraceMonitorVmTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 3, 19, 10, 0, TimeSpan.Zero);
    private static long _id;

    private static TraceMonitorTabViewModel NewVm()
        => new(new FirebirdTraceService(new FirebirdConnectionService())) { FollowTail = false };

    private static TraceEvent Ev(TraceEventKind kind, long? tx = 1, string? sql = null,
        int durMs = 0, bool self = false, long? att = null, string? obj = null)
    {
        var id = ++_id;
        return new TraceEvent
        {
            Id = id,
            Sequence = id,
            Kind = kind,
            Severity = kind == TraceEventKind.System ? TraceEventSeverity.System : TraceEventSeverity.Normal,
            StartTime = T0.AddMilliseconds(id),
            Duration = TimeSpan.FromMilliseconds(durMs),
            TransactionId = tx,
            AttachmentId = att,
            IsSelfActivity = self,
            ContextToken = "CTX",
            Sql = sql,
            ObjectName = obj,
        };
    }

    private static TraceEvent Error()
        => Ev(TraceEventKind.Statement, sql: "SELECT * FROM BROKEN") with { Severity = TraceEventSeverity.Error, ErrorText = "boom" };

    [Fact]
    public void Ingest_AddsRows_ChronologicalOrder()
    {
        var vm = NewVm();
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "SELECT 1"), Ev(TraceEventKind.Procedure, obj: "P") });
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(2, vm.TotalCount);
        Assert.True(vm.Rows[0].Sequence < vm.Rows[1].Sequence);
    }

    [Fact]
    public void Ingest_OperationBand_FlipsOnTransactionChange()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, tx: 100),
            Ev(TraceEventKind.Trigger, tx: 100),   // same tx → same band
            Ev(TraceEventKind.Statement, tx: 200), // new tx → flipped band
        });
        Assert.Equal(vm.Rows[0].BandKey, vm.Rows[1].BandKey);
        Assert.NotEqual(vm.Rows[1].BandKey, vm.Rows[2].BandKey);
    }

    [Fact]
    public void Ingest_TrimsToCap_DroppingOldest()
    {
        var vm = NewVm();
        vm.DisplayCap = 3;
        vm.Ingest(Enumerable.Range(0, 5).Select(_ => Ev(TraceEventKind.Statement, sql: "S")).ToList());
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public void HideSelfActivity_HidesSelfRows_AndToggleRestores()
    {
        var vm = NewVm(); // HideSelfActivity defaults true
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "mine", self: true), Ev(TraceEventKind.Statement, sql: "theirs") });
        Assert.Single(vm.Rows);
        Assert.Equal(2, vm.TotalCount);

        vm.HideSelfActivity = false;
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void FilterText_Narrows_ButNeverHidesErrors()
    {
        var vm = NewVm();
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "SELECT FROM CUSTOMERS"), Ev(TraceEventKind.Statement, sql: "SELECT FROM ORDERS"), Error() });
        vm.FilterText = "CUSTOMERS";
        // the CUSTOMERS statement matches; the error row is always kept; ORDERS is filtered out.
        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains(vm.Rows, r => r.IsError);
    }

    [Fact]
    public void TransactionLens_LabelledByRepresentativeStatement()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, tx: 7, sql: "SELECT WARTOSC FROM KONFFIRMY"),
            Ev(TraceEventKind.Trigger, tx: 7, obj: "TR_X"),
        });
        vm.GroupMode = TraceGroupMode.Transaction;
        var item = Assert.Single(vm.TransactionLens);
        Assert.Contains("KONFFIRMY", item.Label);   // representative statement, not the raw id
        Assert.Equal(7, item.TransactionId);
    }

    [Fact]
    public void StatementLens_CollapsesIdenticalQueries_WithCount()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, sql: "SELECT A FROM T WHERE ID = 1", durMs: 10),
            Ev(TraceEventKind.Statement, sql: "select a from t where id = 2", durMs: 30),
        });
        vm.GroupMode = TraceGroupMode.Statement;
        var item = Assert.Single(vm.StatementLens);
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void SelectingLens_HighlightsMatchingRows_AndShowOnlySelectedNarrows()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, tx: 1, sql: "SELECT 1"),
            Ev(TraceEventKind.Statement, tx: 2, sql: "SELECT 2"),
        });
        vm.GroupMode = TraceGroupMode.Transaction;
        var tx2 = vm.TransactionLens.First(t => t.TransactionId == 2);

        vm.SelectedLensItem = tx2;
        Assert.Single(vm.Rows, r => r.IsHighlighted);            // only tx 2 highlighted
        Assert.False(vm.Rows.First(r => r.TransactionId == 1).IsHighlighted);

        vm.ShowOnlySelected = true;
        Assert.Single(vm.Rows);                                   // narrowed to tx 2
        Assert.Equal(2, vm.Rows[0].TransactionId);
    }
}
