using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>Result of an Execute Procedure/Function run: a result set (or null for a
/// non-result EXECUTE PROCEDURE), an optional error, the work <see cref="Summary"/> (rows
/// changed + rows read) built from the MON$ metrics delta, and the raw per-table
/// <see cref="Reads"/> delta (so the tab feeds its own Performance context + per-table summary).</summary>
public sealed record ProcedureExecOutcome(
    QueryResult? Result,
    string? Error,
    ExecutionSummary? Summary = null,
    IReadOnlyList<PerTableReadRow>? Reads = null);

/// <summary>
/// Detail surface for a Firebird stored PROCEDURE. Tabs: Editor (Source ⇄ Easy modes)
/// · Description · Dependencies · DDL · Result. Shares the routine-editor skeleton
/// (dirty tracking, mode toggle, Format/Comment, Variables grid, Dependencies,
/// Description, Compile, Revert, load lifecycle, field-row owner) with
/// <see cref="SourceObjectDetailTabViewModel"/>; adds the procedure-specific Input/Output
/// parameter grids, the Cursors/Subprograms editors, and Execute Procedure (Data lane).
/// <list type="bullet">
/// <item>Source mode = the full editable CREATE OR ALTER PROCEDURE text.</item>
/// <item>Easy mode = metadata panels (Input/Output params + Variables/Cursors/Subprograms)
/// ABOVE a body-only editor.</item>
/// </list>
/// </summary>
public partial class ProcedureDetailTabViewModel : SourceObjectDetailTabViewModel
{
    private const int InputParamType = 0;
    private const int OutputParamType = 1;

    // Top-tab indices — must match the TabItem order in the view.
    public const int EditorSubTabIndex = 0;
    public const int ResultSubTabIndex = 4;
    public const int PerformanceSubTabIndex = 5;

    public const string NewProcedureTemplate =
        "CREATE OR ALTER PROCEDURE NEW_PROCEDURE\nRETURNS (\n    RESULT INTEGER\n)\nAS\nBEGIN\n    RESULT = 0;\n    SUSPEND;\nEND";

    public ProcedureDetailTabViewModel(string procedureName)
        : this(procedureName, null, null, null)
    {
    }

    public ProcedureDetailTabViewModel(
        string procedureName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
        : base(reader, ddlReader, ddlExecutor)
    {
        ProcedureName = procedureName;
        EditableProcedureName = procedureName;
        InputParams = new ObservableCollection<ProcedureParamRowViewModel>();
        OutputParams = new ObservableCollection<ProcedureParamRowViewModel>();
        Cursors = new ObservableCollection<ProcedureCursorRowViewModel>();
        Subprograms = new ObservableCollection<ProcedureSubprogramRowViewModel>();

        InputParams.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InputTabHeader));
            MoveInputParamUpCommand.NotifyCanExecuteChanged();
            MoveInputParamDownCommand.NotifyCanExecuteChanged();
            DeleteInputParamCommand.NotifyCanExecuteChanged();
        };
        OutputParams.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OutputTabHeader));
            MoveOutputParamUpCommand.NotifyCanExecuteChanged();
            MoveOutputParamDownCommand.NotifyCanExecuteChanged();
            DeleteOutputParamCommand.NotifyCanExecuteChanged();
        };
        Cursors.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CursorsTabHeader));
            DeleteCursorCommand.NotifyCanExecuteChanged();
            MoveCursorUpCommand.NotifyCanExecuteChanged();
            MoveCursorDownCommand.NotifyCanExecuteChanged();
        };
        Subprograms.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SubprogramsTabHeader));
            DeleteSubprogramCommand.NotifyCanExecuteChanged();
            MoveSubprogramUpCommand.NotifyCanExecuteChanged();
            MoveSubprogramDownCommand.NotifyCanExecuteChanged();
        };

        // Dirty tracking: any add/remove/reorder OR row-internal edit in the Easy-mode
        // collections flips the dirty flag (suppressed during programmatic load / mode
        // toggle). Variables is tracked by the base.
        TrackDirty(InputParams);
        TrackDirty(OutputParams);
        TrackDirty(Cursors);
        TrackDirty(Subprograms);

        // Shared filter panel + aggregation bar for the Execute Result grid
        // (materialized: filter/aggregate/page all client-side over the exec result).
        ExecFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyExecFilterAsync };
        ExecAggregationBar = new AggregationBarViewModel(ComputeExecAggregateAsync);

        // Release the ctor-time suppression now that all fields are assigned.
        _suppressDirty = false;
    }

    // ─── Object identity ──────────────────────────────────────────────────

    public string ProcedureName { get; }

    /// <summary>Object name used by Easy mode (the CREATE OR ALTER PROCEDURE header isn't
    /// shown there). Seeded from <see cref="ProcedureName"/> / the parsed source; editable
    /// in the New Procedure flow, read-only display for an existing procedure.</summary>
    [ObservableProperty]
    private string _editableProcedureName = string.Empty;

    private bool _settingNameUpper;
    partial void OnEditableProcedureNameChanged(string value)
    {
        // UPPERCASE user-entered names consistently (gotcha #141). Programmatic sets
        // (ctor / load / parse, under _suppressDirty) don't need coercing.
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableProcedureName = upper; } finally { _settingNameUpper = false; }
                return;
            }
        }
        MarkDirty();
    }

    // ─── Parameters (editable) ────────────────────────────────────────────

    public ObservableCollection<ProcedureParamRowViewModel> InputParams { get; }
    public ObservableCollection<ProcedureParamRowViewModel> OutputParams { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteInputParamCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveInputParamUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveInputParamDownCommand))]
    private ProcedureParamRowViewModel? _selectedInputParam;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteOutputParamCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveOutputParamUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveOutputParamDownCommand))]
    private ProcedureParamRowViewModel? _selectedOutputParam;

    public string InputTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureDetailParamInputFormat, InputParams.Count);
    public string OutputTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureDetailParamOutputFormat, OutputParams.Count);

    [RelayCommand]
    private void AddInputParam()
    {
        var row = new ProcedureParamRowViewModel(this);
        InputParams.Add(row);
        SelectedInputParam = row;
    }

    [RelayCommand]
    private void AddOutputParam()
    {
        var row = new ProcedureParamRowViewModel(this) { IsOutput = true };
        OutputParams.Add(row);
        SelectedOutputParam = row;
    }

    public bool CanDeleteInputParam => SelectedInputParam is not null;
    public bool CanDeleteOutputParam => SelectedOutputParam is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteInputParam))]
    private void DeleteInputParam() => SelectedInputParam = DeleteParam(InputParams, SelectedInputParam);

    [RelayCommand(CanExecute = nameof(CanDeleteOutputParam))]
    private void DeleteOutputParam() => SelectedOutputParam = DeleteParam(OutputParams, SelectedOutputParam);

    public bool CanMoveInputParamUp => CanMoveUp(InputParams, SelectedInputParam);
    public bool CanMoveInputParamDown => CanMoveDown(InputParams, SelectedInputParam);
    public bool CanMoveOutputParamUp => CanMoveUp(OutputParams, SelectedOutputParam);
    public bool CanMoveOutputParamDown => CanMoveDown(OutputParams, SelectedOutputParam);

    [RelayCommand(CanExecute = nameof(CanMoveInputParamUp))]
    private void MoveInputParamUp() => MoveParam(InputParams, SelectedInputParam, -1);

    [RelayCommand(CanExecute = nameof(CanMoveInputParamDown))]
    private void MoveInputParamDown() => MoveParam(InputParams, SelectedInputParam, +1);

    [RelayCommand(CanExecute = nameof(CanMoveOutputParamUp))]
    private void MoveOutputParamUp() => MoveParam(OutputParams, SelectedOutputParam, -1);

    [RelayCommand(CanExecute = nameof(CanMoveOutputParamDown))]
    private void MoveOutputParamDown() => MoveParam(OutputParams, SelectedOutputParam, +1);

    private static ProcedureParamRowViewModel? DeleteParam(ObservableCollection<ProcedureParamRowViewModel> coll, ProcedureParamRowViewModel? sel)
    {
        if (sel is null) return null;
        var idx = coll.IndexOf(sel);
        if (idx < 0) return sel;
        coll.RemoveAt(idx);
        return coll.Count > 0 ? coll[Math.Min(idx, coll.Count - 1)] : null;
    }

    private static bool CanMoveUp(ObservableCollection<ProcedureParamRowViewModel> coll, ProcedureParamRowViewModel? sel)
        => sel is not null && coll.IndexOf(sel) > 0;

    private static bool CanMoveDown(ObservableCollection<ProcedureParamRowViewModel> coll, ProcedureParamRowViewModel? sel)
        => sel is not null && coll.IndexOf(sel) >= 0 && coll.IndexOf(sel) < coll.Count - 1;

    private void MoveParam(ObservableCollection<ProcedureParamRowViewModel> coll, ProcedureParamRowViewModel? sel, int delta)
    {
        if (sel is null) return;
        var idx = coll.IndexOf(sel);
        var t = idx + delta;
        if (idx < 0 || t < 0 || t >= coll.Count) return;
        // RemoveAt + Insert (not Move) — Avalonia DataGrid doesn't reliably re-render
        // a NotifyCollectionChangedAction.Move (same gotcha as the New Table grid).
        coll.RemoveAt(idx);
        coll.Insert(t, sel);
        if (ReferenceEquals(coll, InputParams)) SelectedInputParam = sel;
        else SelectedOutputParam = sel;
    }

    private void ReplaceParams(ObservableCollection<ProcedureParamRowViewModel> coll, IReadOnlyList<ProcedureParameter> source, bool isOutput)
    {
        coll.Clear();
        foreach (var p in source) coll.Add(ProcedureParamRowViewModel.From(p, this, isOutput));
    }

    // ─── Easy-mode model (params + DECLARE section + body) ────────────────

    /// <summary>Reassembles the full CREATE OR ALTER PROCEDURE text from the Easy-mode
    /// structured model (params + regenerated DECLARE section + executable body).</summary>
    internal override string BuildFullSource()
        => DdlGenerator.BuildCreateOrAlterProcedure(
            string.IsNullOrWhiteSpace(EditableProcedureName) ? ProcedureName : EditableProcedureName.Trim(),
            InputParams.Select(p => p.ToParameter()).ToList(),
            OutputParams.Select(p => p.ToParameter()).ToList(),
            DdlGenerator.BuildProcedureBody(BuildBodyModel()));

    /// <summary>Collects the editable Variables / Cursors / Subprograms / executable body
    /// into a Core <see cref="ProcedureBodyModel"/>.</summary>
    internal ProcedureBodyModel BuildBodyModel()
    {
        var model = new ProcedureBodyModel { ExecutableBody = ExecutableBody };
        foreach (var v in Variables) model.Variables.Add(v.ToVariable());
        foreach (var c in Cursors) model.Cursors.Add(c.ToCursor());
        foreach (var sp in Subprograms) model.Subprograms.Add(sp.ToSubprogram());
        return model;
    }

    /// <summary>Splits a body (text after AS) into the editable Variables / Cursors /
    /// Subprograms collections + the executable body editor content.</summary>
    internal void SyncEasyModelFromBody(string? body)
    {
        var model = ProcedureBodySplitter.Split(body);
        Variables.Clear();
        foreach (var v in model.Variables) Variables.Add(ProcedureVariableRowViewModel.From(v, this));
        Cursors.Clear();
        foreach (var c in model.Cursors) Cursors.Add(ProcedureCursorRowViewModel.From(c));
        Subprograms.Clear();
        foreach (var sp in model.Subprograms) Subprograms.Add(ProcedureSubprogramRowViewModel.From(sp));
        ExecutableBody = model.ExecutableBody;
        SelectedCursor = Cursors.Count > 0 ? Cursors[0] : null;
        SelectedSubprogram = Subprograms.Count > 0 ? Subprograms[0] : null;
    }

    // ─── Editable locals — Cursors (list + split editor) ──────────────────

    public ObservableCollection<ProcedureCursorRowViewModel> Cursors { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCursorCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCursorUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCursorDownCommand))]
    private ProcedureCursorRowViewModel? _selectedCursor;

    public string CursorsTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureDetailLocalsCursorsFormat, Cursors.Count);

    [RelayCommand]
    private void AddCursor()
    {
        var row = new ProcedureCursorRowViewModel { Declaration = UiStrings.ProcedureSnippetCursor };
        Cursors.Add(row);
        SelectedCursor = row;
    }

    public bool CanDeleteCursor => SelectedCursor is not null;
    [RelayCommand(CanExecute = nameof(CanDeleteCursor))]
    private void DeleteCursor() => SelectedCursor = DeleteRow(Cursors, SelectedCursor);

    public bool CanMoveCursorUp => CanUp(Cursors, SelectedCursor);
    public bool CanMoveCursorDown => CanDown(Cursors, SelectedCursor);
    [RelayCommand(CanExecute = nameof(CanMoveCursorUp))]
    private void MoveCursorUp() => SelectedCursor = MoveRow(Cursors, SelectedCursor, -1);
    [RelayCommand(CanExecute = nameof(CanMoveCursorDown))]
    private void MoveCursorDown() => SelectedCursor = MoveRow(Cursors, SelectedCursor, +1);

    // ─── Editable locals — Subprograms (list + split editor) ──────────────

    public ObservableCollection<ProcedureSubprogramRowViewModel> Subprograms { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSubprogramCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSubprogramUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSubprogramDownCommand))]
    private ProcedureSubprogramRowViewModel? _selectedSubprogram;

    public string SubprogramsTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureDetailLocalsSubprogramsFormat, Subprograms.Count);

    /// <summary>Set by the view — asks the user whether the new subprogram is a
    /// PROCEDURE or a FUNCTION (returns the chosen kind, or null on cancel).</summary>
    public Func<Task<string?>>? SubprogramKindRequested { get; set; }

    [RelayCommand]
    private async Task AddSubprogram()
    {
        var kind = SubprogramKindRequested is not null
            ? await SubprogramKindRequested().ConfigureAwait(true)
            : "PROCEDURE";
        if (kind is null) return; // cancelled
        var template = string.Equals(kind, "FUNCTION", StringComparison.OrdinalIgnoreCase)
            ? UiStrings.ProcedureSnippetFunction
            : UiStrings.ProcedureSnippetSubprogram;
        var row = new ProcedureSubprogramRowViewModel { Declaration = template };
        Subprograms.Add(row);
        SelectedSubprogram = row;
    }

    public bool CanDeleteSubprogram => SelectedSubprogram is not null;
    [RelayCommand(CanExecute = nameof(CanDeleteSubprogram))]
    private void DeleteSubprogram() => SelectedSubprogram = DeleteRow(Subprograms, SelectedSubprogram);

    public bool CanMoveSubprogramUp => CanUp(Subprograms, SelectedSubprogram);
    public bool CanMoveSubprogramDown => CanDown(Subprograms, SelectedSubprogram);
    [RelayCommand(CanExecute = nameof(CanMoveSubprogramUp))]
    private void MoveSubprogramUp() => SelectedSubprogram = MoveRow(Subprograms, SelectedSubprogram, -1);
    [RelayCommand(CanExecute = nameof(CanMoveSubprogramDown))]
    private void MoveSubprogramDown() => SelectedSubprogram = MoveRow(Subprograms, SelectedSubprogram, +1);

    // ─── Unified collection edit (main-toolbar Section 3) ─────────────────
    //
    // The toolbar binds ONE set of Add/Remove/Move buttons; this routes them to the
    // active Easy sub-tab's collection (0=Input 1=Output 2=Variables 3=Cursors
    // 4=Subprograms). ActiveEasyCollectionIndex is bound to the Easy sub-tab
    // TabControl's SelectedIndex.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemDownCommand))]
    private int _activeEasyCollectionIndex;

    private (IRelayCommand Add, IRelayCommand Remove, IRelayCommand Up, IRelayCommand Down) EasyCommands()
        => ActiveEasyCollectionIndex switch
        {
            0 => (AddInputParamCommand, DeleteInputParamCommand, MoveInputParamUpCommand, MoveInputParamDownCommand),
            1 => (AddOutputParamCommand, DeleteOutputParamCommand, MoveOutputParamUpCommand, MoveOutputParamDownCommand),
            2 => (AddVariableCommand, DeleteVariableCommand, MoveVariableUpCommand, MoveVariableDownCommand),
            3 => (AddCursorCommand, DeleteCursorCommand, MoveCursorUpCommand, MoveCursorDownCommand),
            _ => (AddSubprogramCommand, DeleteSubprogramCommand, MoveSubprogramUpCommand, MoveSubprogramDownCommand),
        };

    /// <summary>All five Easy collections support Add/Remove/reorder.</summary>
    public bool CollectionSupportsReorder => true;

    [RelayCommand]
    private void AddCollectionItem() => EasyCommands().Add.Execute(null);

    private bool CanRemoveCollectionItem() => EasyCommands().Remove.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanRemoveCollectionItem))]
    private void RemoveCollectionItem() => EasyCommands().Remove.Execute(null);

    private bool CanMoveCollectionItemUp() => EasyCommands().Up.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanMoveCollectionItemUp))]
    private void MoveCollectionItemUp() => EasyCommands().Up.Execute(null);

    private bool CanMoveCollectionItemDown() => EasyCommands().Down.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanMoveCollectionItemDown))]
    private void MoveCollectionItemDown() => EasyCommands().Down.Execute(null);

    // ─── Execute Procedure (Data lane, parameterized) ────────────────────

    /// <summary>Set by the view — opens the parameter dialog for the given input params
    /// and returns ordered values (null entry = SQL NULL), or null when the user cancels.</summary>
    public Func<IReadOnlyList<ProcedureParamRowViewModel>, Task<IReadOnlyList<object?>?>>? ExecuteParamsRequested { get; set; }

    /// <summary>Set by the owner — runs the built statement on the Data lane with bound
    /// parameters and returns the outcome.</summary>
    public Func<string, IReadOnlyList<QueryParameter>, Task<ProcedureExecOutcome>>? RunExecuteRequested { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecResult))]
    [NotifyPropertyChangedFor(nameof(ShowExecError))]
    private QueryResult? _execResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecResult))]
    [NotifyPropertyChangedFor(nameof(ShowExecError))]
    private string _execError = string.Empty;

    // Bumped on each result-set change / page change so the code-behind rebuilds
    // (or re-slices) the result grid.
    [ObservableProperty]
    private string _execResultVersionTag = string.Empty;

    public bool HasExecResult => ExecResult is { HasResultSet: true } && string.IsNullOrEmpty(ExecError);
    public bool ShowExecError => !string.IsNullOrEmpty(ExecError);

    /// <summary>This procedure tab's OWN Performance context (its captured run + panel). Set by
    /// the owning MainWindowViewModel factory. Never shared with the SQL Editor or another tab.</summary>
    public HostPerformanceContext? PerformanceContext { get; internal set; }

    /// <summary>The Performance sub-tab panel for THIS procedure — analyzes only this tab's last
    /// Execute. Null until the factory wires the context.</summary>
    public PerformancePanelViewModel? Performance => PerformanceContext?.Panel;

    // Persistent execution info (status / time / rows / affected / error) shown in the
    // bottom panel — feedback even for procedures that return no result set.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExecSummaryFallbackText))]
    private string _execInfo = string.Empty;

    // The one-line summary shown as the exec-info Expander's header (collapsed state).
    [ObservableProperty]
    private string _execInfoCompact = string.Empty;

    [ObservableProperty]
    private bool _execInfoIsError;

    public bool HasExecInfo => !string.IsNullOrEmpty(ExecInfo);

    partial void OnExecInfoChanged(string value) => OnPropertyChanged(nameof(HasExecInfo));

    /// <summary>Per-table CHANGE breakdown for the exec-info Expander's expanded body (table name
    /// + inserted/updated/deleted), built from the MON$ delta. Reads are NOT here — they live in
    /// the Performance tab.</summary>
    public ObservableCollection<TableActivityLine> ExecTableActivity { get; } = new();

    public bool HasExecTableActivity => ExecTableActivity.Count > 0;

    // True when the run's change counts were measured (MON$ delta captured). Distinguishes a
    // genuine read-only run ("nothing changed") from a degraded run where MON$ was unavailable.
    private bool _execChangesMeasured;

    /// <summary>The clean, styled line shown in the expanded Execution Summary when there are no
    /// per-table change cards: a genuine read-only run says "nothing changed"; a run whose changes
    /// weren't measured falls back to the concise aggregate. Never the raw multi-line dump.</summary>
    public string ExecSummaryFallbackText =>
        _execChangesMeasured ? UiStrings.ExecutionSummaryNoChanges : ExecInfo;

    // ── Result grid: client-side paging over the materialized result set
    // (≤5000 rows, executor-capped). Mirrors the SQL editor's paging shape, but a
    // procedure may have side effects so we NEVER re-execute per page — slice the
    // already-fetched rows in memory. ──
    public const int ExecResultPageSize = 200;
    private List<object?[]> _execAllRows = new(); // full (unfiltered) exec result
    private List<object?[]> _execRows = new();    // filtered + displayed
    private int _execPage = 1;
    private int _selectedExecRowInPage = -1; // selection within the current page; -1 = none

    // ── Execute Result grid: shared filter panel + aggregation bar (client-side) ──
    public FilterPanelViewModel ExecFilterPanel { get; }
    public AggregationBarViewModel ExecAggregationBar { get; }
    private GridFilter _execFilter = GridFilter.Empty;

    private List<object?[]> ApplyExecFilter(IReadOnlyList<object?[]> rows)
    {
        if (_execFilter.IsEmpty || ExecResult is null) return new List<object?[]>(rows);
        var cols = ExecResult.Columns;
        var list = new List<object?[]>();
        foreach (var r in rows)
            if (GridFilterEvaluator.Matches(r, _execFilter, cols)) list.Add(r);
        return list;
    }

    private Task ApplyExecFilterAsync(GridFilter filter)
    {
        _execFilter = filter;
        _execRows = ApplyExecFilter(_execAllRows);
        _execPage = 1;
        RebuildExecPage();
        return ExecAggregationBar.RecomputeAllAsync();
    }

    private Task<object?> ComputeExecAggregateAsync(GridColumnRef col, GridAggregate agg)
        => Task.FromResult(GridAggregator.Compute(_execRows, col.Index, agg, col.ClrType));

    public IReadOnlyList<object?[]> PagedExecRows { get; private set; } = Array.Empty<object?[]>();
    private int TotalExecPages => _execRows.Count == 0 ? 1 : (_execRows.Count + ExecResultPageSize - 1) / ExecResultPageSize;
    public int ExecPage => _execPage;
    public bool HasExecPreviousPage => _execPage > 1;
    public bool HasExecNextPage => _execPage < TotalExecPages;
    public string ExecPaginationHint => HasExecResult
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.ResultsPaginationHintFormat, _execPage, TotalExecPages, _execRows.Count)
        : string.Empty;

    // IBExpert-style "Record N of M" over the full (materialized) exec result.
    public string ExecRecordInfo
    {
        get
        {
            int total = _execRows.Count;
            if (total == 0) return string.Empty;
            if (_selectedExecRowInPage >= 0)
            {
                int global = (_execPage - 1) * ExecResultPageSize + _selectedExecRowInPage + 1;
                return string.Format(CultureInfo.CurrentCulture, UiStrings.RecordPositionFormat, global, total);
            }
            return string.Format(CultureInfo.CurrentCulture, UiStrings.RecordCountFormat, total);
        }
    }

    // Called by the view when the exec-result grid selection changes.
    public void SetExecSelectedRow(int indexInPage)
    {
        if (_selectedExecRowInPage == indexInPage) return;
        _selectedExecRowInPage = indexInPage;
        OnPropertyChanged(nameof(ExecRecordInfo));
    }

    partial void OnExecResultChanged(QueryResult? value)
    {
        _execAllRows = value?.Rows is { } rows ? new List<object?[]>(rows) : new List<object?[]>();
        _execFilter = GridFilter.Empty;
        var cols = GridColumnRef.From(value is { HasResultSet: true } ? value.Columns : null);
        ExecFilterPanel.SetColumns(cols);
        ExecAggregationBar.SetColumns(cols);
        _execRows = ApplyExecFilter(_execAllRows);
        _execPage = 1;
        RebuildExecPage();
    }

    private void RebuildExecPage()
    {
        if (_execPage > TotalExecPages) _execPage = TotalExecPages;
        if (_execPage < 1) _execPage = 1;
        int start = (_execPage - 1) * ExecResultPageSize;
        int count = Math.Min(ExecResultPageSize, _execRows.Count - start);
        PagedExecRows = count > 0 ? _execRows.GetRange(start, count) : Array.Empty<object?[]>();

        // Re-slicing the page drops any grid selection; reset the record pointer.
        _selectedExecRowInPage = -1;

        OnPropertyChanged(nameof(PagedExecRows));
        OnPropertyChanged(nameof(ExecPage));
        OnPropertyChanged(nameof(HasExecPreviousPage));
        OnPropertyChanged(nameof(HasExecNextPage));
        OnPropertyChanged(nameof(ExecPaginationHint));
        OnPropertyChanged(nameof(ExecRecordInfo));
        ExecFirstPageCommand.NotifyCanExecuteChanged();
        ExecPreviousPageCommand.NotifyCanExecuteChanged();
        ExecNextPageCommand.NotifyCanExecuteChanged();
        ExecLastPageCommand.NotifyCanExecuteChanged();
        ExecResultVersionTag = Guid.NewGuid().ToString("N");
    }

    [RelayCommand(CanExecute = nameof(HasExecPreviousPage))]
    private void ExecFirstPage() { _execPage = 1; RebuildExecPage(); }

    [RelayCommand(CanExecute = nameof(HasExecPreviousPage))]
    private void ExecPreviousPage() { if (_execPage > 1) { _execPage--; RebuildExecPage(); } }

    [RelayCommand(CanExecute = nameof(HasExecNextPage))]
    private void ExecNextPage() { if (HasExecNextPage) { _execPage++; RebuildExecPage(); } }

    [RelayCommand(CanExecute = nameof(HasExecNextPage))]
    private void ExecLastPage() { _execPage = TotalExecPages; RebuildExecPage(); }

    public bool CanExecuteProcedure => RunExecuteRequested is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanExecuteProcedure))]
    private async Task ExecuteProcedure()
    {
        if (RunExecuteRequested is null) return;

        IReadOnlyList<object?> values = Array.Empty<object?>();
        if (InputParams.Count > 0)
        {
            if (ExecuteParamsRequested is null) return;
            var collected = await ExecuteParamsRequested(InputParams.ToList()).ConfigureAwait(true);
            if (collected is null) return; // cancelled
            values = collected;
        }

        var (sql, parameters) = BuildExecuteStatement(values);

        ExecError = string.Empty;
        var outcome = await RunExecuteRequested(sql, parameters).ConfigureAwait(true);
        if (outcome.Error is { } err)
        {
            ExecResult = null;
            ExecError = err;
            ExecInfo = err;
            ExecInfoCompact = err;
            ExecInfoIsError = true;
        }
        else
        {
            ExecResult = outcome.Result;
            ExecError = string.Empty;
            _execChangesMeasured = outcome.Summary?.ChangesMeasured ?? (outcome.Reads is not null);
            ExecInfo = BuildExecInfo(outcome);
            ExecInfoCompact = BuildExecInfoCompact(outcome);
            ExecInfoIsError = false;
            ApplyExecTableActivity(outcome.Reads);
            OnPropertyChanged(nameof(ExecSummaryFallbackText));
            // Feed THIS tab's OWN Performance context (never the SQL Editor / another tab).
            PerformanceContext?.Record(sql, outcome.Result, outcome.Reads);
            // Only jump to the Result tab when there are rows — a no-result-set
            // procedure (EXECUTE PROCEDURE) gives feedback via the bottom info panel
            // instead of an empty grid.
            if (HasExecResult) ActiveSubTabIndex = ResultSubTabIndex;
        }
    }

    // Rebuild the per-table exec breakdown (expanded exec-info body) from the MON$ delta.
    private void ApplyExecTableActivity(IReadOnlyList<PerTableReadRow>? reads)
    {
        ExecTableActivity.Clear();
        foreach (var line in ExecutionActivity.Build(reads))
        {
            ExecTableActivity.Add(line);
        }
        OnPropertyChanged(nameof(HasExecTableActivity));
    }

    // Execution Metrics: a result-returning proc shows "N rows in T ms" (+ rows read); a
    // non-result EXECUTE PROCEDURE shows the multi-line work summary (rows changed + rows
    // read, or "No data modifications detected." when it only read).
    private static string BuildExecInfo(ProcedureExecOutcome outcome)
    {
        var r = outcome.Result;
        if (r is null) return UiStrings.ProcedureExecCompleted;
        var ms = (long)r.Elapsed.TotalMilliseconds;
        if (r.HasResultSet)
        {
            var line = string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoRowsFormat, r.Rows.Count, ms);
            if (outcome.Summary is { ReadsMeasured: true, RowsRead: > 0 } s)
            {
                line += "\n\n" + string.Format(CultureInfo.InvariantCulture, "{0} rows read", s.RowsRead);
            }
            return line;
        }
        if (outcome.Summary is { } summary)
        {
            return summary.BuildDetailedMessage();
        }
        return string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoCompletedFormat, ms);
    }

    // The collapsed one-line counterpart of BuildExecInfo — shown as the exec-info panel's
    // Expander header. A result-returning proc keeps "N rows in T ms" (+ read); a non-result
    // EXECUTE PROCEDURE shows the compact work line ("Executed in T ms · 8 inserted · 376 read").
    private static string BuildExecInfoCompact(ProcedureExecOutcome outcome)
    {
        var r = outcome.Result;
        if (r is null) return UiStrings.ProcedureExecCompleted;
        var ms = (long)r.Elapsed.TotalMilliseconds;
        if (r.HasResultSet)
        {
            var line = string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoRowsFormat, r.Rows.Count, ms);
            if (outcome.Summary is { ReadsMeasured: true, RowsRead: > 0 } s)
            {
                line += string.Format(CultureInfo.InvariantCulture, " · {0} read", s.RowsRead);
            }
            return line;
        }
        if (outcome.Summary is { } summary)
        {
            return summary.BuildCompactLine();
        }
        return string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoCompletedFormat, ms);
    }

    /// <summary>Builds the EXECUTE statement + bound parameters from ordered input values.
    /// Selectable (SELECT * FROM) when the procedure has outputs and its body contains a
    /// SUSPEND; otherwise EXECUTE PROCEDURE. Internal + pure for tests.</summary>
    internal (string Sql, IReadOnlyList<QueryParameter> Parameters) BuildExecuteStatement(IReadOnlyList<object?> values)
    {
        var names = new List<string>();
        var parameters = new List<QueryParameter>();
        for (int k = 0; k < values.Count; k++)
        {
            var pn = "@p" + k.ToString(CultureInfo.InvariantCulture);
            names.Add(pn);
            parameters.Add(new QueryParameter(pn, values[k]));
        }
        var args = names.Count > 0 ? "(" + string.Join(", ", names) + ")" : string.Empty;
        var quoted = QuoteName(ProcedureName);
        var sql = IsSelectable()
            ? "SELECT * FROM " + quoted + args
            : "EXECUTE PROCEDURE " + quoted + args;
        return (sql, parameters);
    }

    private bool IsSelectable()
        => OutputParams.Count > 0 && Regex.IsMatch(ExecutableBody ?? string.Empty, @"\bSUSPEND\b", RegexOptions.IgnoreCase);

    private static string QuoteName(string name)
        => "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";

    // ─── Object-specific hooks (SourceObjectDetailTabViewModel) ───────────

    protected override string ObjectDisplayName =>
        string.IsNullOrWhiteSpace(EditableProcedureName) ? ProcedureName : EditableProcedureName.Trim();

    protected override string ParseFailedNotice => UiStrings.ProcedureParseFailedNotice;
    protected override string CompileFailedFormat => UiStrings.ProcedureCompileFailedFormat;
    protected override string UnsavedNewFormat => UiStrings.UnsavedNewProcedureFormat;
    protected override string UnsavedModifiedFormat => UiStrings.UnsavedModifiedProcedureFormat;

    protected override string CommentSql(string? comment)
        => DdlGenerator.BuildCommentProcedure(ProcedureName, comment);

    protected override bool TryApplySource(string source)
    {
        var sig = ProcedureSignatureParser.Parse(source);
        if (!sig.Success) return false;
        if (!string.IsNullOrWhiteSpace(sig.Name)) EditableProcedureName = sig.Name!;
        ReplaceParams(InputParams, sig.Inputs, isOutput: false);
        ReplaceParams(OutputParams, sig.Outputs, isOutput: true);
        SyncEasyModelFromBody(sig.Body);
        return true;
    }

    protected override string? TryParseName(string? sql) => TryParseProcedureName(sql);

    internal static string? TryParseProcedureName(string? sql)
    {
        var sig = ProcedureSignatureParser.Parse(sql);
        return sig.Success ? sig.Name : null;
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        await SafeLoadAsync(async () =>
        {
            SourceText = await DdlReader!.FetchProcedureSourceAsync(
                new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
        });

        await SafeLoadAsync(async () =>
        {
            var body = await DdlReader!.FetchProcedureBodyAsync(
                new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
            // Split the body into the editable structured model (Variables / Cursors /
            // Subprograms + executable body).
            SyncEasyModelFromBody(body);
        });

        await SafeLoadAsync(async () =>
        {
            var inputs = await Reader!.GetProcedureParametersAsync(ProcedureName, InputParamType, cancellationToken).ConfigureAwait(true);
            InputParams.Clear();
            foreach (var p in inputs) InputParams.Add(ProcedureParamRowViewModel.From(p, this, isOutput: false));
        });

        await SafeLoadAsync(async () =>
        {
            var outputs = await Reader!.GetProcedureParametersAsync(ProcedureName, OutputParamType, cancellationToken).ConfigureAwait(true);
            OutputParams.Clear();
            foreach (var p in outputs) OutputParams.Add(ProcedureParamRowViewModel.From(p, this, isOutput: true));
        });

        await SafeLoadAsync(async () =>
        {
            var (dependsOn, dependedOnBy) = await Reader!.GetProcedureDependenciesAsync(ProcedureName, cancellationToken).ConfigureAwait(true);
            DependsOnTree.Clear();
            foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
            DependedOnByTree.Clear();
            foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
        });

        await SafeLoadAsync(async () =>
        {
            // DDL tab == Export (structure + COMMENT ON via MetadataExportService); the
            // editable Source (Editor tab) is untouched.
            DdlText = await new MetadataExportService(DdlReader!, Reader!).BuildObjectScriptAsync(
                new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
        });

        await SafeLoadAsync(async () =>
        {
            Description = await Reader!.GetProcedureDescriptionAsync(ProcedureName, cancellationToken).ConfigureAwait(true);
            DescriptionLoaded = true;
        });
    }
}
