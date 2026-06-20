using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird relation TRIGGER. Tabs: Editor (Source ⇄ Easy modes)
/// · Description · Dependencies · DDL — consistent with the Procedure / View editors.
/// <list type="bullet">
/// <item>Source mode = the full editable CREATE OR ALTER TRIGGER text.</item>
/// <item>Easy mode = the trigger metadata (Table / Timing / Events / Position / Active)
/// + an editable Variables grid ABOVE a body-only editor (the IBExpert improvement —
/// variable declarations get a structured grid instead of hand-typed DECLAREs).</item>
/// </list>
/// A canonical model {name, table, timing, events, position, active, variables, body}
/// backs both: switching to Easy parses the source (bounded header parser, reusing the
/// shared body splitter for the DECLARE section); switching to Source regenerates the
/// text (deterministic). Compile reassembles + runs in the working (metadata) tx.
/// Name auto-derives from {table, timing, events, position} until the user edits it.
/// </summary>
public partial class TriggerDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, IFieldRowOwner
{
    // Top-tab indices — must match the TabItem order in the view.
    public const int EditorSubTabIndex = 0;

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Cursors / subprograms found in the body are preserved verbatim through the
    // round-trip (Easy mode surfaces only the Variables grid per spec, but a trigger
    // body MAY contain a cursor / local routine — keep them so Compile doesn't drop
    // them). Re-emitted by BuildBodyModel.
    private readonly List<ProcedureCursor> _preservedCursors = new();
    private readonly List<ProcedureSubprogram> _preservedSubprograms = new();

    public TriggerDetailTabViewModel(string triggerName)
        : this(triggerName, null, null, null)
    {
    }

