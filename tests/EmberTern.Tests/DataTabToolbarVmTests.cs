using System;
using System.IO;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class DataTabToolbarVmTests
{
    [Fact]
    public void TableDetailVm_DefaultActiveSubTab_IsDataSubTabActiveFalse()
    {
        var td = new TableDetailTabViewModel("T");
        Assert.False(td.IsDataSubTabActive);
    }

    [Fact]
    public void TableDetailVm_ActiveSubTabSetToDataIndex_FlipsIsDataSubTabActiveTrue()
    {
        var td = new TableDetailTabViewModel("T");
        td.ActiveSubTabIndex = TableDetailTabViewModel.DataSubTabIndex;
        Assert.True(td.IsDataSubTabActive);
    }

    [Fact]
    public void TableDetailVm_ActiveSubTabBackToZero_FlipsIsDataSubTabActiveFalse()
    {
        var td = new TableDetailTabViewModel("T");
        td.ActiveSubTabIndex = TableDetailTabViewModel.DataSubTabIndex;
        td.ActiveSubTabIndex = 0;
        Assert.False(td.IsDataSubTabActive);
    }

    [Fact]
    public void MainVm_NoTab_IsDataTabActiveFalse_ShowTransactionButtonsFalse()
    {
        using var harness = new Harness();
        Assert.False(harness.Main.IsDataTabActive);
        Assert.False(harness.Main.ShowTransactionButtons);
    }

    [Fact]
    public void MainVm_QueryTabActive_ShowTransactionButtonsTrue_IsDataTabActiveFalse()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        Assert.True(harness.Main.IsQueryTabActive);
        Assert.True(harness.Main.ShowTransactionButtons);
        Assert.False(harness.Main.IsDataTabActive);
    }

    [Fact]
    public void MainVm_TableDetailTabActive_OnPolaSubTab_TransactionButtonsVisible_DataButtonsNot()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var td = new TableDetailTabViewModel("T");
        var tab = WorkspaceTabViewModel.CreateTableDetail(
            harness.Main,
            new MetadataObject("T", MetadataObjectKind.Table),
            td,
            "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectTab(tab);

        // Default sub-tab is Pola (index 0). The Pola sub-tab also shows
        // Commit/Rollback so the user can roll back / commit immediate Add /
        // Drop Field changes from the same toolbar — only the data-edit
        // buttons (add row, refresh, pagination) stay hidden.
        Assert.True(harness.Main.IsTableDetailTabActive);
        Assert.True(harness.Main.IsFieldsTabActive);
        Assert.False(harness.Main.IsDataTabActive);
        Assert.True(harness.Main.ShowTransactionButtons);
    }

    [Fact]
    public void MainVm_TableDetailTab_NonFieldsNonDataSubTab_NoTransactionButtons()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var td = new TableDetailTabViewModel("T");
        var tab = WorkspaceTabViewModel.CreateTableDetail(
            harness.Main,
            new MetadataObject("T", MetadataObjectKind.Table),
            td,
            "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectTab(tab);

        // Switch to a sub-tab that is neither Pola nor Dane (e.g. Ograniczenia = 1).
        td.ActiveSubTabIndex = 1;
        Assert.False(harness.Main.IsFieldsTabActive);
        Assert.False(harness.Main.IsDataTabActive);
        Assert.False(harness.Main.ShowTransactionButtons);
    }

    [Fact]
    public void MainVm_TableDetailTabActive_SwitchToDataSubTab_FlipsIsDataTabActiveLive()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var td = new TableDetailTabViewModel("T");
        var tab = WorkspaceTabViewModel.CreateTableDetail(
            harness.Main,
            new MetadataObject("T", MetadataObjectKind.Table),
            td,
            "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectTab(tab);

        // Inner sub-tab flip without changing the outer tab — the bridge has
        // to bubble IsDataSubTabActive → IsDataTabActive live.
        td.ActiveSubTabIndex = TableDetailTabViewModel.DataSubTabIndex;

        Assert.True(harness.Main.IsDataTabActive);
        Assert.True(harness.Main.ShowTransactionButtons);
    }

    [Fact]
    public void MainVm_SwitchAwayFromTableDetailTab_DropsIsDataTabActive()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var td = new TableDetailTabViewModel("T");
        td.ActiveSubTabIndex = TableDetailTabViewModel.DataSubTabIndex;
        var tab = WorkspaceTabViewModel.CreateTableDetail(
            harness.Main,
            new MetadataObject("T", MetadataObjectKind.Table),
            td,
            "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectTab(tab);
        Assert.True(harness.Main.IsDataTabActive);

        // Switch back to the anchored Query tab.
        harness.Main.SelectTab(harness.Main.WorkspaceTabs[0]);

        Assert.False(harness.Main.IsDataTabActive);
        Assert.True(harness.Main.IsQueryTabActive);
        // Query tab keeps ShowTransactionButtons true via the QueryTabActive path.
        Assert.True(harness.Main.ShowTransactionButtons);
    }

    [Fact]
    public void MainVm_PropertyChangedFires_ForIsDataTabActive_OnSubTabFlip()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var td = new TableDetailTabViewModel("T");
        var tab = WorkspaceTabViewModel.CreateTableDetail(
            harness.Main,
            new MetadataObject("T", MetadataObjectKind.Table),
            td,
            "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectTab(tab);

        bool fired = false;
        harness.Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsDataTabActive)) fired = true;
        };

        td.ActiveSubTabIndex = TableDetailTabViewModel.DataSubTabIndex;

        Assert.True(fired);
    }

    [Fact]
    public async Task RefreshDataPreviewCommand_NoTableDetailTabActive_IsNoOp()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        // No live reader / Firebird → simply ensure CanExecute is false on a
        // Query tab and Execute is a no-op.
        Assert.False(harness.Main.CanRefreshDataPreview);
        Assert.False(harness.Main.RefreshDataPreviewCommand.CanExecute(null));
        // Awaiting still has to complete without throwing.
        await harness.Main.RefreshDataPreviewCommand.ExecuteAsync(null);
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
