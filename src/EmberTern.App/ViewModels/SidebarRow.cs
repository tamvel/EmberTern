using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One visible row in the flattened metadata sidebar. Wraps an existing node view-model
/// (<see cref="ConnectionNodeViewModel"/> / <see cref="FolderNodeViewModel"/> /
/// <see cref="MetadataNodeViewModel"/>) — the node stays the source of truth for icons,
/// colours, labels, context menus, expansion, children and persistence. This wrapper only
/// adds what a flat list needs: depth (indentation) and a chevron-state mirror.
///
/// The flat single-VirtualizingStackPanel ListBox this feeds has a stable scroll extent and
/// correct random-access thumb scrolling, unlike the nested-VSP TreeView it replaces.
/// </summary>
public partial class SidebarRow : ObservableObject
{
    public object Node { get; }
    public int Depth { get; }
    public bool IsExpandable { get; }

    // Mirrors Node.IsExpanded so the chevron glyph updates; the controller keeps it in sync.
    [ObservableProperty]
    private bool _isExpanded;

    public SidebarRow(object node, int depth, bool isExpandable, bool isExpanded)
    {
        Node = node;
        Depth = depth;
        IsExpandable = isExpandable;
        IsExpanded = isExpanded;
    }

    // Leading spacer width for indentation (16px per level), consumed by the row template.
    public double IndentWidth => Depth * 16.0;
}
