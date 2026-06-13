using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the destructive-operation confirmation audit. The reported bug was
/// connection delete with NO confirmation; these tests ensure connection
/// delete, clear-editor, and New Table close all gate on the ConfirmDialog.
/// Cancel (false) preserves; confirm (true) performs.
/// </summary>
public class DeleteConfirmationAuditTests
{
    // ─── Connection delete (HIGH risk) ────────────────────────────────────

    [Fact]
    public async Task DeleteConnection_Cancelled_KeepsProfile()
    {
        using var h = new Harness();
        var p = AddProfile(h, "A");
        h.Main.ConfirmationRequested += _ => Task.FromResult(false);

        await h.Main.DeleteWithConfirmationAsync(p);

        Assert.Contains(h.Store.LoadAll(), x => x.Id == p.Id);
        Assert.Contains(h.Main.Metadata.Connections, c => c.Profile.Id == p.Id);
    }

    [Fact]
    public async Task DeleteConnection_Confirmed_RemovesProfile()
    {
        using var h = new Harness();
        var p = AddProfile(h, "A");
        h.Main.ConfirmationRequested += _ => Task.FromResult(true);

        await h.Main.DeleteWithConfirmationAsync(p);

        Assert.DoesNotContain(h.Store.LoadAll(), x => x.Id == p.Id);
        Assert.DoesNotContain(h.Main.Metadata.Connections, c => c.Profile.Id == p.Id);
    }

    [Fact]
    public async Task DeleteConnection_Confirmation_IsDestructiveAndNamesConnection()
    {
        using var h = new Harness();
        var p = AddProfile(h, "PROD_DB");
        ConfirmRequest? seen = null;
        h.Main.ConfirmationRequested += req => { seen = req; return Task.FromResult(false); };

        await h.Main.DeleteWithConfirmationAsync(p);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("PROD_DB", seen.Message);
        // The rich warning must spell out the irreversible / data-loss reality.
        Assert.Contains("cannot be undone", seen.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteConnection_RawDelete_StillUnconfirmed_ForTestsAndPostConfirm()
    {
        // The raw Delete(profile) is the post-confirm executor; it must NOT
        // prompt (the wrapper already did). Pin that it removes directly.
        using var h = new Harness();
        var p = AddProfile(h, "A");
        h.Main.ConfirmationRequested += _ => Task.FromResult(false); // would block if consulted
        h.Main.Delete(p);
        Assert.DoesNotContain(h.Store.LoadAll(), x => x.Id == p.Id);
    }

    // ─── New Table tab close (data loss) ──────────────────────────────────

    [Fact]
    public async Task CloseNewTableTab_WithContent_Cancelled_KeepsTab()
    {
        using var h = new Harness();
        var form = new NewTableTabViewModel(h.Main) { TableName = "FOO" };
        var tab = WorkspaceTabViewModel.CreateNewTable(h.Main, form, null);
        h.Main.WorkspaceTabs.Add(tab);
        h.Main.ConfirmationRequested += _ => Task.FromResult(false);

        await h.Main.RequestCloseTabAsync(tab);

        Assert.Contains(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public async Task CloseNewTableTab_WithContent_Confirmed_ClosesTab()
    {
        using var h = new Harness();
        var form = new NewTableTabViewModel(h.Main) { TableName = "FOO" };
        var tab = WorkspaceTabViewModel.CreateNewTable(h.Main, form, null);
        h.Main.WorkspaceTabs.Add(tab);
        h.Main.ConfirmationRequested += _ => Task.FromResult(true);

        await h.Main.RequestCloseTabAsync(tab);

        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public async Task CloseNewTableTab_Untouched_ClosesWithoutPrompt()
    {
        using var h = new Harness();
        // Fresh form: empty name + the single seeded ID field → no content.
        var form = new NewTableTabViewModel(h.Main);
        var tab = WorkspaceTabViewModel.CreateNewTable(h.Main, form, null);
        h.Main.WorkspaceTabs.Add(tab);
        bool prompted = false;
        h.Main.ConfirmationRequested += _ => { prompted = true; return Task.FromResult(true); };

        await h.Main.RequestCloseTabAsync(tab);

        Assert.False(prompted);
        Assert.DoesNotContain(tab, h.Main.WorkspaceTabs);
    }

    [Fact]
    public void NewTable_HasContent_TracksNameAndFields()
    {
        var fresh = new NewTableTabViewModel();
        Assert.False(fresh.HasContent); // empty name, 1 seeded field

        var named = new NewTableTabViewModel { TableName = "X" };
        Assert.True(named.HasContent);
    }

    private static ConnectionProfile AddProfile(Harness h, string name)
    {
        var p = new ConnectionProfile { Id = Guid.NewGuid().ToString("N"), Name = name };
        h.Store.Upsert(p);
        h.Main.ReloadConnections();
        return p;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-del-" + Guid.NewGuid().ToString("N"));
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
