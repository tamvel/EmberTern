using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.App;
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

    [Fact]
    public void QuickFilter_Errors_ShowsOnlyErrors()
    {
        var vm = NewVm();
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "SELECT 1"), Error(), Ev(TraceEventKind.Statement, sql: "SELECT 2") });
        vm.QuickFilter = TraceQuickFilter.Errors;
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsError);
    }

    [Fact]
    public void QuickFilter_Slow_ShowsOnlySlowOperations()
    {
        var vm = NewVm();
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "fast", durMs: 5), Ev(TraceEventKind.Statement, sql: "slow", durMs: 250) });
        vm.QuickFilter = TraceQuickFilter.Slow;
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsSlow);
    }

    [Fact]
    public void EmptyState_ReflectsNoSession_ThenNoMatch()
    {
        var vm = NewVm();
        Assert.True(vm.ShowEmptyState);
        Assert.Equal(UiStrings.TraceEmptyHint, vm.EmptyStateText);

        vm.Ingest(new[] { Ev(TraceEventKind.Statement, sql: "SELECT 1") });
        Assert.False(vm.ShowEmptyState);

        vm.QuickFilter = TraceQuickFilter.Errors;   // no errors → nothing matches
        Assert.True(vm.ShowEmptyState);
        Assert.Equal(UiStrings.TraceEmptyNoMatch, vm.EmptyStateText);
    }

    [Fact]
    public void CleanSql_StripsTraceSeparatorLines()
    {
        Assert.Equal("SELECT * FROM CECHA",
            TraceEventRowViewModel.CleanSql("-----------------------------\nSELECT * FROM CECHA"));
        Assert.DoesNotContain("-", TraceEventRowViewModel.Elide("--------\nSELECT 1"));
    }

    // ---- V1.1 ----

    [Fact]
    public void IncludeFunctions_DefaultsOff_AndBuildSessionConfigReflectsToggle()
    {
        var vm = NewVm();
        Assert.False(vm.IncludeFunctions);
        Assert.False(vm.BuildSessionConfig().IncludeFunctions);

        vm.IncludeFunctions = true;
        Assert.True(vm.BuildSessionConfig().IncludeFunctions);
        Assert.True(vm.BuildSessionConfig().IncludeStatements); // other knobs unchanged
    }

    [Fact]
    public void ShowValues_DefaultsOn_InlinesParameters_AndToggleReverts()
    {
        var vm = NewVm();
        var e = new TraceEvent
        {
            Id = 1, Sequence = 1, Kind = TraceEventKind.Statement, StartTime = T0,
            Sql = "SELECT * FROM NAGL WHERE ID_NAGL = ?",
            Parameters = new[] { new RawTraceParam(0, "integer", "10036") },
        };
        vm.Ingest(new[] { e });
        vm.SelectedRow = vm.Rows[0];

        Assert.True(vm.ShowValues);
        Assert.Contains("= 10036", vm.Detail.Sql);          // inlined by default

        vm.ShowValues = false;
        Assert.Contains("= ?", vm.Detail.Sql);              // faithful parameterised source
        Assert.DoesNotContain("10036", vm.Detail.Sql);
    }

    [Fact]
    public void Folder_CarriesTriggerEventAndTransactionParams()
    {
        var folder = new TraceEventFolder();
        var ev = folder.Push(new RawTraceRecord
        {
            RawEventType = "EXECUTE_TRIGGER_FINISH",
            Timestamp = T0,
            ObjectName = "TR_NAGL_AU",
            TriggerEvent = "AFTER UPDATE",
            TransactionId = 42,
            TransactionParams = "READ_COMMITTED | REC_VERSION | WAIT | READ_WRITE",
        });
        Assert.NotNull(ev);
        Assert.Equal("AFTER UPDATE", ev!.TriggerEvent);
        Assert.Equal("READ_COMMITTED | REC_VERSION | WAIT | READ_WRITE", ev.TransactionParams);
    }

    [Fact]
    public void DetailPanel_SurfacesTriggerEventAndTransactionIsolation()
    {
        var detail = new TraceEventDetailViewModel();
        detail.Update(new TraceEvent
        {
            Id = 1, Sequence = 1, Kind = TraceEventKind.Trigger, StartTime = T0,
            ObjectName = "TR_NAGL_AU", TriggerEvent = "AFTER UPDATE",
            TransactionId = 42, TransactionParams = "READ_COMMITTED | REC_VERSION | WAIT",
        });
        Assert.Contains(detail.SessionRows, r => r.Label == "Trigger event" && r.Value == "AFTER UPDATE");
        Assert.Contains(detail.SessionRows, r => r.Label == "Transaction" && r.Value.Contains("TRA 42") && r.Value.Contains("READ_COMMITTED"));
    }

    [Fact]
    public void DetailPanel_Session_ExposesExecutorIdentity()
    {
        var detail = new TraceEventDetailViewModel();
        detail.Update(new TraceEvent
        {
            Id = 1, Sequence = 1, Kind = TraceEventKind.Statement, StartTime = T0,
            TransactionId = 42, AttachmentId = 7,
            UserName = "SYSDBA", RoleName = "RDB$ADMIN", RemoteAddress = "10.0.0.5/54321",
            ProcessName = "erp.exe", ClientProcessId = 1234, Sql = "SELECT 1",
        });
        Assert.True(detail.HasSession);
        Assert.Contains(detail.SessionRows, r => r.Label == "User" && r.Value == "SYSDBA");
        Assert.Contains(detail.SessionRows, r => r.Label == "Process" && r.Value.Contains("erp.exe") && r.Value.Contains("1234"));
        Assert.Contains(detail.SessionRows, r => r.Label == "Transaction" && r.Value == "TRA 42");
        Assert.Contains(detail.SessionRows, r => r.Label == "Attachment" && r.Value == "ATT 7");
    }

    // ---- V1.2 ----

    [Fact]
    public void Row_KindLabel_ShowsOperationForStatements()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, sql: "UPDATE NAGL SET S = 1 WHERE ID = 3"),
            Ev(TraceEventKind.Procedure, obj: "NAGL_GET_STANREALIZ"),
        });
        Assert.Equal("UPDATE", vm.Rows[0].KindLabel);      // operation, not "Statement"
        Assert.Equal(TraceSqlOperation.Update, vm.Rows[0].Operation);
        Assert.Equal("Procedure", vm.Rows[1].KindLabel);   // routine keeps the kind name
    }

    [Fact]
    public void EventFilter_HidesUncheckedKind_ButNeverErrors()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, sql: "SELECT 1"),
            Ev(TraceEventKind.Procedure, obj: "P"),
            Error(),                                        // an error statement
        });
        Assert.Equal(3, vm.Rows.Count);
        Assert.False(vm.IsEventFilterActive);

        vm.ShowProcedureEvents = false;
        Assert.True(vm.IsEventFilterActive);
        Assert.DoesNotContain(vm.Rows, r => r.Event.Kind == TraceEventKind.Procedure);
        Assert.Equal(2, vm.Rows.Count);

        vm.ShowStatementEvents = false;                     // hides the SELECT, keeps the error
        Assert.Contains(vm.Rows, r => r.IsError);
        Assert.DoesNotContain(vm.Rows, r => r is { IsError: false });
    }

    [Fact]
    public void EventFilter_OperationGate_HidesOnlyThatOperation()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, sql: "SELECT * FROM T"),
            Ev(TraceEventKind.Statement, sql: "UPDATE T SET A = 1"),
        });
        vm.ShowOpUpdate = false;
        Assert.Single(vm.Rows);
        Assert.Equal(TraceSqlOperation.Select, vm.Rows[0].Operation);
    }

    [Fact]
    public void ResetEventFilter_RestoresAll()
    {
        var vm = NewVm();
        vm.ShowProcedureEvents = false;
        vm.ShowOpDelete = false;
        Assert.True(vm.IsEventFilterActive);

        vm.ResetEventFilterCommand.Execute(null);
        Assert.False(vm.IsEventFilterActive);
        Assert.True(vm.ShowProcedureEvents);
        Assert.True(vm.ShowOpDelete);
    }

    [Fact]
    public void CopyRow_And_CopyCell_And_CopySql_RaiseClipboard()
    {
        var vm = NewVm();
        string? copied = null;
        vm.CopyToClipboardRequested += t => copied = t;
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, tx: 55, sql: "UPDATE NAGL SET S = 1") });
        var row = vm.Rows[0];

        vm.CopyRow(row);
        Assert.NotNull(copied);
        Assert.Contains("UPDATE NAGL SET S = 1", copied);
        Assert.Contains("\t", copied);                      // TSV

        vm.CopyCell(row, UiStrings.TraceColEvent);
        Assert.Equal("UPDATE", copied);

        vm.CopyCell(row, UiStrings.TraceColTx);
        Assert.Equal("55", copied);

        vm.CopyRowSql(row);
        Assert.Equal("UPDATE NAGL SET S = 1", copied);
    }

    [Fact]
    public void ErrorBlock_ParsesThroughPipeline_FlaggedAndMessaged()
    {
        // A real Firebird "ERROR AT jrd8_execute" block: attach/app/tx lines, a failed statement,
        // and a status-vector error line. Verifies Firebird → Parser → TraceEvent (→ Grid/Detail).
        const string trace =
            "2026-07-04T13:00:00.0000 (8188:0000000A) ERROR AT jrd8_execute\n" +
            "\tSZKOLENIE (ATT_132, SYSDBA:NONE, WIN1250, TCPv6:::1/57835)\n" +
            "\tC:\\App.exe:8188\n" +
            "\t\t(TRA_14040, READ_COMMITTED | REC_VERSION | WAIT | READ_WRITE)\n" +
            "Statement 42:\n" +
            "-------------------------------------------------------------------------------\n" +
            "UPDATE NAGL SET STATUS = 5 WHERE ID_NAGL = ?\n" +
            "param0 = integer, \"10037\"\n" +
            "335544665 : violation of PRIMARY or UNIQUE KEY constraint \"PK_NAGL\" on table \"NAGL\"\n";

        var events = TraceLogParser.Parse(trace);
        var e = Assert.Single(events);
        Assert.Equal(TraceEventSeverity.Error, e.Severity);
        Assert.Equal(TraceEventKind.Statement, e.Kind);          // ERROR-AT-with-SQL → Statement
        Assert.Contains("violation of PRIMARY", e.ErrorText);
        Assert.Contains("UPDATE NAGL", e.Sql);                   // SQL not polluted by the status line

        // Grid + Errors quick-filter surface it.
        var vm = NewVm();
        vm.Ingest(events);
        Assert.True(vm.Rows[0].IsError);
        vm.QuickFilter = TraceQuickFilter.Errors;
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsError);

        // Detail shows the error message.
        vm.SelectedRow = vm.Rows[0];
        Assert.True(vm.Detail.HasError);
        Assert.Contains("violation of PRIMARY", vm.Detail.ErrorText);

        // V1.2 #1: the grid's Object column shows the (code-stripped) error message.
        Assert.StartsWith("violation of PRIMARY", vm.Rows[0].ObjectText);
    }

    [Fact]
    public void ShortErrorMessage_StripsGdsCodeAndTakesFirstLine()
    {
        var text = "335544876 : Error while parsing procedure POZZAMWSP_DODAJ_DO_STANMAG's BLR\n"
                 + "335544343 : invalid request BLR at offset 555";
        Assert.Equal("Error while parsing procedure POZZAMWSP_DODAJ_DO_STANMAG's BLR",
            TraceEventRowViewModel.ShortErrorMessage(text));
    }

    [Fact]
    public void ErrorRow_ObjectText_ShowsMessage_NotEmpty()
    {
        var vm = NewVm();
        vm.Ingest(new[]
        {
            new TraceEvent
            {
                Id = 1, Sequence = 1, Kind = TraceEventKind.System, StartTime = T0,
                Severity = TraceEventSeverity.Error,
                ErrorText = "335544512 : Input parameter mismatch for procedure STANMAG_DODAJ_JESLIBRAK",
            },
        });
        Assert.True(vm.Rows[0].IsError);
        Assert.Equal("Input parameter mismatch for procedure STANMAG_DODAJ_JESLIBRAK", vm.Rows[0].ObjectText);
    }

    [Fact]
    public void CopyRowWithHeaders_PrependsHeaderLine()
    {
        var vm = NewVm();
        string? copied = null;
        vm.CopyToClipboardRequested += t => copied = t;
        vm.Ingest(new[] { Ev(TraceEventKind.Statement, tx: 7, sql: "SELECT 1") });

        vm.CopyRowWithHeaders(vm.Rows[0]);
        var lines = copied!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains(UiStrings.TraceColEvent, lines[0]);      // header row
        Assert.Contains(UiStrings.TraceColObject, lines[0]);
        Assert.Contains("SELECT 1", lines[1]);                    // value row
    }

    [Fact]
    public void CopyAllWithHeaders_EmitsHeaderPlusEveryVisibleRow()
    {
        var vm = NewVm();
        string? copied = null;
        vm.CopyToClipboardRequested += t => copied = t;
        vm.Ingest(new[]
        {
            Ev(TraceEventKind.Statement, sql: "SELECT 1"),
            Ev(TraceEventKind.Procedure, obj: "P"),
        });
        vm.CopyAllWithHeaders();
        var lines = copied!.Split('\n');
        Assert.Equal(3, lines.Length);                            // header + 2 rows
        Assert.Contains(UiStrings.TraceColSeq, lines[0]);
        Assert.Contains("SELECT 1", lines[1]);
        Assert.Contains("P", lines[2]);
    }
}
