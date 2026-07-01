using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Metadata Tree &amp; Context Menu sprint — DDL builders (DROP + ALTER TRIGGER
/// ACTIVE/INACTIVE), the batch-results report, and the tree context-menu gates /
/// commands / dispatch events. Pure logic — no live DB.
/// </summary>
public class MetadataTreeContextMenuTests
{
    // ─── DdlGenerator: DROP builders + dispatcher ─────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.View, "DROP VIEW")]
    [InlineData(MetadataObjectKind.Procedure, "DROP PROCEDURE")]
    [InlineData(MetadataObjectKind.Trigger, "DROP TRIGGER")]
    [InlineData(MetadataObjectKind.Function, "DROP FUNCTION")]
    [InlineData(MetadataObjectKind.Table, "DROP TABLE")]
    [InlineData(MetadataObjectKind.Package, "DROP PACKAGE")]
    [InlineData(MetadataObjectKind.Generator, "DROP SEQUENCE")]
    [InlineData(MetadataObjectKind.Domain, "DROP DOMAIN")]
    [InlineData(MetadataObjectKind.Exception, "DROP EXCEPTION")]
    [InlineData(MetadataObjectKind.Index, "DROP INDEX")]
    public void BuildDrop_DispatchesPerKind(MetadataObjectKind kind, string expectedPrefix)
    {
        var sql = DdlGenerator.BuildDrop(kind, "MY_OBJ");
        Assert.StartsWith(expectedPrefix, sql);
        Assert.Contains("MY_OBJ", sql);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Role)]
    [InlineData(MetadataObjectKind.User)]
    [InlineData(MetadataObjectKind.SystemTable)]
    public void BuildDrop_NoTreeDropPath_Throws(MetadataObjectKind kind)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DdlGenerator.BuildDrop(kind, "X"));

    [Fact]
    public void BuildDrop_QuotesIdentifier()
    {
        // DROP builders use the always-quote Quote (same as BuildDropTable/BuildDropIndex).
        Assert.Equal("DROP VIEW \"MY_VIEW\"", DdlGenerator.BuildDropView("MY_VIEW"));
        Assert.Equal("DROP VIEW \"my view\"", DdlGenerator.BuildDropView("my view"));
    }

    [Fact]
    public void BuildDrop_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropView(""));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropProcedure("  "));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropTrigger(""));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropFunction(""));
    }

    // ─── DdlGenerator: ALTER TRIGGER ACTIVE/INACTIVE ──────────────────────

    [Fact]
    public void BuildAlterTrigger_ActiveInactive()
    {
        Assert.Equal("ALTER TRIGGER \"MY_TRG\" ACTIVE", DdlGenerator.BuildAlterTriggerActive("MY_TRG"));
        Assert.Equal("ALTER TRIGGER \"MY_TRG\" INACTIVE", DdlGenerator.BuildAlterTriggerInactive("MY_TRG"));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAlterTriggerActive(""));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAlterTriggerInactive(" "));
    }

    // ─── Reader: trigger/index listing carries the inactive column ────────

    [Fact]
    public void SqlFor_TriggerAndIndex_IncludeInactiveColumn()
    {
        Assert.Contains("RDB$TRIGGER_INACTIVE", FirebirdMetadataReader.SqlFor(MetadataObjectKind.Trigger));
        Assert.Contains("RDB$INDEX_INACTIVE", FirebirdMetadataReader.SqlFor(MetadataObjectKind.Index));
        // Other kinds stay single-column (name only).
        Assert.DoesNotContain("_INACTIVE", FirebirdMetadataReader.SqlFor(MetadataObjectKind.Procedure));
    }

    // ─── Live BatchResultsViewModel ───────────────────────────────────────

    [Fact]
    public void BatchResults_LiveCounters_TrackAddResult()
    {
        var vm = new BatchResultsViewModel("Recompile procedures");
        vm.Begin(total: 3);
        Assert.Equal(3, vm.Total);
        Assert.True(vm.IsRunning);

        vm.AddResult(new BatchOperationResult("SP_A", "Recompile", true, null));
        vm.AddResult(new BatchOperationResult("SP_B", "Recompile", false, "error X"));
        vm.AddResult(new BatchOperationResult("SP_C", "Recompile", true, null));

        Assert.Equal(3, vm.Processed);
        Assert.Equal(2, vm.SuccessCount);
        Assert.Equal(1, vm.FailedCount);
        Assert.Contains("Processed: 3 / 3", vm.StatusSummary);
        Assert.Contains("Failed: 1", vm.StatusSummary);

        vm.Complete();
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void BatchResults_Filter_ShowsSuccessOrFailedOnly()
    {
        var vm = new BatchResultsViewModel("t");
        vm.Begin(3);
        vm.AddResult(new BatchOperationResult("A", "Recompile", true, null));
        vm.AddResult(new BatchOperationResult("B", "Recompile", false, "boom"));
        vm.AddResult(new BatchOperationResult("C", "Recompile", true, null));

        Assert.Equal(3, vm.VisibleRows.Count);               // All
        vm.SelectedFilterIndex = 1;                          // Success
        Assert.Equal(2, vm.VisibleRows.Count);
        Assert.All(vm.VisibleRows, r => Assert.False(r.IsFailed));
        vm.SelectedFilterIndex = 2;                          // Failed
        Assert.Single(vm.VisibleRows);
        Assert.True(vm.VisibleRows[0].IsFailed);
        vm.SelectedFilterIndex = 0;                          // back to All
        Assert.Equal(3, vm.VisibleRows.Count);
    }

    [Fact]
    public void BatchResults_CopyFailed_TsvOfFailedRowsOnly()
    {
        var vm = new BatchResultsViewModel("t");
        vm.Begin(3);
        vm.AddResult(new BatchOperationResult("PROC_A", "Recompile", true, null));
        vm.AddResult(new BatchOperationResult("PROC_IMPORT", "Recompile", false, "Token unknown"));
        vm.AddResult(new BatchOperationResult("PROC_SYNC", "Recompile", false, "Object XXX not found"));

        var all = vm.BuildClipboardText(failedOnly: false);
        Assert.Contains("PROC_A", all);
        Assert.Equal(3, all.TrimEnd('\n').Split('\n').Length);

        var failed = vm.BuildClipboardText(failedOnly: true);
        Assert.DoesNotContain("PROC_A", failed);
        Assert.Contains("PROC_IMPORT\tRecompile\tFailed\tToken unknown", failed);
        Assert.Contains("PROC_SYNC\tRecompile\tFailed\tObject XXX not found", failed);
        Assert.Equal(2, failed.TrimEnd('\n').Split('\n').Length);

        string? copied = null;
        vm.CopyRequested += t => copied = t;
        vm.CopyFailedCommand.Execute(null);
        Assert.Equal(failed, copied);
    }

    [Fact]
    public void BatchResults_Cancel_SignalsToken()
    {
        var vm = new BatchResultsViewModel("t");
        vm.Begin(10);
        Assert.False(vm.CancellationToken.IsCancellationRequested);
        vm.CancelCommand.Execute(null);
        Assert.True(vm.CancellationToken.IsCancellationRequested);
    }

    // ─── Preparation phase (dialog opens up front, before execution) ──────────

    [Fact]
    public void BatchResults_StartsInPreparingState()
    {
        var vm = new BatchResultsViewModel("Recompile all objects");
        Assert.True(vm.IsPreparing);
        Assert.False(vm.IsRunning);
        Assert.False(vm.PreparationFailed);
        Assert.True(vm.CanCancel);                 // preparation is cancellable
        Assert.Equal(UiStrings.BatchPreparing, vm.PreparationStatus);
    }

    [Fact]
    public void BatchResults_ReportPreparation_Determinate_TracksProgress()
    {
        var vm = new BatchResultsViewModel("t");
        vm.ReportPreparation(143, 1965, "Loading procedures 143 / 1965");
        Assert.Equal(143, vm.PreparationValue);
        Assert.Equal(1965, vm.PreparationTotal);
        Assert.False(vm.PreparationIsIndeterminate);
        Assert.Equal("Loading procedures 143 / 1965", vm.PreparationStatus);
    }

    [Fact]
    public void BatchResults_ReportPreparation_String_IsIndeterminate()
    {
        var vm = new BatchResultsViewModel("t");
        vm.ReportPreparation(1, 10, "x");          // determinate first
        vm.ReportPreparation("Building operation list…");
        Assert.True(vm.PreparationIsIndeterminate);
        Assert.Equal("Building operation list…", vm.PreparationStatus);
    }

    [Fact]
    public void BatchResults_Begin_ExitsPreparingIntoExecution()
    {
        var vm = new BatchResultsViewModel("t");
        vm.ReportPreparation(5, 10, "half");
        vm.Begin(total: 4);
        Assert.False(vm.IsPreparing);
        Assert.True(vm.IsRunning);
        Assert.True(vm.CanCancel);
        Assert.Equal(4, vm.Total);
    }

    [Fact]
    public void BatchResults_FailPreparation_ShowsErrorAndDisablesCancel()
    {
        var vm = new BatchResultsViewModel("t");
        vm.FailPreparation("Could not read the object list.");
        Assert.True(vm.IsPreparing);               // panel stays visible …
        Assert.True(vm.PreparationFailed);         // … showing the error
        Assert.False(vm.PreparationIsIndeterminate);
        Assert.False(vm.IsRunning);
        Assert.False(vm.CanCancel);                // only Close remains
        Assert.Equal("Could not read the object list.", vm.PreparationStatus);
    }

    [Fact]
    public void BatchResults_Cancel_DuringPreparation_SignalsToken()
    {
        var vm = new BatchResultsViewModel("t");   // starts preparing, no Begin yet
        Assert.True(vm.CanCancel);
        Assert.False(vm.CancellationToken.IsCancellationRequested);
        vm.CancelCommand.Execute(null);
        Assert.True(vm.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void BatchResults_Complete_EndsPreparingAndRunning()
    {
        var vm = new BatchResultsViewModel("t");
        vm.Begin(2);
        vm.Complete();
        Assert.False(vm.IsPreparing);
        Assert.False(vm.IsRunning);
        Assert.False(vm.CanCancel);
    }

    [Fact]
    public void BatchResults_CanCancel_RaisesChangeNotifications()
    {
        var vm = new BatchResultsViewModel("t");
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanCancel)) raised++; };
        vm.Begin(1);            // IsPreparing false + IsRunning true → CanCancel notified
        vm.Complete();          // IsRunning false → CanCancel notified
        Assert.True(raised >= 2);
    }

    // ─── Node gates ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Table, true)]
    [InlineData(MetadataObjectKind.View, true)]
    [InlineData(MetadataObjectKind.Procedure, true)]
    [InlineData(MetadataObjectKind.User, true)]
    [InlineData(MetadataObjectKind.Role, true)]
    [InlineData(MetadataObjectKind.Index, false)]       // created inside Table Detail
    [InlineData(MetadataObjectKind.SystemTable, false)] // read-only
    public void Group_SupportsNew(MetadataObjectKind kind, bool expected)
    {
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, kind);
        Assert.Equal(expected, group.SupportsNew);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Procedure, true)]
    [InlineData(MetadataObjectKind.Function, true)]
    [InlineData(MetadataObjectKind.Trigger, true)]
    [InlineData(MetadataObjectKind.Package, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.View, false)]
    public void Group_IsRecompilable(MetadataObjectKind kind, bool expected)
    {
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, kind);
        Assert.Equal(expected, group.IsRecompilableGroup);
    }

    [Fact]
    public void TriggerGroup_IsTriggerGroup_LeafIsNot()
    {
        using var h = new Harness();
        Assert.True(MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Trigger).IsTriggerGroup);
        Assert.False(Leaf(h, "T", MetadataObjectKind.Trigger).IsTriggerGroup);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table, true)]
    [InlineData(MetadataObjectKind.View, true)]
    [InlineData(MetadataObjectKind.Index, true)]
    [InlineData(MetadataObjectKind.SystemTable, false)] // read-only: no delete
    [InlineData(MetadataObjectKind.Role, false)]        // via Security Manager
    [InlineData(MetadataObjectKind.User, false)]
    public void Leaf_CanDelete(MetadataObjectKind kind, bool expected)
    {
        using var h = new Harness();
        Assert.Equal(expected, Leaf(h, "X", kind).CanDeleteLeaf);
    }

    [Fact]
    public void ContextEditLabel_KindSpecific()
    {
        using var h = new Harness();
        Assert.Equal(UiStrings.MetadataContextOpenSecurity, Leaf(h, "U", MetadataObjectKind.User).ContextEditLabel);
        Assert.Equal(UiStrings.MetadataContextOpen, Leaf(h, "S", MetadataObjectKind.SystemTable).ContextEditLabel);
        Assert.Equal(UiStrings.MetadataContextEdit, Leaf(h, "V", MetadataObjectKind.View).ContextEditLabel);
    }

    [Fact]
    public void ContextNewLabel_UsesKindNoun()
    {
        using var h = new Harness();
        Assert.Equal("New View", MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.View).ContextNewLabel);
    }

    [Fact]
    public void InactiveTrigger_DimmedAndSuffixed()
    {
        using var h = new Harness();
        var active = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("TR_A", MetadataObjectKind.Trigger) { IsActive = true });
        var inactive = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("TR_B", MetadataObjectKind.Trigger) { IsActive = false });

        Assert.False(active.IsInactive);
        Assert.Equal("TR_A", active.DisplayLabel);
        Assert.True(active.ShowDeactivate);
        Assert.False(active.ShowActivate);

        Assert.True(inactive.IsInactive);
        Assert.Equal("TR_B" + UiStrings.MetadataInactiveSuffix, inactive.DisplayLabel);
        Assert.True(inactive.ShowActivate);
        Assert.False(inactive.ShowDeactivate);
    }

    // ─── Node commands → explorer dispatch events ─────────────────────────

    [Fact]
    public void NewCommand_FiresNewObjectRequested()
    {
        using var h = new Harness();
        MetadataObjectKind? fired = null;
        h.Main.Metadata.NewObjectRequested += k => fired = k;
        MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Procedure).NewCommand.Execute(null);
        Assert.Equal(MetadataObjectKind.Procedure, fired);
    }

    [Fact]
    public void DeleteCommand_FiresDeleteObjectRequested()
    {
        using var h = new Harness();
        MetadataObject? fired = null;
        h.Main.Metadata.DeleteObjectRequested += o => fired = o;
        Leaf(h, "MY_V", MetadataObjectKind.View).DeleteCommand.Execute(null);
        Assert.Equal("MY_V", fired?.Name);
    }

    [Fact]
    public void ExecuteProcedureCommand_FiresOnlyForProcedureLeaf()
    {
        using var h = new Harness();
        MetadataObject? fired = null;
        h.Main.Metadata.ExecuteProcedureRequested += o => fired = o;
        Leaf(h, "SP", MetadataObjectKind.Procedure).ExecuteProcedureCommand.Execute(null);
        Assert.Equal("SP", fired?.Name);
    }

    [Fact]
    public void ActivateDeactivateCommands_FireSetObjectActive()
    {
        using var h = new Harness();
        var events = new List<(string Name, bool Activate)>();
        h.Main.Metadata.SetObjectActiveRequested += (o, a) => events.Add((o.Name, a));
        var leaf = MetadataNodeViewModel.CreateLeaf(h.Main.Metadata,
            new MetadataObject("TR", MetadataObjectKind.Trigger) { IsActive = false });
        leaf.ActivateCommand.Execute(null);
        leaf.DeactivateCommand.Execute(null);
        Assert.Equal(new[] { ("TR", true), ("TR", false) }, events);
    }

    [Fact]
    public void RecompileAllCommand_FiresRecompileGroup()
    {
        using var h = new Harness();
        MetadataObjectKind? fired = null;
        h.Main.Metadata.RecompileGroupRequested += k => fired = k;
        MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Function).RecompileAllCommand.Execute(null);
        Assert.Equal(MetadataObjectKind.Function, fired);
    }

    [Fact]
    public void BulkVisible_PassesFilteredNames_BulkAll_PassesEmpty()
    {
        using var h = new Harness();
        TriggerBulkRequest? req = null;
        h.Main.Metadata.BulkSetActiveRequested += r => req = r;

        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Trigger);
        group.SetLeaves(new[]
        {
            MetadataNodeViewModel.CreateLeaf(h.Main.Metadata, new MetadataObject("TR_KON", MetadataObjectKind.Trigger) { IsActive = true }),
            MetadataNodeViewModel.CreateLeaf(h.Main.Metadata, new MetadataObject("TR_ART", MetadataObjectKind.Trigger) { IsActive = true }),
        });
        group.MarkLoaded();
        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON"); // Children → [TR_KON]

        group.DeactivateVisibleCommand.Execute(null);
        Assert.NotNull(req);
        Assert.True(req!.VisibleOnly);
        Assert.False(req.Activate);
        Assert.Equal(new[] { "TR_KON" }, req.VisibleNames);

        req = null;
        group.ActivateAllCommand.Execute(null);
        Assert.NotNull(req);
        Assert.False(req!.VisibleOnly);
        Assert.True(req.Activate);
        Assert.Empty(req.VisibleNames);
    }

    // ─── ShowEditorToolbar (no tab → hidden) ──────────────────────────────

    [Fact]
    public void ShowEditorToolbar_FalseWhenNoTabActive()
    {
        using var h = new Harness();
        Assert.False(h.Main.ShowEditorToolbar); // empty workspace → no command strip
    }

    private static MetadataNodeViewModel Leaf(Harness h, string name, MetadataObjectKind kind)
        => MetadataNodeViewModel.CreateLeaf(h.Main.Metadata, new MetadataObject(name, kind));

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
