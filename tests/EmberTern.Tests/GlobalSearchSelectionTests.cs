using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Regression pins for the Global Search results-tree crash: selection must route through
// the VM (SelectedNode), NOT a code-behind named-control reference that could be null; and
// leaf nodes must expose IsExpanded so the shared TreeViewItem style binds cleanly (#156).
public class GlobalSearchSelectionTests
{
    private static string TempPath()
    {
        var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "et-gsearch-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(p);
        return p;
    }

    private static GlobalSearchTabViewModel MakeVm(FirebirdConnectionService service)
    {
        var main = new MainWindowViewModel(new ConnectionProfileStore(TempPath()), service);
        return new GlobalSearchTabViewModel(
            main,
            new FirebirdMetadataSearchReader(service),
            new FirebirdDdlReader(service),
            new MetadataSearchQuery("nagl"));
    }

    private static SearchResultItemViewModel Leaf(string name, string? detail = null)
        => new(MetadataObjectKind.Procedure, new SearchResultLeaf(name, detail, 4, SearchMatchLocation.Source));

    private static SearchResultGroupViewModel Group()
        => new(new SearchResultGroup(MetadataObjectKind.Procedure,
            new[] { new SearchResultLeaf("P1", null, 1, SearchMatchLocation.Name) }));

    [Fact]
    public void SelectingLeafNode_SetsSelectedItem_WithoutTouchingAnyControl()
    {
        using var service = new FirebirdConnectionService();
        var vm = MakeVm(service);

        var leaf = Leaf("P1");
        vm.SelectedNode = leaf; // what the TreeView's SelectedItem binding does

        Assert.Same(leaf, vm.SelectedItem);
        Assert.True(vm.HasSelection);
    }

    [Fact]
    public void SelectingGroupNode_DoesNotChangeSelectedItem()
    {
        using var service = new FirebirdConnectionService();
        var vm = MakeVm(service);

        vm.SelectedNode = Group();

        Assert.Null(vm.SelectedItem);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void LeafAndGroup_ExposeIsExpanded_SoSharedTreeViewItemStyleBinds()
    {
        Assert.True(Leaf("P1").IsExpanded);
        Assert.True(Group().IsExpanded);
    }
}
