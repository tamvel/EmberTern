using System;
using System.IO;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Export-DDL button gate (CanExportDdl): enabled for a real DDL-bearing object tab,
/// disabled for non-object tabs and for in-progress New objects; and that a cancelled Save
/// picker is a clean no-op. The actual DB read + file write is DB-smoke.
/// </summary>
public class MetadataExportGatingTests
{
    [Fact]
    public void CanExportDdl_False_WhenNoTabSelected()
    {
        using var h = new Harness();
        Assert.False(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_True_ForExistingTableDetail()
    {
        using var h = new Harness();
        Select(h, WorkspaceTabViewModel.CreateTableDetail(
            h.Main, new MetadataObject("CUSTOMERS", MetadataObjectKind.Table), new TableDetailTabViewModel("CUSTOMERS"), null));
        Assert.True(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_True_ForReadOnlyDdlTab()
    {
        using var h = new Harness();
        Select(h, WorkspaceTabViewModel.CreateDdl(
            h.Main, new MetadataObject("SP_X", MetadataObjectKind.Procedure), "CREATE …", null));
        Assert.True(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_False_ForNewViewInProgress()
    {
        using var h = new Harness();
        var detail = new ViewDetailTabViewModel("New View") { IsNew = true };
        Select(h, WorkspaceTabViewModel.CreateViewDetail(
            h.Main, new MetadataObject("New View", MetadataObjectKind.View), detail, null));
        Assert.False(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_False_ForExistingViewDetail()
    {
        using var h = new Harness();
        var detail = new ViewDetailTabViewModel("V_ORDERS"); // IsNew defaults false
        Select(h, WorkspaceTabViewModel.CreateViewDetail(
            h.Main, new MetadataObject("V_ORDERS", MetadataObjectKind.View), detail, null));
        Assert.True(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_False_ForQueryTab()
    {
        using var h = new Harness();
        Select(h, WorkspaceTabViewModel.CreateQuery(h.Main));
        Assert.False(h.Main.CanExportDdl);
    }

    [Fact]
    public void CanExportDdl_False_ForNewTableTab()
    {
        using var h = new Harness();
        Select(h, WorkspaceTabViewModel.CreateNewTable(h.Main, new NewTableTabViewModel(h.Main), null));
        Assert.False(h.Main.CanExportDdl);
    }

    [Fact]
    public async Task ExportDdl_Cancelled_IsNoOp()
    {
        using var h = new Harness();
        Select(h, WorkspaceTabViewModel.CreateTableDetail(
            h.Main, new MetadataObject("CUSTOMERS", MetadataObjectKind.Table), new TableDetailTabViewModel("CUSTOMERS"), null));
        // User cancels the Save picker → null path → nothing built, nothing written, no message.
        h.Main.SaveFileRequested += _ => Task.FromResult<string?>(null);

        await h.Main.ExportDdlCommand.ExecuteAsync(null);

        Assert.Empty(h.Main.Messages);
    }

    private static void Select(Harness h, WorkspaceTabViewModel tab)
    {
        h.Main.WorkspaceTabs.Add(tab);
        h.Main.SelectedWorkspaceTab = tab;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-export-" + Guid.NewGuid().ToString("N"));
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
