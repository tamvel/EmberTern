using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird VIEW. Six tabs — SQL (editable source +
/// Compile), Fields (read-only), Dependencies, Data (read-only preview),
/// Description (read-only), DDL (read-only). Deliberately NOT a subclass of
/// (or shared base with) <see cref="TableDetailTabViewModel"/>: a view has no
/// constraints / indexes / inline data editing, and its primary artifact is an
/// editable SELECT source. Reuse happens at the reader / static-helper level
/// (same <see cref="FirebirdTableDetailReader"/> read methods, same
/// <see cref="TableDetailTabViewModel.BuildDependencyTree"/>), not via inheritance.
/// </summary>
public partial class ViewDetailTabViewModel : ViewModelBase, IUnsavedWorkSource
{
    // Mirrors TableDetail's data-preview knobs — a view's Data tab uses the
    // exact same paged SELECT * infrastructure.
    public const int DataPreviewRowLimit = 200;
    public const int MaxPageSize = 1000;
    public const int RowCountCap = 50000;

    // Sub-tab indices — must match the TabItem order in ViewDetailTabView.axaml
    // (SQL, Fields, Dependencies, Data, Description, DDL).
    public const int SqlSubTabIndex = 0;
    public const int DataSubTabIndex = 3;

    // Default CREATE VIEW template for the New View flow. The user edits the SQL
    // directly — no visual designer (per milestone scope).
    public const string NewViewTemplate =
        "CREATE VIEW NEW_VIEW (\n    COLUMN_NAME\n)\nAS\nSELECT\n    /* column */\nFROM\n    /* table */;";

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    public ViewDetailTabViewModel(string viewName)
        : this(viewName, null, null, null)
    {
    }

    public ViewDetailTabViewModel(
        string viewName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        ViewName = viewName;
        EditableViewName = viewName;
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        Fields = new ObservableCollection<FieldInfo>();
        Columns = new ObservableCollection<ViewColumnRowViewModel>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        Columns.CollectionChanged += OnColumnsChanged;

        // Shared filter panel + aggregation bar for the Data grid. Server-paged →
        // filter + aggregates pushed to SQL over the FULL set (never a single page).
        DataFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyDataFilterAsync };
        DataAggregationBar = new AggregationBarViewModel(ComputeDataAggregateAsync);

        // Ctor assignments (EditableViewName) must not flip dirty — release the
        // suppression only once the object is fully constructed.
        _suppressDirty = false;
    }

    public string ViewName { get; }

    // ─── Dirty tracking (drives IUnsavedWorkSource + future auto-draft) ─────
    //
    // Explicit flag rather than a baseline string-compare: the editable source has
    // two representations (Source text vs. the Easy structured model) and a pure
    // toggle between them must NOT read as an edit. Programmatic writes (load,
    // refresh, mode toggle) are wrapped in _suppressDirty; genuine user edits flip
    // it via MarkDirty. Cleared on load / after a successful compile.
    private bool _isDirty;
    // Starts suppressed so ctor field assignments don't mark dirty; reset at ctor end.
    private bool _suppressDirty = true;

    public bool IsDirty => _isDirty;
    internal void ClearDirty() => SetDirty(false);
    private void MarkDirty() { if (!_suppressDirty) SetDirty(true); }

    // Centralized so a dirty transition keeps the Revert button's enabled state (and any
    // IsDirty binding) in sync — Revert is only available when there are edits to undo.
    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        OnPropertyChanged(nameof(IsDirty));
        RevertChangesCommand.NotifyCanExecuteChanged();
    }

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();
        if (e.NewItems is not null)
        {
            foreach (ViewColumnRowViewModel c in e.NewItems) c.PropertyChanged += OnColumnPropertyChanged;
        }
    }

    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => MarkDirty();

    private bool _settingNameUpper;
    partial void OnEditableViewNameChanged(string value)
    {
        // UPPERCASE user-entered names consistently (gotcha #141). Programmatic sets
        // (ctor / load / parse, under _suppressDirty) don't need coercing.
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, System.StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableViewName = upper; } finally { _settingNameUpper = false; }
                return;
            }
        }
        MarkDirty();
    }

    partial void OnSourceTextChanged(string value) => MarkDirty();
    partial void OnEditableBodyChanged(string value) => MarkDirty();

    // Unsaved-work for the WorkGuard. An untouched tab (just opened, or a fresh New
    // View before any edit) is clean → null. The New View flow clears dirty after
    // seeding the template so an untouched new tab doesn't prompt.
    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        var name = string.IsNullOrWhiteSpace(EditableViewName) ? ViewName : EditableViewName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedNewViewFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedViewFormat, name));
    }

    /// <summary>
    /// True for a not-yet-created view (the New View flow). The non-SQL tabs
    /// stay empty until the first successful Compile, after which the owner
    /// reopens the real view. Compile in this mode raises <see cref="ViewCreated"/>.
    /// </summary>
    public bool IsNew { get; init; }

    // ─── Source ⇄ Easy mode (mirrors ProcedureDetailTabViewModel) ─────────
    //
    // Easy mode hides the CREATE VIEW header and presents the view name + column list
    // as editable fields above a body editor that holds just the AS SELECT … part.
    // Source mode is the full editable statement. A brand-new view can also be authored
    // in Easy (started there by the New View flow): the editable name field supplies
    // the object name (Source mode keeps it in the CREATE VIEW header text).

    /// <summary>Easy mode is available for every view, including a not-yet-created one
    /// (New View starts in Easy with an editable name field).</summary>
    public bool CanUseEasyMode => true;

    /// <summary>Object name used by Easy mode (the CREATE VIEW header isn't shown there,
    /// so the name lives here). Seeded from <see cref="ViewName"/> / the parsed source;
    /// editable in the New View flow, read-only display for an existing view.</summary>
    [ObservableProperty]
    private string _editableViewName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceMode))]
    private bool _easyMode;

    public bool IsSourceMode => !EasyMode;

    // Whether the source used CREATE OR ALTER VIEW (vs a plain CREATE VIEW); preserved
    // across the Easy round-trip so the verb isn't silently rewritten. Existing views
    // are fetched as CREATE OR ALTER VIEW, so the default is true.
    private bool _sourceOrAlter = true;

    partial void OnEasyModeChanged(bool value)
    {
        // A pure Source⇄Easy toggle is not an edit — suppress the dirty flips its
        // re-population of SourceText / the structured model would otherwise cause.
        var prev = _suppressDirty;
        _suppressDirty = true;
        try
        {
            if (value)
            {
                // Nothing loaded yet (mode preference applied before lazy load) — don't
                // parse an empty source or show a spurious notice; LoadAsync re-syncs.
                if (string.IsNullOrWhiteSpace(SourceText)) { ErrorMessage = null; return; }
                // Source → Easy: parse the current source into the structured model so
                // source edits carry over. On failure keep the last-good model + note it.
                ErrorMessage = SyncEasyModelFromSource(SourceText) ? null : UiStrings.ViewParseFailedNotice;
            }
            else
            {
                // Easy → Source: regenerate the full statement from the structured model.
                SourceText = BuildFullSource();
            }
        }
        finally
        {
            _suppressDirty = prev;
        }
    }

    /// <summary>Editable AS-SELECT body — the Easy-mode body editor's content. The
    /// CREATE VIEW header + column list come from the structured model, never typed
    /// into this editor.</summary>
    [ObservableProperty]
    private string _editableBody = string.Empty;

    /// <summary>Name-only column rows for the Easy-mode column section (order = the
    /// CREATE VIEW (...) order).</summary>
    public ObservableCollection<ViewColumnRowViewModel> Columns { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteColumnCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveColumnUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveColumnDownCommand))]
    private ViewColumnRowViewModel? _selectedColumn;

    [RelayCommand]
    private void AddColumn()
    {
        var col = new ViewColumnRowViewModel(
            string.Format(CultureInfo.InvariantCulture, "COLUMN_{0}", Columns.Count + 1));
        Columns.Add(col);
        SelectedColumn = col;
    }

    public bool CanDeleteColumn => SelectedColumn is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteColumn))]
    private void DeleteColumn()
    {
        if (SelectedColumn is null) return;
        var idx = Columns.IndexOf(SelectedColumn);
        Columns.Remove(SelectedColumn);
        SelectedColumn = Columns.Count == 0 ? null : Columns[Math.Min(idx, Columns.Count - 1)];
    }

    public bool CanMoveColumnUp => SelectedColumn is not null && Columns.IndexOf(SelectedColumn) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveColumnUp))]
    private void MoveColumnUp()
    {
        if (SelectedColumn is null) return;
        var idx = Columns.IndexOf(SelectedColumn);
        if (idx <= 0) return;
        MoveColumn(idx, idx - 1);
    }

    public bool CanMoveColumnDown => SelectedColumn is not null
        && Columns.IndexOf(SelectedColumn) >= 0
        && Columns.IndexOf(SelectedColumn) < Columns.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveColumnDown))]
    private void MoveColumnDown()
    {
        if (SelectedColumn is null) return;
        var idx = Columns.IndexOf(SelectedColumn);
        if (idx < 0 || idx >= Columns.Count - 1) return;
        MoveColumn(idx, idx + 1);
    }

    // RemoveAt + Insert, NOT ObservableCollection.Move: Avalonia 12's DataGrid does
    // not reliably repaint a NotifyCollectionChangedAction.Move (same gotcha the New
    // Table field grid hit), so a Move left the grid showing the old order even though
    // the collection — and therefore the generated DDL — had already reordered.
    private void MoveColumn(int from, int to)
    {
        var item = Columns[from];
        Columns.RemoveAt(from);
        Columns.Insert(to, item);
        SelectedColumn = item; // keep selection on the moved row (DataGrid clears it on RemoveAt)
        RefreshColumnMoveState();
    }

    // The selected object doesn't change on a Move (only its index), so the
    // index-dependent Move gates must be re-evaluated explicitly.
    private void RefreshColumnMoveState()
    {
        MoveColumnUpCommand.NotifyCanExecuteChanged();
        MoveColumnDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Parses <paramref name="source"/> into the Easy-mode model (columns +
    /// AS body + verb). Returns false when the text doesn't match a CREATE [OR ALTER]
    /// VIEW shape — the caller keeps the last-good model and may surface a notice.</summary>
    internal bool SyncEasyModelFromSource(string? source)
    {
        var sig = ViewSignatureParser.Parse(source);
        if (!sig.Success) return false;
        _sourceOrAlter = sig.OrAlter;
        if (!string.IsNullOrWhiteSpace(sig.Name)) EditableViewName = sig.Name!;
        Columns.Clear();
        foreach (var c in sig.Columns) Columns.Add(new ViewColumnRowViewModel(c));
        EditableBody = sig.Body;
        SelectedColumn = Columns.Count > 0 ? Columns[0] : null;
        return true;
    }

    /// <summary>Reassembles the full CREATE [OR ALTER] VIEW from the Easy-mode model.
    /// An empty column list ⇒ no <c>(...)</c> clause (lossless for header-less views).</summary>
    internal string BuildFullSource()
    {
        var cols = new List<string>();
        foreach (var c in Columns)
            if (!string.IsNullOrWhiteSpace(c.Name)) cols.Add(c.Name.Trim());
        var name = string.IsNullOrWhiteSpace(EditableViewName) ? ViewName : EditableViewName.Trim();
        return DdlGenerator.BuildCreateOrAlterView(name, cols, EditableBody, _sourceOrAlter);
    }

    /// <summary>The SQL to compile: Easy mode reassembles from the structured model;
    /// Source mode uses the raw editor text. Internal so tests can assert it.</summary>
    internal string BuildCompileSql() => EasyMode ? BuildFullSource() : SourceText;

    /// <summary>Editable CREATE OR ALTER VIEW source — the SQL tab's content.</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>Read-only reconstructed DDL (CREATE VIEW …) — the DDL tab.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    public ObservableCollection<FieldInfo> Fields { get; }
    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionEmpty))]
    private bool _descriptionLoaded;

    public bool HasDescription => DescriptionLoaded && !string.IsNullOrEmpty(Description);
    public bool ShowDescriptionEmpty => DescriptionLoaded && string.IsNullOrEmpty(Description);

    // ─── Editable description (COMMENT ON VIEW) ───────────────────────────
    //
    // Same workflow as the table description: the user edits EditableDescription,
    // Save/Clear emit COMMENT ON VIEW … in the working (metadata) transaction —
    // Rollback undoes, Commit persists. Gated on CanEditDescription (a DDL
    // executor is wired).

    /// <summary>User-editable copy of the view description. Mirrors
    /// <see cref="Description"/> on load/refresh; the user edits it
    /// independently until Save.</summary>
    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value)
    {
        EditableDescription = value ?? string.Empty;
    }

    public bool CanEditDescription => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task SaveDescription() => SaveDescriptionCoreAsync();

    [RelayCommand(CanExecute = nameof(CanEditDescription))]
    private Task ClearDescription()
    {
        EditableDescription = string.Empty;
        return SaveDescriptionCoreAsync();
    }

    private async Task SaveDescriptionCoreAsync()
    {
        if (_ddlExecutor is null) return;
        ErrorMessage = null;
        var comment = string.IsNullOrWhiteSpace(EditableDescription) ? null : EditableDescription;
        var sql = DdlGenerator.BuildCommentView(ViewName, comment);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDescriptionSaveFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDescriptionSaveFailedFormat, ex.Message);
            return;
        }
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── SQL formatting (reuses the shared SqlFormatter) ──────────────────
    //
    // Same experience as the SQL Editor: format the editor selection when there
    // is one, otherwise the whole source. The view installs the two callbacks
    // (selection provider + replace) so formatting matches the editor's
    // selection state; with no callbacks (tests) it formats SourceText wholesale.

    /// <summary>Set by the view — returns the SQL editor's current selection, or
    /// null/empty when nothing is selected.</summary>
    public Func<string?>? SelectedTextProvider { get; set; }

    /// <summary>Set by the view — replaces the editor selection with the given
    /// text (re-selecting it) or overwrites the whole document when there's no
    /// selection.</summary>
    public Action<string>? ReplaceSelectedOrAllText { get; set; }

    // In Easy mode the formatted/active text is the AS-SELECT body editor; in Source
    // mode it's the full-statement editor. The view's selection/replace callbacks
    // target the matching editor, so Format acts on whichever editor is active.
    private string ActiveEditorText
    {
        get => EasyMode ? EditableBody : SourceText;
        set { if (EasyMode) EditableBody = value; else SourceText = value; }
    }

    [RelayCommand]
    private void FormatSql()
    {
        var selected = SelectedTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : ActiveEditorText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source);
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace)
        {
            replace(formatted);
        }
        else if (!hasSelection)
        {
            // No view callback (tests / headless) — overwrite the active text.
            ActiveEditorText = formatted;
        }
    }

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ─── Data preview (read-only, paged) ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataResult))]
    [NotifyPropertyChangedFor(nameof(ShowDataError))]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private QueryResult? _dataResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataResult))]
    [NotifyPropertyChangedFor(nameof(ShowDataError))]
    private string _dataError = string.Empty;

    // Bump on each fetch so the view rebuilds the (imperative) grid columns.
    [ObservableProperty]
    private string _dataResultVersionTag = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private string? _sortColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    private bool _sortDescending;

    public bool HasDataResult => DataResult is { HasResultSet: true } && string.IsNullOrEmpty(DataError);
    public bool ShowDataError => !string.IsNullOrEmpty(DataError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataPreviewHint))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyCanExecuteChangedFor(nameof(GoToFirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToPreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToNextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToLastPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    private int _pageSize = DataPreviewRowLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyCanExecuteChangedFor(nameof(GoToNextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToLastPageCommand))]
    private int? _lastKnownRowCount;

    public bool HasPreviousPage => CurrentPage > 1;

    public bool HasNextPage
    {
        get
        {
            if (LastKnownRowCount is { } known) return CurrentPage * PageSize < known;
            return DataResult is { HasResultSet: true } r && r.Rows.Count >= PageSize;
        }
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value < 1) PageSize = 1;
        else if (value > MaxPageSize) PageSize = MaxPageSize;
    }

    partial void OnDataResultChanged(QueryResult? value)
    {
        SyncDataFilterColumns(value);
        _selectedDataRowInPage = -1;
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(DataRecordInfo));
        GoToNextPageCommand.NotifyCanExecuteChanged();
    }

    // ── Data grid: shared filter panel + aggregation bar (SQL push-down) ──────
    public FilterPanelViewModel DataFilterPanel { get; }
    public AggregationBarViewModel DataAggregationBar { get; }
    private GridFilter _dataFilter = GridFilter.Empty;
    private FirebirdGridSqlBuilder.GridSqlFilter? _dataSqlFilter;
    private int _selectedDataRowInPage = -1;

    // IBExpert-style "Record N of M" for the server-paged Data grid. M = the bounded
    // COUNT probe (LastKnownRowCount, refreshed on load / sort / filter). A "+" suffix
    // marks a count that hit RowCountCap.
    public string DataRecordInfo
    {
        get
        {
            if (DataResult is not { HasResultSet: true }) return string.Empty;
            if (LastKnownRowCount is not { } total) return string.Empty;
            string totalText = total >= RowCountCap
                ? total.ToString(CultureInfo.CurrentCulture) + "+"
                : total.ToString(CultureInfo.CurrentCulture);
            if (_selectedDataRowInPage >= 0)
            {
                int global = (CurrentPage - 1) * PageSize + _selectedDataRowInPage + 1;
                return string.Format(CultureInfo.CurrentCulture, UiStrings.RecordPositionFormat, global, totalText);
            }
            return string.Format(CultureInfo.CurrentCulture, UiStrings.RecordCountFormat, totalText);
        }
    }

    public void SetDataSelectedRow(int indexInPage)
    {
        if (_selectedDataRowInPage == indexInPage) return;
        _selectedDataRowInPage = indexInPage;
        OnPropertyChanged(nameof(DataRecordInfo));
    }

    // Re-point the panels only when the column STRUCTURE changes (a same-column
    // filter reload keeps the just-applied conditions — SetColumns clears them).
    private void SyncDataFilterColumns(QueryResult? value)
    {
        var newNames = value is { HasResultSet: true }
            ? value.Columns.Select(c => c.Name).ToList()
            : new List<string>();
        var curNames = DataFilterPanel.Columns.Select(c => c.Name).ToList();
        if (newNames.SequenceEqual(curNames, StringComparer.Ordinal)) return;
        var cols = GridColumnRef.From(value is { HasResultSet: true } ? value.Columns : null);
        DataFilterPanel.SetColumns(cols);
        DataAggregationBar.SetColumns(cols);
        _dataFilter = GridFilter.Empty;
        _dataSqlFilter = null;
    }

    private FirebirdGridSqlBuilder.GridSqlFilter? BuildDataSqlFilter(GridFilter filter)
    {
        if (filter.IsEmpty) return null;
        var cols = DataFilterPanel.Columns.Select(c => new QueryColumn(c.Name, c.ClrType)).ToList();
        return FirebirdGridSqlBuilder.BuildWhere(filter, cols);
    }

    private async Task ApplyDataFilterAsync(GridFilter filter)
    {
        _dataFilter = filter;
        _dataSqlFilter = BuildDataSqlFilter(filter);
        LastKnownRowCount = null;
        CurrentPage = 1;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
        await RefreshDataRowCountAsync().ConfigureAwait(true);
        await DataAggregationBar.RecomputeAllAsync().ConfigureAwait(true);
    }

    private Task<object?> ComputeDataAggregateAsync(GridColumnRef col, GridAggregate agg)
        => _reader is null
            ? Task.FromResult<object?>(null)
            : _reader.GetAggregateAsync(ViewName, col.Name, agg, _dataSqlFilter);

    private async Task RefreshDataRowCountAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return;
        try
        {
            LastKnownRowCount = await _reader.GetRowCountAsync(ViewName, RowCountCap, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            OnPropertyChanged(nameof(DataRecordInfo));
        }
        catch (MetadataReadException) { /* keep the prior count */ }
    }

    public string DataPreviewHint
    {
        get
        {
            var count = DataResult is { HasResultSet: true } r ? r.Rows.Count : 0;
            var baseHint = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.TableDetailDataPagedHintFormat,
                CurrentPage,
                count);
            if (!string.IsNullOrEmpty(SortColumn))
            {
                var arrow = SortDescending ? "↓" : "↑";
                baseHint += string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.TableDetailDataPreviewSortedByFormat,
                    SortColumn,
                    arrow);
            }
            return baseHint;
        }
    }

    // ─── Dependency tree open (reuses TableDetail's routing) ───────────────

    /// <summary>Raised on a dependency-leaf double-click; the owner reuses its
    /// OnOpenDdlRequested path to open a TableDetail / ViewDetail / DDL tab.</summary>
    public event Action<MetadataObject>? OpenObjectRequested;

    public void RequestOpen(DependencyLeafNode leaf)
    {
        if (leaf is null) return;
        var dep = leaf.Dependency;
        if (dep is null || string.IsNullOrEmpty(dep.ObjectName)) return;
        var kind = TableDetailTabViewModel.MapObjectTypeToKind(dep.ObjectType);
        if (kind is null) return;
        OpenObjectRequested?.Invoke(new MetadataObject(dep.ObjectName, kind.Value));
    }

    // ─── Compile / Save ───────────────────────────────────────────────────

    /// <summary>Raised after a successful Compile in <see cref="IsNew"/> mode.
    /// Argument is the view name parsed from the compiled SQL (or null when it
    /// couldn't be parsed). The owner refreshes the metadata tree, closes the
    /// New View tab, and reopens the real view when a name is known.</summary>
    public event Action<string?>? ViewCreated;

    public bool CanCompile => _ddlExecutor is not null;

    /// <summary>
    /// Executes the SQL-tab source (CREATE OR ALTER VIEW for an existing view,
    /// CREATE VIEW for a new one) in the user's working transaction — Rollback
    /// undoes, Commit persists, consistent with all other DDL in the app. On
    /// success an existing view fully refreshes itself (#2); a new view raises
    /// <see cref="ViewCreated"/> for the owner to reopen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        if (_ddlExecutor is null) return;
        // Easy mode reassembles the statement from the structured model; Source mode
        // compiles the raw editor text.
        var sql = BuildCompileSql();
        if (string.IsNullOrWhiteSpace(sql)) return;

        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ViewCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ViewCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            ViewCreated?.Invoke(TryParseViewName(sql));
            return;
        }

        // Existing view: fully refresh itself (#2) — source, fields, dependencies,
        // data preview, DDL, description all re-read from the live catalog.
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Best-effort extraction of the view name from a CREATE [OR ALTER] VIEW
    /// statement so the New View flow can reopen the freshly-created object.
    /// Returns the upper-cased unquoted name, the verbatim quoted name, or null
    /// when the shape doesn't match (the owner then just refreshes the tree).
    /// Pure + internal so it's unit-testable without a database.
    /// </summary>
    internal static string? TryParseViewName(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        var tokens = sql!.Split(new[] { ' ', '\t', '\r', '\n', '(' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        if (i >= tokens.Length || !tokens[i].Equals("CREATE", StringComparison.OrdinalIgnoreCase)) return null;
        i++;
        if (i < tokens.Length && tokens[i].Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            if (i < tokens.Length && tokens[i].Equals("ALTER", StringComparison.OrdinalIgnoreCase)) i++;
            else return null;
        }
        if (i >= tokens.Length || !tokens[i].Equals("VIEW", StringComparison.OrdinalIgnoreCase)) return null;
        i++;
        if (i >= tokens.Length) return null;

        var name = tokens[i];
        if (name.StartsWith('"'))
        {
            var end = name.IndexOf('"', 1);
            return end > 0 ? name.Substring(1, end - 1) : null;
        }
        // Unquoted identifier — strip a trailing '(' shouldn't happen (we split on it),
        // but guard against a stray comma/semicolon.
        name = name.TrimEnd(',', ';');
        return name.Length == 0 ? null : name.ToUpperInvariant();
    }

    // ─── Revert (discard uncompiled edits, reload from DB) ────────────────
    //
    // The source-editor analog of the Table designer's "discard pending changes":
    // reload the view from the database, throwing away uncompiled edits. Confirms
    // first (the edits can't be recovered) so an accidental click never loses work.
    // Existing views only — a not-yet-created view has no DB state to revert to, so
    // the button is disabled in the New View flow (use Close to abandon it).

    /// <summary>Confirmation gate for the destructive Revert — the owner wires this to
    /// the shared ConfirmDialog. With no handler (tests) it proceeds (Task.FromResult(true)).</summary>
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var name = string.IsNullOrWhiteSpace(EditableViewName) ? ViewName : EditableViewName.Trim();
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.RevertChangesConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.RevertChangesConfirmFormat, name),
            ConfirmLabel = UiStrings.RevertChangesConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── Load / refresh ───────────────────────────────────────────────────

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        => _loadTask ??= LoadAsync(cancellationToken);

    /// <summary>Discards the cached load task and re-runs the full load. Used by
    /// <see cref="ExecuteCompileAsync"/> so an ALTERed view repaints from the
    /// live catalog.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _loadTask = null;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // New view: nothing to load until it's compiled. The SQL tab holds the
        // template; other tabs stay empty.
        if (IsNew) return;
        if (_reader is null || _ddlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        DataError = string.Empty;
        // Programmatic population — not user edits. Cleared to a clean state in finally.
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                SourceText = await _ddlReader.FetchViewSourceAsync(
                    new MetadataObject(ViewName, MetadataObjectKind.View), cancellationToken).ConfigureAwait(true);
                // If the user is in Easy mode (e.g. after a Compile→Refresh), re-derive
                // the structured model from the freshly loaded source.
                if (EasyMode) SyncEasyModelFromSource(SourceText);
            });

            await SafeLoadAsync(async () =>
            {
                var fields = await _reader.GetFieldsAsync(ViewName, cancellationToken).ConfigureAwait(true);
                Fields.Clear();
                foreach (var f in fields) Fields.Add(f);
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetDependenciesAsync(ViewName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
            });

            await SafeLoadAsync(async () =>
            {
                // DDL tab == Export: render the complete portable script (structure +
                // COMMENT ON) via the same MetadataExportService the Export button uses.
                // The editable Source (Editor tab) is untouched.
                DdlText = await new MetadataExportService(_ddlReader, _reader).BuildObjectScriptAsync(
                    new MetadataObject(ViewName, MetadataObjectKind.View), cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                Description = await _reader.GetDescriptionAsync(ViewName, cancellationToken).ConfigureAwait(true);
                DescriptionLoaded = true;
            });

            await LoadDataPreviewCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
            _suppressDirty = false;
            ClearDirty();
        }
    }

    private async Task LoadDataPreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) return;
        try
        {
            var preview = await _reader.GetDataPreviewAsync(ViewName, CurrentPage, PageSize, null, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
            // Probe the (bounded) row count so Record N of M has an M on first load.
            await RefreshDataRowCountAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            DataResult = null;
            DataError = ex.Message;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
        }
    }

    public async Task ApplyColumnSortAsync(string columnName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(columnName)) return;
        LastKnownRowCount = null;
        CurrentPage = 1;

        if (string.Equals(SortColumn, columnName, StringComparison.Ordinal))
        {
            if (!SortDescending) SortDescending = true;
            else { SortColumn = null; SortDescending = false; }
        }
        else
        {
            SortColumn = columnName;
            SortDescending = false;
        }

        await ReloadDataPreviewAsync(cancellationToken).ConfigureAwait(true);
        // Sort reset LastKnownRowCount → re-probe so Record N of M keeps its M.
        await RefreshDataRowCountAsync(cancellationToken).ConfigureAwait(true);
    }

    internal async Task ReloadDataPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null) return;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);

        string? orderBy = null;
        if (!string.IsNullOrEmpty(SortColumn))
        {
            var escaped = SortColumn.Replace("\"", "\"\"");
            orderBy = string.Format(
                CultureInfo.InvariantCulture, "\"{0}\" {1}", escaped, SortDescending ? "DESC" : "ASC");
        }

        try
        {
            var preview = await _reader.GetDataPreviewAsync(ViewName, CurrentPage, PageSize, orderBy, _dataSqlFilter, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataError = string.Empty;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
        }
        catch (MetadataReadException ex)
        {
            DataResult = null;
            DataError = ex.Message;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
        }
    }

    public bool CanGoToFirstPage => HasPreviousPage;
    public bool CanGoToPreviousPage => HasPreviousPage;
    public bool CanGoToNextPage => HasNextPage;
    public bool CanGoToLastPage => _reader is not null && (HasNextPage || LastKnownRowCount is null);

    [RelayCommand(CanExecute = nameof(CanGoToFirstPage))]
    private async Task GoToFirstPageAsync()
    {
        if (CurrentPage == 1) return;
        CurrentPage = 1;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task GoToPreviousPageAsync()
    {
        if (CurrentPage <= 1) return;
        CurrentPage--;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task GoToNextPageAsync()
    {
        if (!HasNextPage) return;
        CurrentPage++;
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanGoToLastPage))]
    private async Task GoToLastPageAsync()
    {
        if (_reader is null) return;
        try
        {
            var count = await _reader.GetRowCountAsync(ViewName, RowCountCap, _dataSqlFilter).ConfigureAwait(true);
            LastKnownRowCount = count;
            if (count <= 0) CurrentPage = 1;
            else
            {
                var lastPage = (count + PageSize - 1) / PageSize;
                CurrentPage = lastPage < 1 ? 1 : lastPage;
            }
        }
        catch (MetadataReadException ex)
        {
            DataError = ex.Message;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
            return;
        }
        await ReloadDataPreviewAsync().ConfigureAwait(true);
    }

    private async Task SafeLoadAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            if (string.IsNullOrEmpty(ErrorMessage)) ErrorMessage = ex.Message;
        }
    }
}
