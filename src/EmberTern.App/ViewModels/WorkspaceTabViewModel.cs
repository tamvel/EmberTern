using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

public enum WorkspaceTabKind
{
    Query,
    Ddl,
    TableDetail,
    NewTable,
    ViewDetail,
    ProcedureDetail,
    TriggerDetail,
    FunctionDetail,
    GeneratorDetail,
    DomainDetail,
    PackageDetail,
    ExceptionDetail,
    IndexDetail,
    SecurityManager,
    TraceMonitor,
    SessionManager,
    GlobalSearch,
    ScriptExecutor,
}

public partial class WorkspaceTabViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;

    private WorkspaceTabViewModel(MainWindowViewModel owner)
    {
        _owner = owner;
    }

    public static WorkspaceTabViewModel CreateQuery(MainWindowViewModel owner)
        => new(owner)
        {
            Kind = WorkspaceTabKind.Query,
            BaseTitle = UiStrings.WorkspaceTabUntitled,
            IsClosable = false,
            Icon = "≣",
            IconResourceKey = "IconColor_Query",
            IconGeometryKey = "Icon.Query",
        };

    public static WorkspaceTabViewModel CreateDdl(MainWindowViewModel owner, MetadataObject obj, string ddlText, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.Ddl,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = ddlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
        };

    public static WorkspaceTabViewModel CreateNewTable(MainWindowViewModel owner, NewTableTabViewModel newTable, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.NewTable,
            BaseTitle = newTable.DisplayTitle,
            IsClosable = true,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(MetadataObjectKind.Table),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.Table),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(MetadataObjectKind.Table),
            NewTable = newTable,
        };

    public static WorkspaceTabViewModel CreateTableDetail(MainWindowViewModel owner, MetadataObject obj, TableDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.TableDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            TableDetail = detail,
        };

    public static WorkspaceTabViewModel CreateViewDetail(MainWindowViewModel owner, MetadataObject obj, ViewDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.ViewDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            ViewDetail = detail,
        };

    public static WorkspaceTabViewModel CreateProcedureDetail(MainWindowViewModel owner, MetadataObject obj, ProcedureDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.ProcedureDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            ProcedureDetail = detail,
        };

    public static WorkspaceTabViewModel CreateTriggerDetail(MainWindowViewModel owner, MetadataObject obj, TriggerDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.TriggerDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            TriggerDetail = detail,
        };

    public static WorkspaceTabViewModel CreateFunctionDetail(MainWindowViewModel owner, MetadataObject obj, FunctionDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.FunctionDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            FunctionDetail = detail,
        };

    public static WorkspaceTabViewModel CreateGeneratorDetail(MainWindowViewModel owner, MetadataObject obj, GeneratorDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.GeneratorDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectKind = obj.Kind,
            ObjectName = obj.Name,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            GeneratorDetail = detail,
        };

    public static WorkspaceTabViewModel CreateDomainDetail(MainWindowViewModel owner, MetadataObject obj, DomainDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.DomainDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectName = obj.Name,
            ObjectKind = obj.Kind,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            DomainDetail = detail,
        };

    public static WorkspaceTabViewModel CreatePackageDetail(MainWindowViewModel owner, MetadataObject obj, PackageDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.PackageDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectName = obj.Name,
            ObjectKind = obj.Kind,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            PackageDetail = detail,
        };

    public static WorkspaceTabViewModel CreateExceptionDetail(MainWindowViewModel owner, MetadataObject obj, ExceptionDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.ExceptionDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectName = obj.Name,
            ObjectKind = obj.Kind,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            ExceptionDetail = detail,
        };

    public static WorkspaceTabViewModel CreateIndexDetail(MainWindowViewModel owner, MetadataObject obj, IndexDetailTabViewModel detail, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.IndexDetail,
            BaseTitle = obj.Name,
            IsClosable = true,
            ObjectName = obj.Name,
            ObjectKind = obj.Kind,
            DdlText = detail.DdlText,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(obj.Kind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(obj.Kind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(obj.Kind),
            IndexDetail = detail,
        };

    // The Security Manager tab is keyed by the context object it was opened from
    // (a user or role) — not a singleton; multiple contexts coexist. A context-less
    // tab (toolbar New User/Role) carries an empty ObjectName for dedup.
    public static WorkspaceTabViewModel CreateSecurityManager(
        MainWindowViewModel owner, SecurityManagerTabViewModel manager, MetadataObject? context, string? connectionProfileId)
    {
        var iconKind = context?.Kind == MetadataObjectKind.User
            ? MetadataObjectKind.User
            : MetadataObjectKind.Role;
        return new(owner)
        {
            Kind = WorkspaceTabKind.SecurityManager,
            BaseTitle = context is null
                ? UiStrings.SecurityManagerTabTitle
                : string.Format(CultureInfo.CurrentCulture, UiStrings.SecurityManagerTabTitleFormat, context.Name),
            IsClosable = true,
            ObjectKind = context?.Kind,
            ObjectName = context?.Name ?? string.Empty,
            ConnectionProfileId = connectionProfileId,
            Icon = MetadataNodeViewModel.IconFor(iconKind),
            IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(iconKind),
            IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(iconKind),
            SecurityManager = manager,
        };
    }

    // The Activity Monitor is a live, near-singleton-per-connection diagnostic tab (like the
    // Security Manager). Opened from the Monitoring toolbar button; not persisted.
    public static WorkspaceTabViewModel CreateTraceMonitor(
        MainWindowViewModel owner, TraceMonitorTabViewModel monitor, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.TraceMonitor,
            BaseTitle = UiStrings.TraceMonitorTabTitle,
            IsClosable = true,
            ObjectName = string.Empty,
            ConnectionProfileId = connectionProfileId,
            Icon = string.Empty,
            IconResourceKey = "AccentBrush",
            IconGeometryKey = "Icon.Activity",
            TraceMonitor = monitor,
        };

    // The Session Manager is a live, near-singleton-per-connection diagnostic tab (like the
    // Activity Monitor). Opened from the Monitoring toolbar; not persisted.
    public static WorkspaceTabViewModel CreateSessionManager(
        MainWindowViewModel owner, SessionManagerTabViewModel manager, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.SessionManager,
            BaseTitle = UiStrings.SessionManagerTabTitle,
            IsClosable = true,
            ObjectName = string.Empty,
            ConnectionProfileId = connectionProfileId,
            Icon = string.Empty,
            IconResourceKey = "AccentBrush",
            IconGeometryKey = "Icon.Connection",
            SessionManager = manager,
        };

    // A Global Search results tab — one per phrase (NOT a singleton, no overwrite);
    // opened from the toolbar / Ctrl+Shift+F. Not persisted.
    public static WorkspaceTabViewModel CreateGlobalSearch(
        MainWindowViewModel owner, GlobalSearchTabViewModel search, string phrase, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.GlobalSearch,
            BaseTitle = string.Format(CultureInfo.CurrentCulture, UiStrings.GlobalSearchTabTitleFormat, phrase),
            IsClosable = true,
            ObjectName = phrase,
            ConnectionProfileId = connectionProfileId,
            Icon = string.Empty,
            IconResourceKey = "AccentBrush",
            IconGeometryKey = "Icon.Search",
            GlobalSearch = search,
        };

    // The Script Executor is a tools tab (like the monitors): opened from the toolbar,
    // near-singleton per connection, not persisted.
    public static WorkspaceTabViewModel CreateScriptExecutor(
        MainWindowViewModel owner, ScriptExecutorTabViewModel script, string? connectionProfileId)
        => new(owner)
        {
            Kind = WorkspaceTabKind.ScriptExecutor,
            BaseTitle = UiStrings.ScriptExecutorTabTitle,
            IsClosable = true,
            ObjectName = string.Empty,
            ConnectionProfileId = connectionProfileId,
            Icon = string.Empty,
            IconResourceKey = "AccentBrush",
            IconGeometryKey = "Icon.FileText",
            ScriptExecutor = script,
        };

    public WorkspaceTabKind Kind { get; private init; }
    public bool IsClosable { get; private init; }
    public MetadataObjectKind? ObjectKind { get; private init; }
    public string? ObjectName { get; private init; }
    public string DdlText { get; private init; } = string.Empty;
    public string? ConnectionProfileId { get; private init; }
    public string Icon { get; private init; } = string.Empty;
    public string IconResourceKey { get; private init; } = string.Empty;
    // Geometry key into IconGeometries.axaml (e.g. "Icon.Query"); resolved by
    // IconGeometryConverter to render the tab's SvgIcon. See MetadataNodeViewModel.
    public string IconGeometryKey { get; private init; } = string.Empty;
    public TableDetailTabViewModel? TableDetail { get; private init; }
    public NewTableTabViewModel? NewTable { get; private init; }
    public ViewDetailTabViewModel? ViewDetail { get; private init; }
    public ProcedureDetailTabViewModel? ProcedureDetail { get; private init; }
    public TriggerDetailTabViewModel? TriggerDetail { get; private init; }
    public FunctionDetailTabViewModel? FunctionDetail { get; private init; }
    public GeneratorDetailTabViewModel? GeneratorDetail { get; private init; }
    public DomainDetailTabViewModel? DomainDetail { get; private init; }
    public PackageDetailTabViewModel? PackageDetail { get; private init; }
    public ExceptionDetailTabViewModel? ExceptionDetail { get; private init; }
    public IndexDetailTabViewModel? IndexDetail { get; private init; }
    public SecurityManagerTabViewModel? SecurityManager { get; private init; }
    public TraceMonitorTabViewModel? TraceMonitor { get; private init; }
    public SessionManagerTabViewModel? SessionManager { get; private init; }
    public GlobalSearchTabViewModel? GlobalSearch { get; private init; }
    public ScriptExecutorTabViewModel? ScriptExecutor { get; private init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _baseTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _showActiveTransactionMarker;

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayTitle => ShowActiveTransactionMarker ? BaseTitle + " ●" : BaseTitle;

    // Unsaved work this tab holds (uncompiled new object / modified source /
    // queued structural changes), or null when clean. Delegates to whichever
    // child detail VM backs the tab. The WorkGuard on MainWindowViewModel
    // aggregates this across tabs for tab-close / disconnect / exit decisions.
    public UnsavedWorkItem? UnsavedWork => Kind switch
    {
        WorkspaceTabKind.NewTable => NewTable?.GetUnsavedWork(),
        WorkspaceTabKind.TableDetail => TableDetail?.GetUnsavedWork(),
        WorkspaceTabKind.ViewDetail => ViewDetail?.GetUnsavedWork(),
        WorkspaceTabKind.ProcedureDetail => ProcedureDetail?.GetUnsavedWork(),
        WorkspaceTabKind.TriggerDetail => TriggerDetail?.GetUnsavedWork(),
        WorkspaceTabKind.FunctionDetail => FunctionDetail?.GetUnsavedWork(),
        WorkspaceTabKind.GeneratorDetail => GeneratorDetail?.GetUnsavedWork(),
        WorkspaceTabKind.DomainDetail => DomainDetail?.GetUnsavedWork(),
        WorkspaceTabKind.PackageDetail => PackageDetail?.GetUnsavedWork(),
        WorkspaceTabKind.ExceptionDetail => ExceptionDetail?.GetUnsavedWork(),
        WorkspaceTabKind.IndexDetail => IndexDetail?.GetUnsavedWork(),
        _ => null,
    };

    [RelayCommand]
    private void Activate() => _owner.SelectTab(this);

    // Routes through the confirming wrapper so a New Table tab with unsaved
    // form content prompts before discard. DDL / TableDetail tabs close
    // silently (reopenable from the tree).
    [RelayCommand]
    private async Task CloseAsync() => await _owner.RequestCloseTabAsync(this).ConfigureAwait(true);
}
