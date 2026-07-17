using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Increment 1 of the data-loss protection model: the unified WorkGuard + the
/// multi-button ChoiceDialog. Covers per-tab unsaved-work detection (IUnsavedWorkSource),
/// the dirty flag on the source editors, tab-close prompting for every kind, and the
/// app-close guard. Auto-draft (Increment 2) is tested separately.
/// </summary>
public class DataLossGuardTests
{
    // ─── ChoiceDialogViewModel (pure) ─────────────────────────────────────

    [Fact]
    public void ChoiceDialog_Choose_SetsResultAndRaisesClose()
    {
        var vm = new ChoiceDialogViewModel(new ChoiceRequest
        {
            Title = "T",
            Message = "M",
            Options = new[]
            {
                new ChoiceOption { Id = "commit", Label = "Commit" },
                new ChoiceOption { Id = "cancel", Label = "Cancel", IsCancel = true },
            },
        });
        bool closed = false;
        vm.RequestClose += () => closed = true;

        Assert.Equal(2, vm.Options.Count);
        vm.Options[0].InvokeCommand.Execute(null);

        Assert.Equal("commit", vm.Result);
        Assert.True(closed);
    }

    [Fact]
    public void ChoiceOption_Flags_MapThrough()
    {
        var vm = new ChoiceDialogViewModel(new ChoiceRequest
        {
            Options = new[]
            {
                new ChoiceOption { Id = "rollback", Label = "Roll back", IsDefault = true },
                new ChoiceOption { Id = "cancel", Label = "Cancel", IsCancel = true },
            },
        });
        Assert.True(vm.Options[0].IsDefault);
        Assert.False(vm.Options[0].IsNotDefault);
        Assert.True(vm.Options[1].IsCancel);
        Assert.True(vm.Options[1].IsNotDefault);
    }

    // ─── Per-tab unsaved-work detection ───────────────────────────────────

