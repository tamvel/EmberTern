using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class MetadataExplorerViewModelTests
{
    [Fact]
    public void ReloadConnections_PopulatesMetadataConnections()
    {
        using var harness = new Harness();
        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Store.Upsert(new ConnectionProfile { Name = "B", Host = "h", Port = 3050 });

        harness.Main.ReloadConnections();

        Assert.Equal(2, harness.Main.Metadata.Connections.Count);
        Assert.Contains(harness.Main.Metadata.Connections, c => c.Profile.Name == "A");
        Assert.Contains(harness.Main.Metadata.Connections, c => c.Profile.Name == "B");
    }

    [Fact]
    public void ReloadConnections_DoesNotAccumulateStaleNodes()
    {
        using var harness = new Harness();
        harness.Store.Upsert(new ConnectionProfile { Name = "A", Host = "h", Port = 3050 });
        harness.Main.ReloadConnections();
        harness.Main.ReloadConnections();
        harness.Main.ReloadConnections();

        // Three calls, one profile → still one node, no doubles.
        Assert.Single(harness.Main.Metadata.Connections);
    }

    [Fact]
    public void Commands_DisabledWhenNoConnectionSelected()
    {
        using var harness = new Harness();

        Assert.Null(harness.Main.Metadata.SelectedConnection);
        Assert.False(harness.Main.Metadata.EditSelectedCommand.CanExecute(null));
        Assert.False(harness.Main.Metadata.CopySelectedCommand.CanExecute(null));
        Assert.False(harness.Main.Metadata.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_EnabledWhenConnectionSelected()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        var node = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.SelectedConnection = node;

        Assert.True(harness.Main.Metadata.EditSelectedCommand.CanExecute(null));
        Assert.True(harness.Main.Metadata.CopySelectedCommand.CanExecute(null));
        Assert.True(harness.Main.Metadata.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void EditSelected_ForwardsToOwnerRequestEdit()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();
        ConnectionProfile? received = null;
        harness.Main.EditRequested += p => received = p;

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.EditSelectedCommand.Execute(null);

        Assert.Same(profile, received);
    }

    [Fact]
    public void CopySelected_ForwardsToOwnerCopy()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.CopySelectedCommand.Execute(null);

        Assert.Contains(harness.Store.LoadAll(), p => p.Name == "ERP (Copy)");
    }

    [Fact]
    public void DeleteSelected_RemovesProfileFromStore()
    {
        using var harness = new Harness();
        var profile = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        harness.Store.Upsert(profile);
        harness.Main.ReloadConnections();

        harness.Main.Metadata.SelectedConnection = new ConnectionNodeViewModel(profile, harness.Main);
        harness.Main.Metadata.DeleteSelectedCommand.Execute(null);

        Assert.DoesNotContain(harness.Store.LoadAll(), p => p.Id == profile.Id);
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
