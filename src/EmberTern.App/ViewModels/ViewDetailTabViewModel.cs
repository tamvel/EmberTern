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
public partial class ViewDetailTabViewModel : ViewModelBase
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
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        Fields = new ObservableCollection<FieldInfo>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
    }

    public string ViewName { get; }

    /// <summary>
    /// True for a not-yet-created view (the New View flow). The non-SQL tabs
    /// stay empty until the first successful Compile, after which the owner
    /// reopens the real view. Compile in this mode raises <see cref="ViewCreated"/>.
    /// </summary>
    public bool IsNew { get; init; }

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

    [RelayCommand]
    private void FormatSql()
    {
        var selected = SelectedTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : SourceText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source);
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace)
        {
            replace(formatted);
        }
        else if (!hasSelection)
        {
            // No view callback (tests / headless) — overwrite the whole source.
            SourceText = formatted;
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
        OnPropertyChanged(nameof(HasNextPage));
        GoToNextPageCommand.NotifyCanExecuteChanged();
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
        if (string.IsNullOrWhiteSpace(SourceText)) return;

        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(SourceText, cancellationToken).ConfigureAwait(true);
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
            ViewCreated?.Invoke(TryParseViewName(SourceText));
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
        try
        {
            await SafeLoadAsync(async () =>
            {
                SourceText = await _ddlReader.FetchViewSourceAsync(
                    new MetadataObject(ViewName, MetadataObjectKind.View), cancellationToken).ConfigureAwait(true);
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
                DdlText = await _ddlReader.FetchDdlAsync(
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
        }
    }

    private async Task LoadDataPreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) return;
        try
        {
            var preview = await _reader.GetDataPreviewAsync(ViewName, CurrentPage, PageSize, cancellationToken).ConfigureAwait(true);
            DataResult = preview;
            DataResultVersionTag = Guid.NewGuid().ToString("N");
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
            var preview = await _reader.GetDataPreviewAsync(ViewName, CurrentPage, PageSize, orderBy, cancellationToken).ConfigureAwait(true);
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
            var count = await _reader.GetRowCountAsync(ViewName, RowCountCap).ConfigureAwait(true);
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
