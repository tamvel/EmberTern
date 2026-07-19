using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
            IconGeometryKey = GeometryKeyFor(kind),
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
            IconGeometryKey = GeometryKeyFor(obj.Kind),
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
    // private set (not init) so SetActiveState can flip a trigger/index leaf's active state in place.
    public MetadataObject? Object { get; private set; }
    public string Icon { get; private init; } = string.Empty;
    // Resource key (e.g. "IconColor_Table") into the theme dictionary. The XAML side
    // looks it up via IconBrushConverter, which re-evaluates on theme toggle. Keeping
    // a key (not a brush) in the VM preserves the "Core/VM hold no Avalonia types" rule.
    public string IconResourceKey { get; private init; } = string.Empty;
    // Geometry resource key (e.g. "Icon.Table") into Themes/IconGeometries.axaml.
    // The XAML side resolves it to a Geometry via IconGeometryConverter and renders an
    // SvgIcon; color still flows through IconResourceKey + IconBrushConverter. Holding a
    // key (not a Geometry) keeps the "VM holds no Avalonia types" rule. Empty for placeholders.
    public string IconGeometryKey { get; private init; } = string.Empty;

    // Children is the DISPLAYED collection bound to the tree (and the placeholder before
    // load). For a loaded GROUP it holds the current filter result. _allLeaves is the
    // unfiltered master list; keeping the full set here lets the filter rebuild Children
    // to matches only — so the VirtualizingStackPanel never holds hidden zero-height rows
    // (the scroll-lag root cause: an IsVisible=false leaf still occupies a VSP slot the
    // panel must realize/measure, corrupting the scroll extent on 1000+-row categories).
    public ObservableCollection<MetadataNodeViewModel> Children { get; }
    private readonly List<MetadataNodeViewModel> _allLeaves = new();

    // The full, unfiltered leaf set of a loaded group (empty for leaves/placeholders and
    // unloaded groups). Consumers that need EVERY object regardless of the active filter
    // (autocomplete, name resolution, bulk "all" operations) must read this, NOT Children.
    public IReadOnlyList<MetadataNodeViewModel> AllLeaves => _allLeaves;

    // Replace the group's full leaf set (called by LoadGroupAsync after a successful fetch).
    // Resets Children to the full set; the caller re-applies the active filter afterwards.
    internal void SetLeaves(IEnumerable<MetadataNodeViewModel> leaves)
    {
        _allLeaves.Clear();
        _allLeaves.AddRange(leaves);
        Children.Clear();
        foreach (var leaf in _allLeaves)
        {
            Children.Add(leaf);
        }
    }

    // Rebuild Children to only the leaves matching the predicate (or the full set when
    // null). Rebuilds in place so the tree's VSP only ever sees rows that are actually
    // shown — no zero-height hidden containers.
    internal void ApplyLeafFilter(Func<MetadataNodeViewModel, bool>? match)
    {
        Children.Clear();
        foreach (var leaf in _allLeaves)
        {
            if (match is null || match(leaf))
            {
                Children.Add(leaf);
            }
        }
    }

    // GroupLabel is the raw label (e.g. "Tables" for groups, name for leaves).
    // DisplayLabel adds the "(n)" suffix when this is a group and a count is known.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string _groupLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private int? _count;

    // Number of leaves matching the active filter. Non-null only while a filter is
    // applied; drives the label to show the MATCH count (e.g. "Views (1)") instead of
    // the total, so the user sees where matches are without expanding. Cleared (null)
    // when the filter is empty, restoring the total-count label.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private int? _filterMatchCount;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsLoaded { get; private set; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasCount => IsGroup && Count is not null;
    public bool IsActionable => !IsGroup && !IsPlaceholder;
    // A deactivated trigger/index leaf (IsActive == false). Drives the dimmed styling
    // + "(inactive)" label suffix. Null IsActive (every other kind) is never inactive.
    public bool IsInactive => IsActionable && Object?.IsActive == false;
    // Leaf: the object name (+ "(inactive)" when deactivated). Group: name + count —
    // the MATCH count while filtering ("Views (1)"), otherwise the total ("Views (215)").
    public string DisplayLabel => !IsGroup
        ? IsInactive ? GroupLabel + UiStrings.MetadataInactiveSuffix : GroupLabel
        : FilterMatchCount is { } fc ? $"{GroupLabel} ({fc})"
        : Count is { } c ? $"{GroupLabel} ({c})"
        : GroupLabel;

    public string ContextOpenDdlLabel => UiStrings.MetadataContextOpenDdl;
    public string ContextCopyNameLabel => UiStrings.MetadataContextCopyName;

    // Table-specific context-menu gates. The Tables CATEGORY node shows
    // "New Table"; a table LEAF shows Open / Design / Delete. Everything else
    // keeps the existing View DDL / Copy Name menu.
    public bool IsTableGroup => IsGroup && Kind == MetadataObjectKind.Table;
    public bool IsTableLeaf => IsActionable && Kind == MetadataObjectKind.Table;

    // Security context-menu gates. Users / Roles CATEGORY nodes show "Add user…" /
    // "Add role…"; a user/role LEAF shows "Open in Security Manager" (same path as
    // double-click). Delete is done inside the Security Manager (with confirmation).
    public bool IsUserGroup => IsGroup && Kind == MetadataObjectKind.User;
    public bool IsRoleGroup => IsGroup && Kind == MetadataObjectKind.Role;
    public bool IsSecurityLeaf => IsActionable && Kind is MetadataObjectKind.User or MetadataObjectKind.Role;

    // ─── Generic context-menu gates + labels (drive the shared ContextMenu) ────
    // A group whose kind supports "New X". Every kind except Index (created only inside
    // Table Detail) and SystemTable (read-only).
    public bool SupportsNew => IsGroup && Kind is not (MetadataObjectKind.Index or MetadataObjectKind.SystemTable);
    public string ContextNewLabel => string.Format(UiStrings.MetadataContextNewFormat, KindNounTitle);

    // Proc/func/trigger/package groups → "Recompile all". (Firebird has no inactive
    // state for proc/func/pkg — recompile is their bulk maintenance op, not deactivate.)
    public bool IsRecompilableGroup => IsGroup && Kind is MetadataObjectKind.Procedure
        or MetadataObjectKind.Function or MetadataObjectKind.Trigger or MetadataObjectKind.Package;
    public string ContextRecompileAllLabel => string.Format(UiStrings.MetadataContextRecompileAllFormat, KindNounTitle);

    // Trigger group → bulk activate/deactivate (visible = current filter set, or all).
    public bool IsTriggerGroup => IsGroup && Kind == MetadataObjectKind.Trigger;
    // Procedure leaf → Execute.
    public bool IsProcedureLeaf => IsActionable && Kind == MetadataObjectKind.Procedure;
    // Trigger leaf → single activate/deactivate (show only the applicable one). Both hide while a
    // multi-trigger selection is active — the leaf menu then offers the "Selected (N)" bulk ops
    // instead (see below), so the user never scrolls back to the Triggers group header for them.
    public bool IsTriggerLeaf => IsActionable && Kind == MetadataObjectKind.Trigger;
    public bool ShowActivate => IsTriggerLeaf && Object?.IsActive == false && !IsMultiTriggerSelected;
    public bool ShowDeactivate => IsTriggerLeaf && Object?.IsActive == true && !IsMultiTriggerSelected;

    // A multi-trigger selection is active AND this is one of the trigger leaves → the leaf's context
    // menu shows "Activate/Deactivate selected (N)" and hides the single-object ops (Edit/Delete/Copy
    // + single Activate/Deactivate), which are ambiguous with >1 selected. Count comes from the owner
    // (which holds the sidebar multi-selection); NotifySelectionDependentMenuItems() re-reads it just
    // before the menu opens (called from the right-click handler on the clicked node).
    private bool IsMultiTriggerSelected => IsTriggerLeaf && _owner.SelectedTriggerCount > 1;
    public bool ShowSelectedTriggerOps => IsMultiTriggerSelected;
    public string ActivateSelectedTriggersLabel =>
        string.Format(UiStrings.MetadataContextActivateSelectedFormat, _owner.SelectedTriggerCount);
    public string DeactivateSelectedTriggersLabel =>
        string.Format(UiStrings.MetadataContextDeactivateSelectedFormat, _owner.SelectedTriggerCount);

    // Deletable schema leaf (Role/User delete via Security Manager; SystemTable read-only).
    public bool CanDeleteLeaf => IsActionable && !IsMultiTriggerSelected && Kind is MetadataObjectKind.Table
        or MetadataObjectKind.View or MetadataObjectKind.Procedure or MetadataObjectKind.Trigger
        or MetadataObjectKind.Function or MetadataObjectKind.Package or MetadataObjectKind.Generator
        or MetadataObjectKind.Domain or MetadataObjectKind.Exception or MetadataObjectKind.Index;

    // Copy Name — hidden for a trigger leaf while a multi-selection is active (which object?).
    public bool ShowCopyNameLeaf => IsActionable && !IsMultiTriggerSelected;

    // Leaf "open/edit" label: Role/User → Security Manager; SystemTable → read-only Open; else Edit.
    public bool CanEditLeaf => IsActionable && !IsMultiTriggerSelected;
    public string ContextEditLabel => IsSecurityLeaf
        ? UiStrings.MetadataContextOpenSecurity
        : Kind == MetadataObjectKind.SystemTable ? UiStrings.MetadataContextOpen
        : UiStrings.MetadataContextEdit;

    // Title-cased singular noun for menu labels ("New View", "Recompile all functions").
    private string KindNounTitle => Kind switch
    {
        MetadataObjectKind.Table => "Table",
        MetadataObjectKind.View => "View",
        MetadataObjectKind.Procedure => "Procedure",
        MetadataObjectKind.Trigger => "Trigger",
        MetadataObjectKind.Function => "Function",
        MetadataObjectKind.Generator => "Generator",
        MetadataObjectKind.Domain => "Domain",
        MetadataObjectKind.Package => "Package",
        MetadataObjectKind.Exception => "Exception",
        MetadataObjectKind.Role => "Role",
        MetadataObjectKind.User => "User",
        MetadataObjectKind.Index => "Index",
        MetadataObjectKind.SystemTable => "System table",
        _ => Kind.ToString(),
    };

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

    // ─── Generic context-menu actions ─────────────────────────────────────
    // Every action dispatches to the owner (MainWindowViewModel), which REUSES the
    // existing New*/detail-editor/DROP flows — the tree is just the entry point.
    // Open/Edit routes through the existing OpenDdlCommand (no duplicate open logic).

    // "New X" on a category node — dispatched by Kind.
    [RelayCommand]
    private void New()
    {
        if (IsGroup)
        {
            _owner.RequestNewObject(Kind);
        }
    }

    // Generic leaf Delete (schema kinds) — owner confirms + DROPs.
    [RelayCommand]
    private void Delete()
    {
        if (CanDeleteLeaf && Object is { } obj)
        {
            _owner.RequestDeleteObject(obj);
        }
    }

    // Procedure leaf → Execute.
    [RelayCommand]
    private void ExecuteProcedure()
    {
        if (IsProcedureLeaf && Object is { } obj)
        {
            _owner.RequestExecuteProcedure(obj);
        }
    }

    // Procedure leaf → Debug (Stage X / D4). Opens a debugger tab for the standalone procedure.
    [RelayCommand]
    private void DebugProcedure()
    {
        if (IsProcedureLeaf && Object is { } obj)
        {
            _owner.RequestDebugProcedure(obj);
        }
    }

    // Trigger leaf → Debug (Stage X / D10). Opens a debugger tab for the relation trigger.
    [RelayCommand]
    private void DebugTrigger()
    {
        if (IsTriggerLeaf && Object is { } obj)
        {
            _owner.RequestDebugTrigger(obj);
        }
    }

    // Trigger leaf → single activate / deactivate.
    [RelayCommand]
    private void Activate()
    {
        if (IsTriggerLeaf && Object is { } obj)
        {
            _owner.RequestSetObjectActive(obj, activate: true);
        }
    }

    [RelayCommand]
    private void Deactivate()
    {
        if (IsTriggerLeaf && Object is { } obj)
        {
            _owner.RequestSetObjectActive(obj, activate: false);
        }
    }

    // Trigger group → bulk activate/deactivate over the VISIBLE (current filter) set or ALL.
    // ("Selected" scope lives on the explorer VM — it owns the sidebar multi-selection.)
    [RelayCommand] private void ActivateVisible() => RequestTriggerBulk(activate: true, scope: BatchOperationScope.Visible);
    [RelayCommand] private void DeactivateVisible() => RequestTriggerBulk(activate: false, scope: BatchOperationScope.Visible);
    [RelayCommand] private void ActivateAll() => RequestTriggerBulk(activate: true, scope: BatchOperationScope.All);
    [RelayCommand] private void DeactivateAll() => RequestTriggerBulk(activate: false, scope: BatchOperationScope.All);

    private void RequestTriggerBulk(bool activate, BatchOperationScope scope)
    {
        if (!IsTriggerGroup)
        {
            return;
        }
        // "Visible" = the current filter result = the displayed Children. "All" is
        // resolved owner-side from the reader (Children may not be loaded/expanded).
        var names = scope == BatchOperationScope.Visible
            ? Children.Where(c => c.IsActionable && c.Object is not null)
                      .Select(c => c.Object!.Name).ToList()
            : new List<string>();
        _owner.RequestBulkSetActive(new TriggerBulkRequest(Kind, activate, scope, names));
    }

    // "Selected" bulk ops act on the sidebar multi-selection, which the explorer VM owns. Expose its
    // commands here so the trigger-group ContextMenu can bind by DataContext inheritance — an
    // ElementName binding can't cross the ContextMenu's separate popup namescope (it resolves to
    // null, so the menu item silently did nothing). Same command instance → CanExecute gating works.
    public IRelayCommand ActivateSelectedTriggersCommand => _owner.ActivateSelectedTriggersCommand;
    public IRelayCommand DeactivateSelectedTriggersCommand => _owner.DeactivateSelectedTriggersCommand;

    /// <summary>Re-reads the owner's multi-selection count into the selection-dependent menu items.
    /// Called from the sidebar right-click handler on the node whose context menu is about to open, so
    /// "Activate/Deactivate selected (N)" and the single-op hiding reflect the CURRENT selection at
    /// open time (the count lives on the owner, which these plain getters read live).</summary>
    /// <summary>Flip this leaf's trigger/index active state IN PLACE — no collection change, so the
    /// sidebar row updates without a reproject (scroll + selection + expansion survive). No-op when
    /// the state already matches or there's no object. Only IsActive-derived display changes (dim +
    /// "(inactive)" suffix + the single Activate/Deactivate menu items); icon and name are unchanged.</summary>
    internal void SetActiveState(bool active)
    {
        if (Object is not { } obj || obj.IsActive == active)
        {
            return;
        }
        Object = obj with { IsActive = active };
        OnPropertyChanged(nameof(IsInactive));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(ShowActivate));
        OnPropertyChanged(nameof(ShowDeactivate));
    }

    internal void NotifySelectionDependentMenuItems()
    {
        OnPropertyChanged(nameof(ShowSelectedTriggerOps));
        OnPropertyChanged(nameof(ActivateSelectedTriggersLabel));
        OnPropertyChanged(nameof(DeactivateSelectedTriggersLabel));
        OnPropertyChanged(nameof(ShowActivate));
        OnPropertyChanged(nameof(ShowDeactivate));
        OnPropertyChanged(nameof(CanEditLeaf));
        OnPropertyChanged(nameof(CanDeleteLeaf));
        OnPropertyChanged(nameof(ShowCopyNameLeaf));
    }

    // Proc/func/trigger/package group → recompile every object of that kind.
    [RelayCommand]
    private void RecompileAll()
    {
        if (IsRecompilableGroup)
        {
            _owner.RequestRecompileGroup(Kind);
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

    // Geometry resource key for the SVG icon system (Etap 2). The keys in
    // Themes/IconGeometries.axaml are named "Icon.<KindName>" 1:1 with the enum, so the
    // mapping is just $"Icon.{kind}" — every kind in MetadataObjectKind has a matching
    // <StreamGeometry x:Key="Icon.<Kind>">. The legacy IconFor(...) glyphs are retained
    // only as a fallback / for tests; live UI renders these geometries.
    internal static string GeometryKeyFor(MetadataObjectKind kind) => $"Icon.{kind}";
}
