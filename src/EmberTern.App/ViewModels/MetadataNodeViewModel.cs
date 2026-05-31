using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

public partial class MetadataNodeViewModel : ViewModelBase
{
    private readonly MetadataExplorerViewModel _owner;

    private MetadataNodeViewModel(MetadataExplorerViewModel owner)
    {
        _owner = owner;
        Children = new ObservableCollection<MetadataNodeViewModel>();
    }

    public static MetadataNodeViewModel CreateGroup(MetadataExplorerViewModel owner, MetadataObjectKind kind)
    {
        var group = new MetadataNodeViewModel(owner)
        {
            IsGroup = true,
            Kind = kind,
            GroupLabel = LabelFor(kind),
            Icon = IconFor(kind),
            IconResourceKey = ResourceKeyFor(kind),
        };
        // TreeViewItem hides the expand chevron when HasItems is false; lazy-loaded
        // groups would never be expandable. Seed a placeholder so the chevron shows;
        // LoadGroupAsync removes it before populating real children.
        group.Children.Add(CreatePlaceholder(owner));
        return group;
    }

    public static MetadataNodeViewModel CreateLeaf(MetadataExplorerViewModel owner, MetadataObject obj)
        => new(owner)
        {
            IsGroup = false,
            Kind = obj.Kind,
            Object = obj,
            GroupLabel = obj.Name,
            Icon = IconFor(obj.Kind),
            IconResourceKey = ResourceKeyFor(obj.Kind),
        };

    public static MetadataNodeViewModel CreatePlaceholder(MetadataExplorerViewModel owner)
        => new(owner)
        {
            IsPlaceholder = true,
            GroupLabel = UiStrings.MetadataLoadingPlaceholder,
            Icon = string.Empty,
            IconResourceKey = string.Empty,
        };

    public bool IsGroup { get; private init; }
    public bool IsPlaceholder { get; private init; }
    public MetadataObjectKind Kind { get; private init; }
    public MetadataObject? Object { get; private init; }
    public string Icon { get; private init; } = string.Empty;
    // Resource key (e.g. "IconColor_Table") into the theme dictionary. The XAML side
    // looks it up via IconBrushConverter, which re-evaluates on theme toggle. Keeping
    // a key (not a brush) in the VM preserves the "Core/VM hold no Avalonia types" rule.
    public string IconResourceKey { get; private init; } = string.Empty;
    public ObservableCollection<MetadataNodeViewModel> Children { get; }

    // GroupLabel is the raw label (e.g. "Tables" for groups, name for leaves).
    // DisplayLabel adds the "(n)" suffix when this is a group and a count is known.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string _groupLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private int? _count;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsLoaded { get; private set; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasCount => IsGroup && Count is not null;
    public bool IsActionable => !IsGroup && !IsPlaceholder;
    public string DisplayLabel => IsGroup && Count is { } c
        ? $"{GroupLabel} ({c})"
        : GroupLabel;

    public string ContextOpenDdlLabel => UiStrings.MetadataContextOpenDdl;
    public string ContextCopyNameLabel => UiStrings.MetadataContextCopyName;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || !IsGroup)
        {
            return;
        }

        if (!IsLoaded && !IsLoading)
        {
            _ = _owner.LoadGroupAsync(this);
            return;
        }

        // Categories already loaded eagerly on connect — but realizing 2000+ leaf
        // TreeViewItems still costs visible time. Flash the "…" indicator until the
        // UI catches up: set IsLoading=true synchronously (paints immediately), then
        // post the clear at Background priority so it runs AFTER layout/render of
        // all children completes. User sees instant feedback that something is
        // happening, indicator disappears the moment the row is actually drawn.
        if (IsLoaded && !IsLoading && Children.Count > 0)
        {
            IsLoading = true;
            Dispatcher.UIThread.Post(() => IsLoading = false, DispatcherPriority.Background);
        }
    }

    internal void MarkLoaded() => IsLoaded = true;
    internal void MarkUnloaded() => IsLoaded = false;

    [RelayCommand]
    private void OpenDdl()
    {
        if (!IsGroup && Object is { } obj)
        {
            _owner.RequestOpenDdl(obj);
        }
    }

    [RelayCommand]
    private void CopyName()
    {
        if (!IsGroup && Object is { } obj)
        {
            _owner.RequestCopyName(obj.Name);
        }
    }

    private static string LabelFor(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => UiStrings.MetadataGroupTables,
        MetadataObjectKind.View => UiStrings.MetadataGroupViews,
        MetadataObjectKind.Procedure => UiStrings.MetadataGroupProcedures,
        MetadataObjectKind.Trigger => UiStrings.MetadataGroupTriggers,
        MetadataObjectKind.Function => UiStrings.MetadataGroupFunctions,
        MetadataObjectKind.Generator => UiStrings.MetadataGroupGenerators,
        MetadataObjectKind.Domain => UiStrings.MetadataGroupDomains,
        MetadataObjectKind.Package => UiStrings.MetadataGroupPackages,
        MetadataObjectKind.Exception => UiStrings.MetadataGroupExceptions,
        MetadataObjectKind.Role => UiStrings.MetadataGroupRoles,
        MetadataObjectKind.User => UiStrings.MetadataGroupUsers,
        MetadataObjectKind.Index => UiStrings.MetadataGroupIndexes,
        MetadataObjectKind.SystemTable => UiStrings.MetadataGroupSystemTables,
        _ => kind.ToString(),
    };

    // Compact unicode glyphs — picked for visual distinctness at 12px in a 16x16 chip.
    // ▦ table grid, ◫ view (table with side bar), ƒ function, ⚙ procedure (gear = code),
    // ⚡ trigger (event), № sequence/generator, ◇ domain (typed shape), ⊞ package (boxed group),
    // ⚠ exception, 🛡 role (shield), ☻ user, ⌘ index (lookup), ⛁ system table (different stack).
    internal static string IconFor(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => "▦",
        MetadataObjectKind.View => "◫",
        MetadataObjectKind.Procedure => "⚙",
        MetadataObjectKind.Trigger => "⚡",
        MetadataObjectKind.Function => "ƒ",
        MetadataObjectKind.Generator => "№",
        MetadataObjectKind.Domain => "◇",
        MetadataObjectKind.Package => "⊞",
        MetadataObjectKind.Exception => "⚠",
        MetadataObjectKind.Role => "♜",
        MetadataObjectKind.User => "☻",
        MetadataObjectKind.Index => "⌘",
        MetadataObjectKind.SystemTable => "⛁",
        _ => "?",
    };

    // Resource key into Themes/Colors.axaml; per-theme brushes live in both Dark and
    // Light dictionaries so theme toggle re-evaluates via IconBrushConverter.
    internal static string ResourceKeyFor(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => "IconColor_Table",
        MetadataObjectKind.View => "IconColor_View",
        MetadataObjectKind.Procedure => "IconColor_Procedure",
        MetadataObjectKind.Trigger => "IconColor_Trigger",
        MetadataObjectKind.Function => "IconColor_Function",
        MetadataObjectKind.Generator => "IconColor_Generator",
        MetadataObjectKind.Domain => "IconColor_Domain",
        MetadataObjectKind.Package => "IconColor_Package",
        MetadataObjectKind.Exception => "IconColor_Exception",
        MetadataObjectKind.Role => "IconColor_Role",
        MetadataObjectKind.User => "IconColor_User",
        MetadataObjectKind.Index => "IconColor_Index",
        MetadataObjectKind.SystemTable => "IconColor_SystemTable",
        _ => string.Empty,
    };
}