    [Fact]
    public void NewTable_GetUnsavedWork_NullWhenEmpty_ItemWhenContent()
    {
        Assert.Null(new NewTableTabViewModel().GetUnsavedWork());

        var named = new NewTableTabViewModel { TableName = "CUSTOMERS" };
        var work = named.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.NewObject, work!.Kind);
        Assert.Contains("CUSTOMERS", work.Label);
    }

    [Fact]
    public void TableDetail_GetUnsavedWork_PendingStructure()
    {
        var vm = new TableDetailTabViewModel("ORDERS");
        Assert.Null(vm.GetUnsavedWork());

        vm.PendingChanges.Add(new PendingDdlChange { Kind = PendingDdlChangeKind.AddField, Sql = "ALTER ..." });
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.PendingStructure, work!.Kind);
        Assert.Contains("ORDERS", work.Label);
    }

    [Fact]
    public void View_ExistingFresh_IsClean_UntilEdited()
    {
        var vm = new ViewDetailTabViewModel("V_SALES");
        Assert.False(vm.IsDirty);
        Assert.Null(vm.GetUnsavedWork());

        vm.SourceText = "SELECT 1 FROM RDB$DATABASE";
        Assert.True(vm.IsDirty);
        var work = vm.GetUnsavedWork();
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
        Assert.Contains("V_SALES", work.Label);
    }

    [Fact]
    public void View_NewUntouched_IsClean_NewEdited_IsNewObject()
    {
        var vm = new ViewDetailTabViewModel("NEW_VIEW") { IsNew = true, SourceText = ViewDetailTabViewModel.NewViewTemplate };
        // Mirror the New View flow: seeding marks dirty, the owner clears it.
        vm.ClearDirty();
        Assert.Null(vm.GetUnsavedWork());

        vm.SourceText += "\n-- edited";
        var work = vm.GetUnsavedWork();
        Assert.Equal(UnsavedWorkKind.NewObject, work!.Kind);
    }

    [Fact]
    public void View_ModeToggle_DoesNotMarkDirty()
    {
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = "CREATE OR ALTER VIEW V_X (A) AS SELECT 1 A FROM RDB$DATABASE" };
        vm.ClearDirty();
        vm.EasyMode = true;   // Source → Easy
        vm.EasyMode = false;  // Easy → Source
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Procedure_ExistingFresh_IsClean_UntilEdited()
    {
        var vm = new ProcedureDetailTabViewModel("SP_BALANCE");
        Assert.False(vm.IsDirty);
        Assert.Null(vm.GetUnsavedWork());

        vm.SourceText = "CREATE OR ALTER PROCEDURE SP_BALANCE AS BEGIN END";
        var work = vm.GetUnsavedWork();
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
        Assert.Contains("SP_BALANCE", work.Label);
    }

    [Fact]
    public void Procedure_NewUntouched_IsClean_NewEdited_IsNewObject()
    {
        var vm = new ProcedureDetailTabViewModel("NEW_PROC") { IsNew = true, SourceText = ProcedureDetailTabViewModel.NewProcedureTemplate };
        vm.ClearDirty();
        Assert.Null(vm.GetUnsavedWork());

        vm.SourceText += "\n-- edited";
        Assert.Equal(UnsavedWorkKind.NewObject, vm.GetUnsavedWork()!.Kind);
    }

    // ─── WorkGuard aggregation + tab-close (harness) ──────────────────────

    [Fact]
    public void CollectUnsavedWork_ListsOnlyDirtyTabs()
    {
        using var h = new Harness();
        var dirtyTab = ViewTab(h, "V_DIRTY", dirty: true);
        var cleanTab = ViewTab(h, "V_CLEAN", dirty: false);
        h.Main.WorkspaceTabs.Add(dirtyTab);
        h.Main.WorkspaceTabs.Add(cleanTab);

        var items = h.Main.CollectUnsavedWork();
        Assert.Single(items);
        Assert.Contains("V_DIRTY", items[0].Label);
    }

    [Fact]
    public async Task RequestCloseTab_CleanTab_ClosesWithoutPrompt()
    {
        using var h = new Harness();
        var tab = ViewTab(h, "V_CLEAN", dirty: false);
        h.Main.WorkspaceTabs.Add(tab);
        bool prompted = false;
        h.Main.ConfirmationRequested += _ => { prompted = true; return Task.FromResult(true); };

        await h.Main.RequestCloseTabAsync(tab);

        Assert.False(prompted);
        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public async Task RequestCloseTab_DirtyView_Cancelled_KeepsTab()
    {
        using var h = new Harness();
        var tab = ViewTab(h, "V_DIRTY", dirty: true);
        h.Main.WorkspaceTabs.Add(tab);
        ConfirmRequest? seen = null;
        h.Main.ConfirmationRequested += req => { seen = req; return Task.FromResult(false); };

        await h.Main.RequestCloseTabAsync(tab);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("V_DIRTY", seen.Message);
        Assert.Contains(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public async Task RequestCloseTab_DirtyView_Confirmed_ClosesTab()
    {
        using var h = new Harness();
        var tab = ViewTab(h, "V_DIRTY", dirty: true);
        h.Main.WorkspaceTabs.Add(tab);
        h.Main.ConfirmationRequested += _ => Task.FromResult(true);

        await h.Main.RequestCloseTabAsync(tab);

        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    // ─── App-close guard ──────────────────────────────────────────────────

    [Fact]
    public async Task TryCloseApplication_NothingUnsaved_ReturnsTrue_NoDialog()
    {
        using var h = new Harness();
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V", dirty: false));
        bool prompted = false;
        h.Main.ChoiceRequested += _ => { prompted = true; return Task.FromResult<string?>("cancel"); };

        Assert.True(await h.Main.TryCloseApplicationAsync());
        Assert.False(prompted);
    }

    [Fact]
    public async Task TryCloseApplication_Unsaved_Discard_ReturnsTrue()
    {
        using var h = new Harness();
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        ChoiceRequest? seen = null;
        h.Main.ChoiceRequested += req => { seen = req; return Task.FromResult<string?>("discard"); };

        Assert.True(await h.Main.TryCloseApplicationAsync());
        Assert.NotNull(seen);
        Assert.Contains("V_DIRTY", seen!.Message);
        // A savable dirty editor adds a "Save and exit" option: cancel (default) / save / discard.
        Assert.Equal(3, seen.Options.Count);
        Assert.Contains(seen.Options, o => o.Id == "cancel" && o.IsDefault);
        Assert.Contains(seen.Options, o => o.Id == "save");
        Assert.Contains(seen.Options, o => o.Id == "discard");
    }

    [Fact]
    public async Task TryCloseApplication_Unsaved_Save_AllSucceed_ReturnsTrue()
    {
        using var h = new Harness();
        // A dirty View with no DDL executor: SaveAsync compiles a no-op and reports success,
        // so the save-all batch completes cleanly and the app may close.
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        h.Main.ChoiceRequested += _ => Task.FromResult<string?>("save");

        Assert.True(await h.Main.TryCloseApplicationAsync());
    }

    [Fact]
    public async Task TryCloseApplication_Unsaved_Save_Fails_KeepsAppOpenAndSelectsTab()
    {
        using var h = new Harness();
        // A New Table with a name but no named field: IsValid() fails, so SaveAsync fails
        // deterministically (no database needed) — the app must stay open.
        var newTable = new NewTableTabViewModel { TableName = "BADTABLE" };
        var tab = WorkspaceTabViewModel.CreateNewTable(h.Main, newTable, null);
        h.Main.WorkspaceTabs.Add(tab);
        h.Main.ChoiceRequested += _ => Task.FromResult<string?>("save");

        Assert.False(await h.Main.TryCloseApplicationAsync());
        Assert.Contains(tab, h.Main.WorkspaceTabs);
        Assert.True(tab.IsSelected); // the offending tab is brought into focus
    }

    [Fact]
    public async Task TryCloseApplication_Unsaved_Cancel_ReturnsFalse()
    {
        using var h = new Harness();
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        h.Main.ChoiceRequested += _ => Task.FromResult<string?>("cancel");

        Assert.False(await h.Main.TryCloseApplicationAsync());
    }

    [Fact]
    public async Task TryCloseApplication_Unsaved_Dismissed_ReturnsFalse()
    {
        using var h = new Harness();
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        // No handler → RequestChoiceAsync returns null → treated as cancel.
        Assert.False(await h.Main.TryCloseApplicationAsync());
    }

    // ─── Disconnect with unsaved metadata editors (no transaction) ────────

    [Fact]
    public async Task Disconnect_UnsavedNoTx_OffersSaveDiscardCancel()
    {
        using var h = new Harness();
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        ChoiceRequest? seen = null;
        // Cancel keeps the connection: the guard's phase-1 dialog is a 3-way choice now.
        h.Main.ChoiceRequested += req => { seen = req; return Task.FromResult<string?>("cancel"); };

        await h.Main.DisconnectAsync();

        Assert.NotNull(seen);
        Assert.Contains("V_DIRTY", seen!.Message);
        Assert.Contains(seen.Options, o => o.Id == "save" && o.IsDefault);
        Assert.Contains(seen.Options, o => o.Id == "discard");
        Assert.Contains(seen.Options, o => o.Id == "cancel" && o.IsCancel);
    }

    [Fact]
    public async Task Disconnect_UnsavedNoTx_Save_AllSucceed_NoError()
    {
        using var h = new Harness();
        // Dirty View with no executor → SaveAsync is a successful no-op → disconnect proceeds
        // (with no active connection, DisconnectAsync is a harmless no-op) without prompting twice.
        h.Main.WorkspaceTabs.Add(ViewTab(h, "V_DIRTY", dirty: true));
        int prompts = 0;
        h.Main.ChoiceRequested += _ => { prompts++; return Task.FromResult<string?>("save"); };

        await h.Main.DisconnectAsync();

        Assert.Equal(1, prompts); // only the phase-1 metadata dialog; no transaction phase
    }

    // ─── ISavableObjectEditor adapter + tab exposure ──────────────────────

    [Fact]
    public void SavableEditor_ExposedForEditorTabs_NullForOthers()
    {
        using var h = new Harness();
        var viewTab = ViewTab(h, "V", dirty: false);
        Assert.NotNull(viewTab.SavableEditor);

        var queryTab = WorkspaceTabViewModel.CreateQuery(h.Main);
        Assert.Null(queryTab.SavableEditor);
    }

    [Fact]
    public async Task ViewEditor_SaveAsync_NoExecutor_ReportsSuccess()
    {
        // No DDL executor wired → the compile is a no-op that raises no error, so the
        // adapter reports success (nothing to fail on).
        var vm = new ViewDetailTabViewModel("V") { SourceText = "SELECT 1 FROM RDB$DATABASE" };
        var result = await vm.SaveAsync();
        Assert.True(result.Success);
    }

    [Fact]
    public async Task NewTable_SaveAsync_InvalidState_ReturnsFailure()
    {
        // Empty table name → IsValid() fails, so SaveAsync fails deterministically (before
        // touching the owner create path) and surfaces the validation message.
        var vm = new NewTableTabViewModel();
        var result = await vm.SaveAsync();
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static WorkspaceTabViewModel ViewTab(Harness h, string name, bool dirty)
    {
        var detail = new ViewDetailTabViewModel(name);
        if (dirty) detail.SourceText = "SELECT 1 FROM RDB$DATABASE"; else detail.ClearDirty();
        var obj = new MetadataObject(name, MetadataObjectKind.View);
        return WorkspaceTabViewModel.CreateViewDetail(h.Main, obj, detail, null);
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new EmberTern.Core.Connections.ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public EmberTern.Core.Connections.ConnectionProfileStore Store { get; }
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
