using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class ConnectionNodeViewModelTests
{
    [Fact]
    public void DisplayName_FormatsAsNameHostPort()
    {
        var profile = new ConnectionProfile
        {
            Name = "ERP Prod",
            Host = "10.0.0.5",
            Port = 3055,
        };

        var node = new ConnectionNodeViewModel(profile);

        Assert.Equal("ERP Prod (10.0.0.5:3055)", node.DisplayName);
    }

    [Fact]
    public void StatusIndicator_IsFilledDotWhenConnected()
    {
        var node = new ConnectionNodeViewModel(new ConnectionProfile { Name = "x", Host = "h", Port = 1 })
        {
            IsConnected = true,
        };

        Assert.Equal("●", node.StatusIndicator);
    }

    [Fact]
    public void StatusIndicator_IsHollowDotWhenDisconnected()
    {
        var node = new ConnectionNodeViewModel(new ConnectionProfile { Name = "x", Host = "h", Port = 1 })
        {
            IsConnected = false,
        };

        Assert.Equal("○", node.StatusIndicator);
    }

    [Fact]
    public void IsConnected_TrueAutoExpands()
    {
        var node = new ConnectionNodeViewModel(new ConnectionProfile { Name = "x", Host = "h", Port = 1 });
        Assert.False(node.IsExpanded);

        node.IsConnected = true;

        Assert.True(node.IsExpanded);
    }

    [Fact]
    public void Copy_ProducesProfileWithCopySuffixAndNewId()
    {
        using var tempDir = new TempDir();
        var store = new ConnectionProfileStore(tempDir.Path);
        using var service = new FirebirdConnectionService();
        var main = new MainWindowViewModel(store, service);
        var original = new ConnectionProfile { Name = "ERP", Host = "h", Port = 3050 };
        store.Upsert(original);
        main.ReloadConnections();

        var copy = main.Copy(original);

        Assert.Equal("ERP (Copy)", copy.Name);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Host, copy.Host);
        Assert.Equal(original.Port, copy.Port);
        Assert.Contains(store.LoadAll(), p => p.Id == copy.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task LoadCategoriesAsync_PopulatesAllCategoryGroups()
    {
        // Build a real MainWindowViewModel against an isolated temp store — the parameterized ctor
        // constructs MetadataExplorerViewModel which is what ConnectionNodeViewModel needs as the
        // category owner. No network or DB I/O is triggered until ConnectAsync runs, which we don't.
        using var tempDir = new TempDir();
        var store = new ConnectionProfileStore(tempDir.Path);
        using var service = new FirebirdConnectionService();
        var main = new MainWindowViewModel(store, service);

        var profile = new ConnectionProfile { Name = "p", Host = "h", Port = 3050 };
        var node = new ConnectionNodeViewModel(profile, main);

        await node.LoadCategoriesAsync();

        Assert.Equal(
            new[]
            {
                Core.Metadata.MetadataObjectKind.Table,
                Core.Metadata.MetadataObjectKind.View,
                Core.Metadata.MetadataObjectKind.Procedure,
                Core.Metadata.MetadataObjectKind.Trigger,
                Core.Metadata.MetadataObjectKind.Function,
                Core.Metadata.MetadataObjectKind.Generator,
                Core.Metadata.MetadataObjectKind.Domain,
                Core.Metadata.MetadataObjectKind.Package,
                Core.Metadata.MetadataObjectKind.Exception,
                Core.Metadata.MetadataObjectKind.Role,
                Core.Metadata.MetadataObjectKind.User,
                Core.Metadata.MetadataObjectKind.Index,
                Core.Metadata.MetadataObjectKind.SystemTable,
            },
            node.Children.Select(c => c.Kind).ToArray());
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
