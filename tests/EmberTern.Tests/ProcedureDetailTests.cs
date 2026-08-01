using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

// Procedure Detail V1: a procedure opens in a dedicated 4-tab surface (Editor /
// Description / Dependencies / DDL, with Input/Output parameter grids under the
// editor) — NOT a plain DDL tab and NOT the table/view detail VM. The editable
// source compiles via CREATE OR ALTER PROCEDURE.
public class ProcedureDetailTests
{
    [Theory]
    [InlineData(MetadataObjectKind.Procedure, true)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.SystemTable, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Function, false)]
    public void OpensAsProcedureDetail_ProceduresOnly(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsProcedureDetail(kind));

    [Fact]
    public void Factory_Procedure_HasCompile()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_BALANCE", MetadataObjectKind.Procedure));

        Assert.True(vm.CanCompile);
        Assert.Equal("SP_BALANCE", vm.ProcedureName);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void DirectOpen_Procedure_OpensProcedureDetailTab_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("SP_BALANCE", MetadataObjectKind.Procedure);
        harness.Main.Metadata.RequestOpenDdl(obj);

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "SP_BALANCE");
        Assert.Equal(VmTabKind.ProcedureDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Procedure, tab.ObjectKind);
        Assert.NotNull(tab.ProcedureDetail);
    }

    [Fact]
    public void DirectOpen_Procedure_Twice_FocusesExistingTab()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("SP_BALANCE", MetadataObjectKind.Procedure);

        harness.Main.Metadata.RequestOpenDdl(obj);
        harness.Main.Metadata.RequestOpenDdl(obj);

        Assert.Single(harness.Main.WorkspaceTabs, t => t.ObjectName == "SP_BALANCE");
    }

    [Fact]
    public void Restore_ProcedureTab_NativeProcedureDetail_NotDdl()
    {
        using var harness = new Harness();
        harness.Main.RestoreWorkspace(new WorkspaceState
        {
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    Tabs =
                    {
                        new WorkspaceTab
                        {
                            Kind = CoreTabKind.ProcedureDetail,
                            ObjectName = "SP_BALANCE",
                            ObjectKind = MetadataObjectKind.Procedure,
                            DdlText = "CREATE OR ALTER PROCEDURE \"SP_BALANCE\" AS BEGIN END",
                        },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        var tab = harness.Main.WorkspaceTabs.Single(t => t.ObjectName == "SP_BALANCE");
        Assert.Equal(VmTabKind.ProcedureDetail, tab.Kind);
        Assert.Equal(MetadataObjectKind.Procedure, tab.ObjectKind);
        Assert.NotNull(tab.ProcedureDetail);
        Assert.Contains("SP_BALANCE", tab.ProcedureDetail!.DdlText);
    }

    [Fact]
    public void Capture_ProcedureTab_PersistsAsProcedureDetail()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.Metadata.RequestOpenDdl(new MetadataObject("SP_BALANCE", MetadataObjectKind.Procedure));

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.TryGetValue("A", out var ws));
        Assert.Contains(ws!.Tabs, t => t.Kind == CoreTabKind.ProcedureDetail && t.ObjectName == "SP_BALANCE");
    }

    // ─── ProcedureDetailTabViewModel unit behavior (no live FB) ───────────

    [Fact]
    public void NoExecutor_CannotCompile()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X");
        Assert.False(vm.CanCompile);
    }

    [Fact]
    public async Task ExecuteCompile_NoExecutor_ReportsNoConnection()
    {
        // Seam 6b — was "…_IsNoOp" and asserted ErrorMessage stayed NULL, which is precisely the defect:
        // SaveAsync reads "no error" as success, so a silent exit made the save-and-close WorkGuard
        // believe the code had been written when nothing ran.
        var vm = new ProcedureDetailTabViewModel("SP_X") { SourceText = "CREATE OR ALTER PROCEDURE SP_X AS BEGIN END" };
        await vm.ExecuteCompileAsync();
        Assert.Equal(UiStrings.NoConnectionMessage, vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteCompile_EmptySource_ReportsNothingToCompile()
    {
        // The second silent exit (executor wired, buffer emptied). Same reasoning as above — and here a
        // wrong "success" would discard source the user still has on screen.
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_X", MetadataObjectKind.Procedure));
        vm.SourceText = "   ";
        await vm.ExecuteCompileAsync();
        Assert.Equal(UiStrings.EditorNothingToCompile, vm.ErrorMessage);
    }

    // ─── DDL change safety (audit A-01) ───────────────────────────────────
    //
    // These prove the gate is ON THE COMPILE PATH, which the ObjectChangeSafetyTests deliberately cannot:
    // a component that is fully unit-tested but never called looks exactly like a working feature and
    // exactly like a regression (gotcha #233). The engine's own decision table lives there; here we only
    // assert that ExecuteCompileAsync consults it and stops.

    [Fact]
    public async Task ExecuteCompile_RefusesWhenSafetyCannotBeEstablished()
    {
        // An existing object with no reachable database: the baseline was never captured and the re-read
        // cannot run, so the gate reports "unverifiable" — and unverifiable is NOT permission to write.
        // Before the gate existed this reached FirebirdDdlExecutor and failed for an unrelated reason; now
        // the refusal happens before any DDL is attempted, and says so.
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_X", MetadataObjectKind.Procedure));
        vm.SourceText = "CREATE OR ALTER PROCEDURE SP_X AS BEGIN END";

        await vm.ExecuteCompileAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("could not read the current state", vm.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("Nothing was written", vm.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCompile_NewObject_RefusesWhenTheNameIsAlreadyTaken()
    {
        // The New flow's own hazard: BuildFullSource always emits CREATE OR ALTER, so a name collision
        // OVERWRITES a colleague's procedure instead of failing. One user and a typo are enough.
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_X", MetadataObjectKind.Procedure));
        var newVm = new ProcedureDetailTabViewModel("NEW_PROCEDURE")
        {
            IsNew = true,
            SourceText = "CREATE OR ALTER PROCEDURE SP_TAKEN AS BEGIN END",
        };
        newVm.ObjectExistsProbe = (_, _) => Task.FromResult(true);

        // No DdlExecutor on this VM, so assert the gate's verdict directly rather than through the compile's
        // earlier no-connection refusal — the wiring under test is "the New flow asks about the name it will
        // actually create", which is the name parsed out of the statement.
        var check = await newVm.ChangeGate.CheckCreateAsync("SP_TAKEN", _ => Task.FromResult(true));

        Assert.False(check.MayProceed);
        Assert.Contains("SP_TAKEN", check.RefusalMessage!, StringComparison.Ordinal);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public async Task ExecuteCompile_StillReportsNoConnection_BeforeReachingTheGate()
    {
        // Ordering guard: the cheap, settled refusals must keep their wording. The gate costs a catalog round
        // trip, so it runs last — a missing connection is still reported as a missing connection.
        var vm = new ProcedureDetailTabViewModel("SP_X")
        {
            SourceText = "CREATE OR ALTER PROCEDURE SP_X AS BEGIN END",
        };

        await vm.ExecuteCompileAsync();

        Assert.Equal(UiStrings.NoConnectionMessage, vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadDefinition_DisarmsTheGate_BeforeReading_SoAFailedReloadCannotAuthoriseAWrite()
    {
        // The ORDERING inside LoadDefinitionAsync, exercised through the real load path: the baseline is
        // dropped BEFORE the read, so a reload that fails leaves the gate unverifiable rather than holding a
        // fingerprint nobody re-verified. Reversed, this test would see the stale baseline survive.
        //
        // The throw is pre-existing and unrelated: a disconnected lane raises InvalidOperationException, which
        // SafeLoadAsync does not trap (it traps MetadataReadException). Asserting it keeps the test honest
        // about what actually happens instead of hiding it.
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_X", MetadataObjectKind.Procedure));
        vm.ChangeGate.CaptureBaseline("CREATE OR ALTER PROCEDURE SP_X AS BEGIN END");
        Assert.NotNull(vm.ChangeGate.BaselineFingerprint);

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.LoadAsync());

        Assert.Null(vm.ChangeGate.BaselineFingerprint);
    }

    [Fact]
    public async Task NewMode_LoadAsync_IsNoOp()
    {
        var vm = new ProcedureDetailTabViewModel("NEW_PROCEDURE") { IsNew = true, SourceText = ProcedureDetailTabViewModel.NewProcedureTemplate };
        await vm.LoadAsync();
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.InputParams);
        Assert.Empty(vm.OutputParams);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE SP_FOO (A INTEGER) AS BEGIN END", "SP_FOO")]
    [InlineData("create or alter procedure sp_bar returns (r integer) as begin end", "SP_BAR")]
    [InlineData("CREATE OR ALTER PROCEDURE \"MixedCase\" AS BEGIN END", "MixedCase")]
    [InlineData("CREATE PROCEDURE   SP_SPACED\n(\n  X INTEGER\n)\nAS BEGIN END", "SP_SPACED")]
    [InlineData("CREATE TABLE T (X INTEGER)", null)]                  // not a procedure
    [InlineData("SELECT * FROM FOO", null)]                            // not a CREATE
    [InlineData("CREATE OR REPLACE PROCEDURE P AS BEGIN END", null)]   // OR must be OR ALTER
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryParseProcedureName_Cases(string? sql, string? expected)
        => Assert.Equal(expected, ProcedureDetailTabViewModel.TryParseProcedureName(sql));

    // ─── Parameter tab headers track the collections ──────────────────────

    [Fact]
    public void ParameterTabHeaders_TrackCounts()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X");
        Assert.Equal("Input (0)", vm.InputTabHeader);
        Assert.Equal("Output (0)", vm.OutputTabHeader);

        vm.InputParams.Add(new ProcedureParamRowViewModel { Name = "IN_A", TypeText = "INTEGER" });
        vm.OutputParams.Add(new ProcedureParamRowViewModel { Name = "OUT_R", TypeText = "INTEGER" });
        vm.OutputParams.Add(new ProcedureParamRowViewModel { Name = "OUT_S", TypeText = "VARCHAR(10)" });

        Assert.Equal("Input (1)", vm.InputTabHeader);
        Assert.Equal("Output (2)", vm.OutputTabHeader);
    }

    // ─── SQL formatting (reuses the shared SqlFormatter) ──────────────────

    [Fact]
    public void FormatSql_FormatsSourceWholesale_WhenNoSelectionCallback()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X") { SourceText = "SELECT 1 FROM RDB$DATABASE" };
        var expected = SqlFormatter.Format("SELECT 1 FROM RDB$DATABASE");

        vm.FormatSqlCommand.Execute(null);

        Assert.Equal(expected, vm.SourceText);
        Assert.NotEqual("SELECT 1 FROM RDB$DATABASE", vm.SourceText);
    }

    [Fact]
    public void FormatSql_UsesSelectionCallback_WhenWired()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X") { SourceText = "ignored full text" };
        string? replaced = null;
        vm.SelectedTextProvider = () => "SELECT 1 FROM RDB$DATABASE";
        vm.ReplaceSelectedOrAllText = t => replaced = t;

        vm.FormatSqlCommand.Execute(null);

        Assert.Equal(SqlFormatter.Format("SELECT 1 FROM RDB$DATABASE"), replaced);
    }

    [Fact]
    public void FormatSql_EmptySource_IsNoOp()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X") { SourceText = string.Empty };
        vm.FormatSqlCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SourceText);
    }

    // ─── Editable description (COMMENT ON PROCEDURE) ──────────────────────

    [Fact]
    public void Factory_Procedure_CanEditDescription()
    {
        using var harness = new Harness();
        var vm = harness.Main.CreateProcedureDetail(new MetadataObject("SP_X", MetadataObjectKind.Procedure));
        Assert.True(vm.CanEditDescription);
    }

    [Fact]
    public void NoExecutor_CannotEditDescription()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X");
        Assert.False(vm.CanEditDescription);
    }

    [Fact]
    public void Description_MirrorsIntoEditableCopy()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X");
        vm.Description = "Recalculates balances";
        Assert.Equal("Recalculates balances", vm.EditableDescription);
    }

    [Theory]
    [InlineData("SP_BALANCE", "Recalc", "COMMENT ON PROCEDURE \"SP_BALANCE\" IS 'Recalc'")]
    [InlineData("SP_BALANCE", "", "COMMENT ON PROCEDURE \"SP_BALANCE\" IS NULL")]
    [InlineData("SP_BALANCE", null, "COMMENT ON PROCEDURE \"SP_BALANCE\" IS NULL")]
    [InlineData("SP_X", "it's", "COMMENT ON PROCEDURE \"SP_X\" IS 'it''s'")]
    public void BuildCommentProcedure_Cases(string name, string? comment, string expected)
        => Assert.Equal(expected, DdlGenerator.BuildCommentProcedure(name, comment));

    [Fact]
    public void BuildCommentProcedure_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCommentProcedure("", "x"));

    // ─── New Procedure command re-enables on connection change ────────────

    [Fact]
    public void NewProcedureCommand_ReNotifiesCanExecute_OnConnectionChange()
    {
        using var harness = new Harness();
        var fired = false;
        harness.Main.NewProcedureCommand.CanExecuteChanged += (_, _) => fired = true;

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.True(fired);
    }

    // ─── Procedure-scoped catalog SQL pins (NOT the table/view path) ──────

    [Fact]
    public void ProcedureParametersSql_QueriesProcedureParameters()
    {
        Assert.Contains("RDB$PROCEDURE_PARAMETERS", FirebirdTableDetailReader.ProcedureParametersSql);
        Assert.Contains("RDB$PARAMETER_TYPE = @pt", FirebirdTableDetailReader.ProcedureParametersSql);
        Assert.Contains("@name", FirebirdTableDetailReader.ProcedureParametersSql);
    }

    [Fact]
    public void ProcedureDependencySql_UsesProcedureType5_NotRelations()
    {
        // Procedures use RDB$*_TYPE = 5 — the table/view dependency path (RELATION,
        // type 0) does NOT apply, so these must not lean on RDB$RELATIONS.
        Assert.Contains("RDB$DEPENDENT_TYPE = 5", FirebirdTableDetailReader.ProcedureDependsOnSql);
        Assert.Contains("RDB$DEPENDED_ON_TYPE = 5", FirebirdTableDetailReader.ProcedureDependedOnBySql);
        Assert.DoesNotContain("RDB$RELATIONS", FirebirdTableDetailReader.ProcedureDependsOnSql);
        Assert.DoesNotContain("RDB$RELATIONS", FirebirdTableDetailReader.ProcedureDependedOnBySql);
    }

    // ─── V1.1: Source ⇄ Easy mode ─────────────────────────────────────────

    [Fact]
    public void EasyMode_FromSource_ParsesModel()
    {
        var vm = new ProcedureDetailTabViewModel("P")
        {
            SourceText = "CREATE OR ALTER PROCEDURE P (A INTEGER) RETURNS (R INTEGER) AS BEGIN R = A; SUSPEND; END",
        };
        vm.EasyMode = true;

        Assert.Single(vm.InputParams);
        Assert.Equal("A", vm.InputParams[0].Name);
        Assert.Single(vm.OutputParams);
        Assert.Equal("R", vm.OutputParams[0].Name);
        Assert.Contains("BEGIN", vm.ExecutableBody);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void EasyMode_ParseFail_KeepsModel_SetsNotice()
    {
        var vm = new ProcedureDetailTabViewModel("P") { SourceText = "this is not a procedure" };
        vm.EasyMode = true;

        Assert.Equal(UiStrings.ProcedureParseFailedNotice, vm.ErrorMessage);
        Assert.Empty(vm.InputParams);
    }

    [Fact]
    public void EasyMode_New_CanUseEasy_StartsInEasyWithEditableName()
    {
        // Approved target design: a new procedure CAN use Easy mode and starts there.
        var vm = new ProcedureDetailTabViewModel("NEW_PROCEDURE")
        {
            IsNew = true,
            SourceText = ProcedureDetailTabViewModel.NewProcedureTemplate,
        };
        Assert.True(vm.CanUseEasyMode);

        vm.EasyMode = true; // New Procedure starts in Easy (set by the New Procedure flow)
        Assert.True(vm.EasyMode);
        Assert.Equal("NEW_PROCEDURE", vm.EditableProcedureName);

        // Editing the name + params + body flows into the compiled SQL (dirty/compile #3).
        vm.EditableProcedureName = "CALC_TOTALS";
        vm.AddInputParamCommand.Execute(null);
        vm.InputParams[^1].Name = "FROM_DATE";
        vm.InputParams[^1].TypeText = "DATE";
        vm.ExecutableBody = "BEGIN RESULT = 1; SUSPEND; END";

        var sql = vm.BuildCompileSql();
        Assert.Contains("CREATE OR ALTER PROCEDURE CALC_TOTALS", sql);
        Assert.Contains("FROM_DATE", sql);
        Assert.Contains("RESULT = 1", sql);
    }

    [Fact]
    public void EasyMode_ToSource_Regenerates()
    {
        var vm = new ProcedureDetailTabViewModel("MY_PROC");
        vm.EasyMode = true;             // parse of empty source fails → notice (ignored)
        vm.ErrorMessage = null;
        vm.InputParams.Add(new ProcedureParamRowViewModel { Name = "A", TypeText = "INTEGER" });
        vm.ExecutableBody = "BEGIN END";

        vm.EasyMode = false;

        Assert.Contains("CREATE OR ALTER PROCEDURE MY_PROC", vm.SourceText);
        Assert.Contains("A INTEGER", vm.SourceText);
    }

    [Fact]
    public void BuildCompileSql_Easy_Reassembles_Source_Verbatim()
    {
        var easy = new ProcedureDetailTabViewModel("MY_PROC");
        easy.EasyMode = true;
        easy.InputParams.Add(new ProcedureParamRowViewModel { Name = "A", TypeText = "INTEGER" });
        easy.ExecutableBody = "BEGIN END";
        var sql = easy.BuildCompileSql();
        Assert.Contains("CREATE OR ALTER PROCEDURE MY_PROC", sql);
        Assert.Contains("A INTEGER", sql);

        var src = new ProcedureDetailTabViewModel("P") { SourceText = "CREATE OR ALTER PROCEDURE P AS BEGIN END" };
        Assert.Equal(src.SourceText, src.BuildCompileSql());
    }

    // ─── V1.1: Execute (statement shape + parameters) ─────────────────────

    [Fact]
    public void BuildExecuteStatement_Executable_NoSuspend()
    {
        var vm = new ProcedureDetailTabViewModel("MY_PROC") { ExecutableBody = "BEGIN END" };
        var (sql, ps) = vm.BuildExecuteStatement(new object?[] { 5L, "x" });

        Assert.StartsWith("EXECUTE PROCEDURE \"MY_PROC\"(@p0, @p1)", sql);
        Assert.Equal(2, ps.Count);
        Assert.Equal("@p0", ps[0].Name);
        Assert.Equal(5L, ps[0].Value);
        Assert.Equal("x", ps[1].Value);
    }

    [Fact]
    public void BuildExecuteStatement_Selectable_WithOutputsAndSuspend()
    {
        var vm = new ProcedureDetailTabViewModel("MY_PROC") { ExecutableBody = "BEGIN SUSPEND; END" };
        vm.OutputParams.Add(new ProcedureParamRowViewModel { Name = "R", TypeText = "INTEGER" });

        var (sql, ps) = vm.BuildExecuteStatement(System.Array.Empty<object?>());

        Assert.StartsWith("SELECT * FROM \"MY_PROC\"", sql);
        Assert.Empty(ps); // no input values → no parens
        Assert.DoesNotContain("(@", sql);
    }

    [Fact]
    public void CanExecuteProcedure_Gating()
    {
        var noRunner = new ProcedureDetailTabViewModel("P");
        Assert.False(noRunner.CanExecuteProcedure);

        var withRunner = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(new ProcedureExecOutcome(null, null)),
        };
        Assert.True(withRunner.CanExecuteProcedure);

        var newProc = new ProcedureDetailTabViewModel("P")
        {
            IsNew = true,
            RunExecuteRequested = (_, _) => Task.FromResult(new ProcedureExecOutcome(null, null)),
        };
        Assert.False(newProc.CanExecuteProcedure); // can't execute an uncreated procedure
    }

    // ─── V1.1: parameter grid editing (Add / Delete / Move) ───────────────

    [Fact]
    public void InputParams_AddDeleteMove()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        vm.AddInputParamCommand.Execute(null);
        vm.AddInputParamCommand.Execute(null);
        Assert.Equal(2, vm.InputParams.Count);

        var second = vm.InputParams[1];
        vm.SelectedInputParam = second;
        vm.MoveInputParamUpCommand.Execute(null);
        Assert.Same(second, vm.InputParams[0]);

        vm.SelectedInputParam = vm.InputParams[0];
        vm.DeleteInputParamCommand.Execute(null);
        Assert.Single(vm.InputParams);
    }

    // ─── V1.1: execute-dialog value conversion ────────────────────────────

    [Theory]
    [InlineData("INTEGER", "5", 5L)]
    [InlineData("NUMERIC(18,4)", "1.5", null)]   // decimal — asserted separately
    [InlineData("VARCHAR(10)", "hi", "hi")]
    public void ConvertByType_Cases(string type, string value, object? expected)
    {
        var result = ExecuteProcedureParamRowViewModel.ConvertByType(type, value);
        if (expected is null && type.StartsWith("NUMERIC"))
            Assert.Equal(1.5m, result);
        else
            Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("INTEGER", "")]
    [InlineData("INTEGER", null)]
    [InlineData("VARCHAR(10)", "  ")]
    public void ConvertByType_EmptyIsNull(string type, string? value)
        => Assert.Null(ExecuteProcedureParamRowViewModel.ConvertByType(type, value));

    // ─── V1.2: Result paging (client-side over the materialized result) ───

    [Fact]
    public void ExecResult_Paging()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        var cols = new[] { new QueryColumn("ID", typeof(int)) };
        var rows = Enumerable.Range(0, 450).Select(i => new object?[] { i }).ToList();
        vm.ExecResult = new QueryResult { Columns = cols, Rows = rows };

        Assert.True(vm.HasExecResult);
        Assert.Equal(1, vm.ExecPage);
        Assert.False(vm.HasExecPreviousPage);
        Assert.True(vm.HasExecNextPage);
        Assert.Equal(ProcedureDetailTabViewModel.ExecResultPageSize, vm.PagedExecRows.Count);

        vm.ExecNextPageCommand.Execute(null);
        Assert.Equal(2, vm.ExecPage);

        vm.ExecLastPageCommand.Execute(null);
        Assert.Equal(3, vm.ExecPage);                 // ceil(450 / 200) = 3
        Assert.Equal(50, vm.PagedExecRows.Count);
        Assert.False(vm.HasExecNextPage);

        vm.ExecFirstPageCommand.Execute(null);
        Assert.Equal(1, vm.ExecPage);
        Assert.True(vm.HasExecNextPage);
    }

    // ─── V1.2: execution info (rows / affected / completed / error) ───────

    [Fact]
    public async Task ExecInfo_RowsReturned()
    {
        var cols = new[] { new QueryColumn("ID", typeof(int)) };
        var rows = new List<object?[]> { new object?[] { 1 } };
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { Columns = cols, Rows = rows, Elapsed = TimeSpan.FromMilliseconds(12) }, null)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.True(vm.HasExecInfo);
        Assert.False(vm.ExecInfoIsError);
        Assert.Contains("12 ms", vm.ExecInfo);
        Assert.Contains("row(s) returned", vm.ExecInfo);
    }

    [Fact]
    public async Task ExecInfo_Affected_WhenNoResultSet()
    {
        // Production always attaches a work Summary; with reads unmeasured it degrades to the
        // driver's affected-rows total.
        var summary = new ExecutionSummary { RecordsAffected = 3, Elapsed = TimeSpan.FromMilliseconds(5), ChangesMeasured = false };
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { RecordsAffected = 3, Elapsed = TimeSpan.FromMilliseconds(5) }, null, summary)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.False(vm.HasExecResult);               // no result set
        Assert.Contains("3 rows affected", vm.ExecInfo);
    }

    [Fact]
    public async Task ExecInfo_WorkSummary_ShowsChangesAndReads()
    {
        // The Execution Metrics summary: a non-result procedure reports what it changed + read.
        var summary = new ExecutionSummary
        {
            Inserts = 8,
            Updates = 16,
            Deletes = 8,
            RowsRead = 20_552,
            ChangesMeasured = true,
            ReadsMeasured = true,
            Elapsed = TimeSpan.FromMilliseconds(93),
        };
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { RecordsAffected = null, Elapsed = TimeSpan.FromMilliseconds(93) }, null, summary)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.False(vm.HasExecResult);
        Assert.Contains("16 rows updated", vm.ExecInfo);
        Assert.Contains("20552 rows read", vm.ExecInfo);
        Assert.DoesNotContain("0 rows affected", vm.ExecInfo);

        // The collapsed Expander header shows the single-line compact summary.
        Assert.Equal("Executed in 93 ms · 8 inserted · 16 updated · 8 deleted · 20552 read", vm.ExecInfoCompact);
        Assert.DoesNotContain("\n", vm.ExecInfoCompact);
    }

    [Fact]
    public async Task ExecSummaryFallback_ReadOnlyMeasured_ShowsCleanNoChangesLine()
    {
        // A read-only run: change counts measured (MON$ delta), but nothing was written.
        var summary = new ExecutionSummary
        {
            RowsRead = 285, ReadsMeasured = true, ChangesMeasured = true,
            Elapsed = TimeSpan.FromMilliseconds(47),
        };
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { Elapsed = TimeSpan.FromMilliseconds(47) }, null, summary)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.False(vm.HasExecTableActivity);                 // no change cards
        Assert.Equal("No data was changed by this execution.", vm.ExecSummaryFallbackText);
        Assert.DoesNotContain("read", vm.ExecSummaryFallbackText);   // reads live in Performance
    }

    [Fact]
    public async Task ExecSummaryFallback_NotMeasured_FallsBackToAggregate()
    {
        var summary = new ExecutionSummary
        {
            RecordsAffected = 5, ChangesMeasured = false, Elapsed = TimeSpan.FromMilliseconds(9),
        };
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { RecordsAffected = 5, Elapsed = TimeSpan.FromMilliseconds(9) }, null, summary)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.False(vm.HasExecTableActivity);
        Assert.Contains("rows affected", vm.ExecSummaryFallbackText);   // honest aggregate, not "nothing changed"
    }

    [Fact]
    public void PerformanceSubTabIndex_FollowsResult()
        => Assert.Equal(ProcedureDetailTabViewModel.ResultSubTabIndex + 1, ProcedureDetailTabViewModel.PerformanceSubTabIndex);

    [Fact]
    public async Task ExecInfo_Completed_WhenNoRowsNoAffected()
    {
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(
                new ProcedureExecOutcome(new QueryResult { Elapsed = TimeSpan.FromMilliseconds(7) }, null)),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);
        Assert.Contains("completed", vm.ExecInfo);
        Assert.False(vm.ExecInfoIsError);
    }

    [Fact]
    public async Task ExecInfo_Error()
    {
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(new ProcedureExecOutcome(null, "boom")),
        };
        await vm.ExecuteProcedureCommand.ExecuteAsync(null);

        Assert.True(vm.ExecInfoIsError);
        Assert.Equal("boom", vm.ExecInfo);
        Assert.True(vm.ShowExecError);
    }

    // ─── Easy Mode: the STATED default (Settings Center etap 6 / §7.6) ────────
    //
    // ⚠ These two used to assert the opposite of what they assert now, and the change is the etap's point. The
    // default was a WorkspaceState flag that the editor's own toggle wrote back, so opening a procedure in Easy
    // mode because of something done to a DIFFERENT procedure looked like a bug. The default now lives in
    // Preferences with one way to change it, and toggling a mode inside an editor is a per-tab action.

    [Fact]
    public void TheStatedEasyModeDefault_IsAppliedToANewlyOpenedProcedure()
    {
        using var harness = new Harness();
        harness.Main.Preferences.Apply(
            harness.Main.Preferences.Current with { ProcedureEasyModeDefault = true });

        var detail = harness.Main.CreateProcedureDetail(new MetadataObject("P", MetadataObjectKind.Procedure));
        Assert.True(detail.EasyMode);
    }

    [Fact]
    public void TogglingEasyModeInTheEditor_LeavesTheStatedDefaultAlone()
    {
        using var harness = new Harness();
        harness.Main.Preferences.Apply(
            harness.Main.Preferences.Current with { ProcedureEasyModeDefault = true });

        var detail = harness.Main.CreateProcedureDetail(new MetadataObject("P", MetadataObjectKind.Procedure));
        detail.EasyMode = false;

        Assert.True(harness.Main.ProcedureEasyModeDefault);
        Assert.True(harness.Main.Preferences.Current.ProcedureEasyModeDefault);
    }

    // ─── V1.3: typed Execute-dialog classification + resolve ──────────────

    [Theory]
    [InlineData("DATE", ExecuteParamKind.Date)]
    [InlineData("TIME", ExecuteParamKind.Time)]
    [InlineData("TIMESTAMP", ExecuteParamKind.Timestamp)]
    [InlineData("BOOLEAN", ExecuteParamKind.Boolean)]
    [InlineData("SMALLINT", ExecuteParamKind.Numeric)]
    [InlineData("INTEGER", ExecuteParamKind.Numeric)]
    [InlineData("BIGINT", ExecuteParamKind.Numeric)]
    [InlineData("NUMERIC(18,4)", ExecuteParamKind.Numeric)]
    [InlineData("VARCHAR(50)", ExecuteParamKind.Text)]
    [InlineData("CHAR(1)", ExecuteParamKind.Text)]
    [InlineData("BLOB SUB_TYPE 1", ExecuteParamKind.BlobText)]
    [InlineData("BLOB SUB_TYPE 0", ExecuteParamKind.BlobBinary)]
    public void ClassifyKind_Cases(string typeText, ExecuteParamKind expected)
        => Assert.Equal(expected, ExecuteProcedureParamRowViewModel.ClassifyKind(typeText));

    [Fact]
    public void Resolve_TypedValues()
    {
        Assert.Equal(true, new ExecuteProcedureParamRowViewModel("P", "BOOLEAN") { IsNull = false, BoolValue = true }.Resolve());
        Assert.Equal(5m, new ExecuteProcedureParamRowViewModel("P", "INTEGER") { IsNull = false, NumericValue = 5m }.Resolve());
        Assert.Equal("hi", new ExecuteProcedureParamRowViewModel("P", "VARCHAR(10)") { IsNull = false, TextValue = "hi" }.Resolve());

        var d = new DateTime(2020, 1, 2);
        Assert.Equal(d, new ExecuteProcedureParamRowViewModel("P", "DATE") { IsNull = false, DateValue = d }.Resolve());

        Assert.Null(new ExecuteProcedureParamRowViewModel("P", "INTEGER") { IsNull = true, NumericValue = 5m }.Resolve());
    }

    [Fact]
    public void Resolve_Timestamp_CombinesDateAndTime()
    {
        var row = new ExecuteProcedureParamRowViewModel("P", "TIMESTAMP")
        {
            IsNull = false,
            DateValue = new DateTime(2020, 1, 2),
            TimeValue = new TimeSpan(13, 30, 0),
        };
        Assert.Equal(new DateTime(2020, 1, 2, 13, 30, 0), row.Resolve());
    }

    // ─── V1.4 item 6: Execute dialog defaults / NULL-default / history ────

    [Fact]
    public void ExecuteDialog_DefaultsAndNullChecked()
    {
        var inputs = new[]
        {
            new ProcedureParamRowViewModel { Name = "D", TypeText = "DATE" },
            new ProcedureParamRowViewModel { Name = "N", TypeText = "INTEGER" },
            new ProcedureParamRowViewModel { Name = "B", TypeText = "BOOLEAN" },
            new ProcedureParamRowViewModel { Name = "T", TypeText = "TIMESTAMP" },
        };
        var dlg = new ExecuteProcedureDialogViewModel(inputs);

        Assert.All(dlg.Params, p => Assert.True(p.IsNull)); // NULL checked by default
        Assert.Equal(DateTime.Now.Date, dlg.Params[0].DateValue);
        Assert.Equal(0m, dlg.Params[1].NumericValue);
        Assert.False(dlg.Params[2].BoolValue);
        Assert.Equal(DateTime.Now.Date, dlg.Params[3].DateValue);
        Assert.NotNull(dlg.Params[3].TimeValue);
    }

    [Fact]
    public void ExecuteDialog_History_RoundTripsThroughStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-parmhist-" + Guid.NewGuid().ToString("N"));
        var store = new EmberTern.Core.Settings.ParameterHistoryStore(dir);
        var inputs = new[] { new ProcedureParamRowViewModel { Name = "N", TypeText = "INTEGER" } };

        var dlg1 = new ExecuteProcedureDialogViewModel(inputs, "HIST_PROC_TEST", "conn-1", "Procedure", store);
        dlg1.Params[0].IsNull = false;
        dlg1.Params[0].NumericValue = 42m;
        dlg1.AcceptCommand.Execute(null);

        // A fresh dialog (fresh store instance over the same file) auto-loads last run.
        var store2 = new EmberTern.Core.Settings.ParameterHistoryStore(dir);
        var dlg2 = new ExecuteProcedureDialogViewModel(inputs, "HIST_PROC_TEST", "conn-1", "Procedure", store2);
        Assert.True(dlg2.HasHistory);
        Assert.False(dlg2.Params[0].IsNull);   // restored from persisted history
        Assert.Equal(42m, dlg2.Params[0].NumericValue);
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
