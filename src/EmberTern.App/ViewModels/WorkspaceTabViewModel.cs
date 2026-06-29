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