    public TriggerDetailTabViewModel(
        string triggerName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        TriggerName = triggerName;
        EditableTriggerName = triggerName;
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        Variables = new ObservableCollection<ProcedureVariableRowViewModel>();
        AvailableDomains = new ObservableCollection<DomainSpec>();
        AvailableTables = new ObservableCollection<string>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();

        Variables.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(VariablesTabHeader));
            DeleteVariableCommand.NotifyCanExecuteChanged();
            MoveVariableUpCommand.NotifyCanExecuteChanged();
            MoveVariableDownCommand.NotifyCanExecuteChanged();
        };
        // The table list loads async; re-evaluate the picker's selection once it
        // arrives so a header-loaded TableName resolves to the matching item.
        AvailableTables.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedTable));
        TrackDirty(Variables);
        // Release the ctor-time suppression now that all fields are assigned.
        _suppressDirty = false;
    }

    public string TriggerName { get; }

    /// <summary>True for a not-yet-created trigger (New Trigger flow). Authored in
    /// Easy mode with an editable name field; the name auto-derives until the user
    /// types one.</summary>
    public bool IsNew { get; init; }

    public bool CanUseEasyMode => true;

    // ─── Dirty tracking (drives IUnsavedWorkSource + WorkGuard) ───────────
    private bool _isDirty;
    private bool _suppressDirty = true;

    public bool IsDirty => _isDirty;
    internal void ClearDirty() => _isDirty = false;
    private void MarkDirty() { if (!_suppressDirty) _isDirty = true; }

    private void TrackDirty(System.Collections.Specialized.INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, e) =>
        {
            MarkDirty();
            if (e.NewItems is not null)
            {
                foreach (System.ComponentModel.INotifyPropertyChanged row in e.NewItems)
                {
                    row.PropertyChanged += (_, _) => MarkDirty();
                }
            }
        };
    }

    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        var name = string.IsNullOrWhiteSpace(EditableTriggerName) ? TriggerName : EditableTriggerName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedNewTriggerFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedTriggerFormat, name));
    }

    // ─── IFieldRowOwner (Variables grid Type / Domain combos) ─────────────

    public ObservableCollection<DomainSpec> AvailableDomains { get; }

    public IReadOnlyList<string> BasicTypes { get; } = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };

    public void SetAvailableDomains(IEnumerable<DomainSpec> domains)
    {
        AvailableDomains.Clear();
        AvailableDomains.Add(new DomainSpec(UiStrings.DomainNoneOption, string.Empty));
        foreach (var d in domains) AvailableDomains.Add(d);
    }

    // ─── Trigger metadata (Easy mode header) ──────────────────────────────

    /// <summary>Tables available on the active connection — the searchable Table
    /// picker. Populated best-effort by the owner (same shape as the FK wizard).</summary>
    public ObservableCollection<string> AvailableTables { get; }

    public void SetAvailableTables(IEnumerable<string> tables)
    {
        AvailableTables.Clear();
        foreach (var t in tables) AvailableTables.Add(t);
    }

    [ObservableProperty]
    private string _tableName = string.Empty;

    partial void OnTableNameChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedTable));
        MarkDirty();
        MaybeAutoName();
    }

    /// <summary>Wrapper for the Table ComboBox's SelectedItem (TwoWay). Avalonia's
    /// ComboBox nulls SelectedItem and writes null back when the bound value isn't in
    /// ItemsSource (the table list loads async after the header) — that would wipe a
    /// loaded TableName. Ignoring a null write keeps the value (gotcha #71).</summary>
    public string? SelectedTable
    {
        get => string.IsNullOrEmpty(TableName) ? null : TableName;
        set { if (value is not null) TableName = value; }
    }

    public IReadOnlyList<string> TimingOptions { get; } = new[] { "BEFORE", "AFTER" };

    [ObservableProperty]
    private string _selectedTiming = "BEFORE";

    public bool IsBefore => string.Equals(SelectedTiming, "BEFORE", StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedTimingChanged(string value) { MarkDirty(); MaybeAutoName(); }

    [ObservableProperty] private bool _firesInsert;
    [ObservableProperty] private bool _firesUpdate;
    [ObservableProperty] private bool _firesDelete;

    partial void OnFiresInsertChanged(bool value) { MarkDirty(); MaybeAutoName(); }
    partial void OnFiresUpdateChanged(bool value) { MarkDirty(); MaybeAutoName(); }
    partial void OnFiresDeleteChanged(bool value) { MarkDirty(); MaybeAutoName(); }

    [ObservableProperty] private int _position;

    partial void OnPositionChanged(int value)
    {
        OnPropertyChanged(nameof(PositionValue));
        MarkDirty();
        MaybeAutoName();
    }

    /// <summary>decimal? bridge for the NumericUpDown (Avalonia 12 NumericUpDown.Value
    /// is decimal?; the model keeps Position as int). See gotcha #57.</summary>
    public decimal? PositionValue
    {
        get => Position;
        set => Position = value is { } v ? (int)v : 0;
    }

    [ObservableProperty] private bool _active = true;

    partial void OnActiveChanged(bool value) => MarkDirty();

    // ─── Trigger name (auto-derived until overridden) ─────────────────────

    /// <summary>Trigger name shown in Easy mode. Editable in the New Trigger flow
    /// (auto-derives from the metadata until the user types one); read-only for an
    /// existing trigger (Firebird can't rename via CREATE OR ALTER).</summary>
    [ObservableProperty]
    private string _editableTriggerName = string.Empty;

    private bool _autoWritingName;
    private bool _settingNameUpper;
    private string _lastAutoName = string.Empty;
    private bool _userOverrodeName;

    partial void OnEditableTriggerNameChanged(string value)
    {
        // UPPERCASE user-entered names (Firebird folds unquoted identifiers; EmberTern
        // keeps object names uppercase consistently — gotcha #141). Programmatic sets
        // (ctor / load, under _suppressDirty) and the already-uppercase auto-name don't
        // need coercing, so this only fires on a genuine user edit.
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableTriggerName = upper; } finally { _settingNameUpper = false; }
                return; // re-runs this handler with the uppercased value
            }
        }
        MarkDirty();
        // During ctor / programmatic load (_suppressDirty) and during our own
        // auto-write, a value change is not a user override.
        if (_suppressDirty || _autoWritingName) return;
        if (!string.Equals(value, _lastAutoName, StringComparison.Ordinal)) _userOverrodeName = true;
    }

    // Auto-name: {TABLE}_{B|A}{I?}{U?}{D?}_{position}. Fires only for a NEW trigger the
    // user hasn't manually named yet, and only once a table is chosen. Mirrors the FK
    // wizard's "user override sticks" pattern (gotcha #66).
    private void MaybeAutoName()
    {
        if (!IsNew || _userOverrodeName) return;
        if (string.IsNullOrWhiteSpace(TableName)) return;
        var name = DdlGenerator.BuildTriggerName(TableName, IsBefore, FiresInsert, FiresUpdate, FiresDelete, Position);
        _autoWritingName = true;
        try
        {
            EditableTriggerName = name;
            _lastAutoName = name;
        }
        finally { _autoWritingName = false; }
    }

    // ─── Mode ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceMode))]
    private bool _easyMode;

    public bool IsSourceMode => !EasyMode;

    partial void OnEasyModeChanged(bool value)
    {
        // A pure Source⇄Easy toggle is not an edit — suppress the dirty flips the
        // re-population would otherwise cause.
        var prev = _suppressDirty;
        _suppressDirty = true;
        try
        {
            if (value)
            {
                // Nothing loaded yet (mode preference applied at tab creation, before
                // lazy load) — don't parse an empty source / show a spurious notice.
                if (string.IsNullOrWhiteSpace(SourceText)) { ErrorMessage = null; return; }
                var sig = TriggerSignatureParser.Parse(SourceText);
                if (sig.Success)
                {
                    if (!string.IsNullOrWhiteSpace(sig.Name)) EditableTriggerName = sig.Name!;
                    ApplyHeader(sig.Table, sig.IsBefore, sig.FiresInsert, sig.FiresUpdate, sig.FiresDelete, sig.Position, sig.Active);
                    SyncEasyModelFromBody(sig.Body);
                    ErrorMessage = null;
                }
                else
                {
                    ErrorMessage = UiStrings.TriggerParseFailedNotice;
                }
            }
            else
            {
                SourceText = BuildFullSource();
            }
        }
        finally { _suppressDirty = prev; }
    }

    private void ApplyHeader(string table, bool isBefore, bool ins, bool upd, bool del, int position, bool active)
    {
        TableName = table;
        SelectedTiming = isBefore ? "BEFORE" : "AFTER";
        FiresInsert = ins;
        FiresUpdate = upd;
        FiresDelete = del;
        Position = position;
        Active = active;
    }

    /// <summary>Reassembles the full CREATE OR ALTER TRIGGER text from the Easy-mode
    /// model. Defensive — placeholders keep it from throwing while a new trigger is
    /// still being filled in (a real Compile validates the metadata first).</summary>
    internal string BuildFullSource()
    {
        var name = string.IsNullOrWhiteSpace(EditableTriggerName) ? TriggerName : EditableTriggerName.Trim();
        var table = string.IsNullOrWhiteSpace(TableName) ? "TABLE_NAME" : TableName.Trim();
        bool ins = FiresInsert, upd = FiresUpdate, del = FiresDelete;
        if (!(ins || upd || del)) ins = true; // never emit invalid DDL
        return DdlGenerator.BuildCreateOrAlterTrigger(
            name, table, IsBefore, ins, upd, del, Position, Active,
            DdlGenerator.BuildProcedureBody(BuildBodyModel()));
    }

    internal ProcedureBodyModel BuildBodyModel()
    {
        var model = new ProcedureBodyModel { ExecutableBody = ExecutableBody };
        foreach (var v in Variables) model.Variables.Add(v.ToVariable());
        foreach (var c in _preservedCursors) model.Cursors.Add(c);
        foreach (var sp in _preservedSubprograms) model.Subprograms.Add(sp);
        return model;
    }

    /// <summary>Splits a body (text after AS) into the editable Variables collection +
    /// the executable body editor content. Cursors / subprograms (rare in triggers)
    /// are preserved verbatim for the round-trip but not surfaced as editable grids.</summary>
    internal void SyncEasyModelFromBody(string? body)
    {
        var model = ProcedureBodySplitter.Split(body);
        Variables.Clear();
        foreach (var v in model.Variables) Variables.Add(ProcedureVariableRowViewModel.From(v, this));
        _preservedCursors.Clear();
        _preservedCursors.AddRange(model.Cursors);
        _preservedSubprograms.Clear();
        _preservedSubprograms.AddRange(model.Subprograms);
        ExecutableBody = model.ExecutableBody;
    }

    // ─── Editor text ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string _sourceText = string.Empty;

    partial void OnSourceTextChanged(string value) => MarkDirty();

    [ObservableProperty]
    private string _executableBody = string.Empty;

    partial void OnExecutableBodyChanged(string value) => MarkDirty();

    [ObservableProperty]
    private string _ddlText = string.Empty;

    // ─── Variables (editable grid) ────────────────────────────────────────

    public ObservableCollection<ProcedureVariableRowViewModel> Variables { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteVariableCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveVariableUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveVariableDownCommand))]
    private ProcedureVariableRowViewModel? _selectedVariable;

    public string VariablesTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureDetailLocalsVariablesFormat, Variables.Count);

    [RelayCommand]
    private void AddVariable()
    {
        var row = new ProcedureVariableRowViewModel(this);
        Variables.Add(row);
        SelectedVariable = row;
    }

    public bool CanDeleteVariable => SelectedVariable is not null;
    [RelayCommand(CanExecute = nameof(CanDeleteVariable))]
    private void DeleteVariable()
    {
        if (SelectedVariable is null) return;
        var idx = Variables.IndexOf(SelectedVariable);
        if (idx < 0) return;
        Variables.RemoveAt(idx);
        SelectedVariable = Variables.Count > 0 ? Variables[Math.Min(idx, Variables.Count - 1)] : null;
    }

    public bool CanMoveVariableUp => SelectedVariable is not null && Variables.IndexOf(SelectedVariable) > 0;
    public bool CanMoveVariableDown => SelectedVariable is not null
        && Variables.IndexOf(SelectedVariable) is var ix && ix >= 0 && ix < Variables.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveVariableUp))]
    private void MoveVariableUp() => MoveVariable(-1);
    [RelayCommand(CanExecute = nameof(CanMoveVariableDown))]
    private void MoveVariableDown() => MoveVariable(+1);

    private void MoveVariable(int delta)
    {
        if (SelectedVariable is null) return;
        var idx = Variables.IndexOf(SelectedVariable);
        var t = idx + delta;
        if (idx < 0 || t < 0 || t >= Variables.Count) return;
        // RemoveAt + Insert (not Move) — Avalonia DataGrid doesn't reliably re-render
        // a NotifyCollectionChangedAction.Move (same gotcha as the New Table grid).
        var sel = SelectedVariable;
        Variables.RemoveAt(idx);
        Variables.Insert(t, sel);
        SelectedVariable = sel;
    }

    // ─── Description (editable COMMENT ON TRIGGER) ────────────────────────

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionEmpty))]
    private bool _descriptionLoaded;

    public bool HasDescription => DescriptionLoaded && !string.IsNullOrEmpty(Description);
    public bool ShowDescriptionEmpty => DescriptionLoaded && string.IsNullOrEmpty(Description);

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value) => EditableDescription = value ?? string.Empty;

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
        var sql = DdlGenerator.BuildCommentTrigger(TriggerName, comment);
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

    // ─── Format / Comment / Uncomment (act on the active editor) ──────────

    public Func<string?>? SelectedTextProvider { get; set; }
    public Action<string>? ReplaceSelectedOrAllText { get; set; }
    public event Action? CommentRequested;
    public event Action? UncommentRequested;

    [RelayCommand]
    private void FormatSql()
    {
        var selected = SelectedTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : ActiveEditorText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source);
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace) replace(formatted);
        else if (!hasSelection) ActiveEditorText = formatted;
    }

    [RelayCommand]
    private void CommentBody() => CommentRequested?.Invoke();

    [RelayCommand]
    private void UncommentBody() => UncommentRequested?.Invoke();

    private string ActiveEditorText
    {
        get => EasyMode ? ExecutableBody : SourceText;
        set { if (EasyMode) ExecutableBody = value; else SourceText = value; }
    }

    // ─── Dependencies ──────────────────────────────────────────────────────

    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

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

    // ─── Compile ────────────────────────────────────────────────────────────

    public event Action<string?>? TriggerCreated;

    public bool CanCompile => _ddlExecutor is not null;

    internal string BuildCompileSql() => EasyMode ? BuildFullSource() : SourceText;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        if (_ddlExecutor is null) return;

        // In Easy mode a table and at least one event are mandatory — guard before
        // building so the user gets a clear message instead of a server error.
        if (EasyMode)
        {
            if (string.IsNullOrWhiteSpace(TableName))
            {
                ErrorMessage = UiStrings.TriggerTableRequiredNotice;
                return;
            }
            if (!(FiresInsert || FiresUpdate || FiresDelete))
            {
                ErrorMessage = UiStrings.TriggerEventRequiredNotice;
                return;
            }
        }

        var sql = BuildCompileSql();
        if (string.IsNullOrWhiteSpace(sql)) return;

        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TriggerCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TriggerCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            TriggerCreated?.Invoke(TryParseTriggerName(sql));
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    internal static string? TryParseTriggerName(string? sql)
    {
        var sig = TriggerSignatureParser.Parse(sql);
        return sig.Success ? sig.Name : null;
    }

    // ─── Misc bound state ──────────────────────────────────────────────────

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ─── Load / refresh ───────────────────────────────────────────────────

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        => _loadTask ??= LoadAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _loadTask = null;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsNew) return;
        if (_reader is null || _ddlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                SourceText = await _ddlReader.FetchTriggerSourceAsync(
                    new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                var header = await _reader.GetTriggerHeaderAsync(TriggerName, cancellationToken).ConfigureAwait(true);
                ApplyHeader(header.Table, header.IsBefore, header.FiresInsert, header.FiresUpdate, header.FiresDelete, header.Position, header.Active);
            });

            await SafeLoadAsync(async () =>
            {
                var body = await _ddlReader.FetchTriggerBodyAsync(
                    new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
                SyncEasyModelFromBody(body);
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetTriggerDependenciesAsync(TriggerName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
            });

            await SafeLoadAsync(async () =>
            {
                DdlText = await _ddlReader.FetchDdlAsync(
                    new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                Description = await _reader.GetTriggerDescriptionAsync(TriggerName, cancellationToken).ConfigureAwait(true);
                DescriptionLoaded = true;
            });
        }
        finally
        {
            IsLoading = false;
            _suppressDirty = false;
            ClearDirty();
        }
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
