using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird PSQL FUNCTION. Mirrors the Procedure editor —
/// "Function = Procedure + the differences that follow from a function's nature":
/// <list type="bullet">
/// <item>Input parameters → <b>Arguments</b>.</item>
/// <item>Output parameters → <b>Result</b> — a function returns ONE value, so the Result
/// sub-tab is a single row (no Add/Remove/Move) plus a Deterministic flag.</item>
/// <item>Execute runs the function (<c>SELECT fn(...) FROM RDB$DATABASE</c>) — its runtime
/// output lands in the separate <b>Execute Result</b> tab (distinct from the Easy-mode
/// Result metadata).</item>
/// </list>
/// Everything else (Source ⇄ Easy, Variables/Cursors/Subprograms, Dependencies,
/// Description, Compile, Revert, Format/Comment) comes from
/// <see cref="SourceObjectDetailTabViewModel"/>, so the user works in the same editor.
/// </summary>
public partial class FunctionDetailTabViewModel : SourceObjectDetailTabViewModel
{
    // Top-tab indices — must match the TabItem order in the view.
    public const int EditorSubTabIndex = 0;
    public const int ExecuteResultSubTabIndex = 4;
    public const int PerformanceSubTabIndex = 5;

    // Easy-mode sub-tab indices (Arguments / Result / Variables / Cursors / Subprograms).
    // Result (1) is a single record — no collection toolbar.
    private const int ResultEasyIndex = 1;
    // The two Easy sub-tabs that host an SQL editor of their own: diagnostics navigation (S5) has to
    // select the right one before the caret it just moved can be seen.
    public const int CursorsEasyIndex = 3;
    public const int SubprogramsEasyIndex = 4;

    public const string NewFunctionTemplate =
        "CREATE OR ALTER FUNCTION NEW_FUNCTION\nRETURNS INTEGER\nAS\nBEGIN\n    RETURN 0;\nEND";

    public FunctionDetailTabViewModel(string functionName)
        : this(functionName, null, null, null)
    {
    }

    public FunctionDetailTabViewModel(
        string functionName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
        : base(reader, ddlReader, ddlExecutor)
    {
        FunctionName = functionName;
        EditableFunctionName = functionName;
        Arguments = new ObservableCollection<ProcedureParamRowViewModel>();
        ResultRows = new ObservableCollection<ProcedureParamRowViewModel>();
        Cursors = new ObservableCollection<ProcedureCursorRowViewModel>();
        Subprograms = new ObservableCollection<ProcedureSubprogramRowViewModel>();

        // Result is always exactly one row (the return value).
        ResultRows.Add(new ProcedureParamRowViewModel(this) { IsOutput = true });

        Arguments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ArgumentsTabHeader));
            DeleteArgumentCommand.NotifyCanExecuteChanged();
            MoveArgumentUpCommand.NotifyCanExecuteChanged();
            MoveArgumentDownCommand.NotifyCanExecuteChanged();
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

        TrackDirty(Arguments);
        TrackDirty(ResultRows);
        TrackDirty(Cursors);
        TrackDirty(Subprograms);

        // Ambient-symbol refresh: the arguments feed BuildAmbientSymbols, so a rename/add/remove must
        // rebuild the body editor's model (Variables is tracked by the base; ResultRows is the return
        // value, not an in-body symbol).
        TrackAmbient(Arguments);

        // Shared filter panel + aggregation bar for the Execute Result grid
        // (materialized: filter/aggregate/page all client-side over the exec result).
        ExecFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyExecFilterAsync };
        ExecAggregationBar = new AggregationBarViewModel(ComputeExecAggregateAsync);

        // Release the ctor-time suppression now that all fields are assigned.
        _suppressDirty = false;
    }

    // ─── Object identity ──────────────────────────────────────────────────

    public string FunctionName { get; }

    /// <summary>Object name used by Easy mode. Seeded from <see cref="FunctionName"/> /
    /// the parsed source; editable in the New Function flow, read-only display for an
    /// existing function.</summary>
    [ObservableProperty]
    private string _editableFunctionName = string.Empty;

    private bool _settingNameUpper;
    partial void OnEditableFunctionNameChanged(string value)
    {
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableFunctionName = upper; } finally { _settingNameUpper = false; }
                return;
            }
        }
        MarkDirty();
    }

    // ─── Arguments (editable) ─────────────────────────────────────────────

    public ObservableCollection<ProcedureParamRowViewModel> Arguments { get; }

    /// <summary>Easy-mode body editor: the body text declares neither the arguments nor the
    /// variables (they live in the grids), so seed both into the model. See
    /// <see cref="SourceObjectDetailTabViewModel.BuildAmbientSymbols"/>.</summary>
    public override IReadOnlyList<Symbol> BuildAmbientSymbols()
    {
        var symbols = new List<Symbol>();
        foreach (var a in Arguments)
        {
            var name = a.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            symbols.Add(new ParameterSymbol(name) { Direction = ParameterDirection.Input });
        }
        AddVariableSymbols(symbols);
        return symbols;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteArgumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveArgumentUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveArgumentDownCommand))]
    private ProcedureParamRowViewModel? _selectedArgument;

    public string ArgumentsTabHeader =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.FunctionDetailArgumentsFormat, Arguments.Count);

    [RelayCommand]
    private void AddArgument()
    {
        var row = new ProcedureParamRowViewModel(this);
        Arguments.Add(row);
        SelectedArgument = row;
    }

    public bool CanDeleteArgument => SelectedArgument is not null;
    [RelayCommand(CanExecute = nameof(CanDeleteArgument))]
    private void DeleteArgument() => SelectedArgument = DeleteRow(Arguments, SelectedArgument);

    public bool CanMoveArgumentUp => CanUp(Arguments, SelectedArgument);
    public bool CanMoveArgumentDown => CanDown(Arguments, SelectedArgument);
    [RelayCommand(CanExecute = nameof(CanMoveArgumentUp))]
    private void MoveArgumentUp() => SelectedArgument = MoveRow(Arguments, SelectedArgument, -1);
    [RelayCommand(CanExecute = nameof(CanMoveArgumentDown))]
    private void MoveArgumentDown() => SelectedArgument = MoveRow(Arguments, SelectedArgument, +1);

    private void ReplaceArguments(IReadOnlyList<ProcedureParameter> source)
    {
        Arguments.Clear();
        foreach (var p in source) Arguments.Add(ProcedureParamRowViewModel.From(p, this, isOutput: false));
    }

    // ─── Result (single record — the return value) ────────────────────────

    /// <summary>The function's return value — exactly one row (no Add/Remove/Move).
    /// Bound as a one-item collection so it reuses the shared field grid; the Name column
    /// is hidden in the view (a function return is unnamed).</summary>
    public ObservableCollection<ProcedureParamRowViewModel> ResultRows { get; }

    public ProcedureParamRowViewModel? ResultRow => ResultRows.Count > 0 ? ResultRows[0] : null;

    /// <summary>Whether the function is declared <c>DETERMINISTIC</c> (Result sub-tab).</summary>
    [ObservableProperty]
    private bool _deterministic;

    partial void OnDeterministicChanged(bool value) => MarkDirty();

    // Replaces the single Result row with one carrying the given return type. From(...)
    // runs LoadType, so the structured type editors reflect the type immediately.
    private void SetResultType(string? returnType)
    {
        ResultRows.Clear();
        ResultRows.Add(ProcedureParamRowViewModel.From(
            new ProcedureParameter { TypeText = returnType ?? string.Empty }, this, isOutput: true));
        OnPropertyChanged(nameof(ResultRow));
    }

    // ─── Easy-mode model (Arguments + return + DECLARE section + body) ────

    /// <summary>Reassembles the full CREATE OR ALTER FUNCTION text from the Easy-mode
    /// model. Defensive return-type fallback keeps it from throwing while a new function
    /// is still being filled in (a real Compile validates the return type first).</summary>
    internal override string BuildFullSource()
    {
        var ret = ResultRow?.TypeText;
        if (string.IsNullOrWhiteSpace(ret)) ret = "INTEGER";
        return DdlGenerator.BuildCreateOrAlterFunction(
            string.IsNullOrWhiteSpace(EditableFunctionName) ? FunctionName : EditableFunctionName.Trim(),
            Arguments.Select(a => a.ToParameter()).ToList(),
            ret!,
            Deterministic,
            DdlGenerator.BuildProcedureBody(BuildBodyModel()));
    }

    internal ProcedureBodyModel BuildBodyModel()
    {
        var model = new ProcedureBodyModel { ExecutableBody = ExecutableBody };
        foreach (var v in Variables) model.Variables.Add(v.ToVariable());
        foreach (var c in Cursors) model.Cursors.Add(c.ToCursor());
        foreach (var sp in Subprograms) model.Subprograms.Add(sp.ToSubprogram());
        return model;
    }

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
    // Routes the toolbar's single Add/Remove/Move set to the active Easy sub-tab's
    // collection (0=Arguments 2=Variables 3=Cursors 4=Subprograms). The Result sub-tab
    // (1) is a single record — IsEasyCollectionEditable is false there so the owner hides
    // the collection section entirely.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEasyCollectionEditable))]
    [NotifyCanExecuteChangedFor(nameof(AddCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemDownCommand))]
    private int _activeEasyCollectionIndex;

    /// <summary>False on the single-record Result sub-tab — the owner returns no
    /// collection there, hiding the Add/Remove/Move toolbar section.</summary>
    public bool IsEasyCollectionEditable => ActiveEasyCollectionIndex != ResultEasyIndex;

    private (IRelayCommand Add, IRelayCommand Remove, IRelayCommand Up, IRelayCommand Down) EasyCommands()
        => ActiveEasyCollectionIndex switch
        {
            0 => (AddArgumentCommand, DeleteArgumentCommand, MoveArgumentUpCommand, MoveArgumentDownCommand),
            2 => (AddVariableCommand, DeleteVariableCommand, MoveVariableUpCommand, MoveVariableDownCommand),
            3 => (AddCursorCommand, DeleteCursorCommand, MoveCursorUpCommand, MoveCursorDownCommand),
            4 => (AddSubprogramCommand, DeleteSubprogramCommand, MoveSubprogramUpCommand, MoveSubprogramDownCommand),
            // Result (1): no collection ops — the toolbar section is hidden, so these are
            // never invoked; route to Arguments as a harmless placeholder.
            _ => (AddArgumentCommand, DeleteArgumentCommand, MoveArgumentUpCommand, MoveArgumentDownCommand),
        };

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

    // ─── Execute Function (Data lane, parameterized) ──────────────────────

    /// <summary>Set by the view — opens the parameter dialog for the given arguments and
    /// returns ordered values (null entry = SQL NULL), or null when the user cancels.</summary>
    public Func<IReadOnlyList<ProcedureParamRowViewModel>, Task<IReadOnlyList<object?>?>>? ExecuteParamsRequested { get; set; }

    /// <summary>Set by the owner — runs the built statement on the Data lane with bound
    /// parameters and returns the outcome.</summary>
    public Func<string, IReadOnlyList<QueryParameter>, Task<ProcedureExecOutcome>>? RunExecuteRequested { get; set; }

    /// <summary>Set by the owner — opens a debugger tab for this function, launched as the debug ROOT
    /// (D-function). Null when debugging is unavailable.</summary>
    public Action? DebugRequested { get; set; }

    /// <summary>Debug is available for an EXISTING (compiled) function only — a New function has no catalog
    /// object to launch. Fixed at construction (<see cref="DebugRequested"/> + <see cref="IsNew"/> do not
    /// change), so this needs no change notification (mirrors <c>ProcedureDetailTabViewModel.CanDebugProcedure</c>).</summary>
    public bool CanDebugFunction => DebugRequested is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanDebugFunction))]
    private void DebugFunction() => DebugRequested?.Invoke();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecResult))]
    [NotifyPropertyChangedFor(nameof(ShowExecError))]
    [NotifyPropertyChangedFor(nameof(CanExportExecResult))]
    private QueryResult? _execResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecResult))]
    [NotifyPropertyChangedFor(nameof(ShowExecError))]
    [NotifyPropertyChangedFor(nameof(CanExportExecResult))]
    private string _execError = string.Empty;

    [ObservableProperty]
    private string _execResultVersionTag = string.Empty;

    public bool HasExecResult => ExecResult is { HasResultSet: true } && string.IsNullOrEmpty(ExecError);
    public bool ShowExecError => !string.IsNullOrEmpty(ExecError);

    public bool CanExportExecResult => HasExecResult;

    /// <summary>Export source for the Execute-Function result grid — the SAME materialized model as
    /// the SQL Editor results (CurrentView = filtered/displayed; AllRows = the full result). No
    /// re-fetch (re-running the function could repeat side effects).</summary>
    public IExportDataSource? BuildExecResultExportSource()
    {
        if (ExecResult is not { HasResultSet: true } result) return null;
        return new QueryResultExportSource(
            result.Columns, _execRows, _execAllRows, isPartial: false, streamAll: null, FunctionName + "_result");
    }

    /// <summary>This function tab's OWN Performance context (its captured run + panel). Set by
    /// the owning MainWindowViewModel factory. Never shared with the SQL Editor or another tab.</summary>
    public HostPerformanceContext? PerformanceContext { get; internal set; }

    /// <summary>The Performance sub-tab panel for THIS function — analyzes only this tab's last
    /// Execute. Null until the factory wires the context.</summary>
    public PerformancePanelViewModel? Performance => PerformanceContext?.Panel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExecSummaryFallbackText))]
    private string _execInfo = string.Empty;

    // The one-line summary shown as the exec-info Expander's header (collapsed state).
    [ObservableProperty]
    private string _execInfoCompact = string.Empty;

    [ObservableProperty]
    private bool _execInfoIsError;

    /// <summary>Per-table CHANGE breakdown for the exec-info Expander's expanded body, built from
    /// the MON$ delta. Reads are NOT here — they live in the Performance tab.</summary>
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

    public bool HasExecInfo => !string.IsNullOrEmpty(ExecInfo);

    partial void OnExecInfoChanged(string value) => OnPropertyChanged(nameof(HasExecInfo));

    // Client-side paging over the materialized result set — a function scalar is one row,
    // but the same shape as the procedure result grid keeps the view identical.
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

    public bool CanExecuteFunction => RunExecuteRequested is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanExecuteFunction))]
    private async Task ExecuteFunction()
    {
        if (RunExecuteRequested is null) return;

        IReadOnlyList<object?> values = Array.Empty<object?>();
        if (Arguments.Count > 0)
        {
            if (ExecuteParamsRequested is null) return;
            var collected = await ExecuteParamsRequested(Arguments.ToList()).ConfigureAwait(true);
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
            if (HasExecResult) ActiveSubTabIndex = ExecuteResultSubTabIndex;
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

    // A function is usually read-only: a scalar result shows "1 row in T ms" (+ rows read);
    // a set-returning function shows its rows + reads. Same metrics infrastructure as the
    // SQL Editor and Procedure Detail.
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
    // Expander header. A scalar/set result keeps "N rows in T ms" (+ read); a non-result call
    // shows the compact work line. Same shape as ProcedureDetailTabViewModel.
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

    /// <summary>Builds the function call <c>SELECT fn(@p0, …) FROM RDB$DATABASE</c> +
    /// bound parameters from ordered argument values. Internal + pure for tests.</summary>
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
        var quoted = QuoteName(FunctionName);
        var sql = "SELECT " + quoted + "(" + string.Join(", ", names) + ") FROM RDB$DATABASE";
        return (sql, parameters);
    }

    private static string QuoteName(string name)
        => "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";

    // ─── Object-specific hooks (SourceObjectDetailTabViewModel) ───────────

    protected override string ObjectDisplayName =>
        string.IsNullOrWhiteSpace(EditableFunctionName) ? FunctionName : EditableFunctionName.Trim();

    protected override string ParseFailedNotice => UiStrings.FunctionParseFailedNotice;
    protected override string CompileFailedFormat => UiStrings.FunctionCompileFailedFormat;
    protected override string UnsavedNewFormat => UiStrings.UnsavedNewFunctionFormat;
    protected override string UnsavedModifiedFormat => UiStrings.UnsavedModifiedFunctionFormat;

    protected override string CommentSql(string? comment)
        => DdlGenerator.BuildCommentFunction(FunctionName, comment);

    protected override bool TryApplySource(string source)
    {
        var sig = FunctionSignatureParser.Parse(source);
        if (!sig.Success) return false;
        if (!string.IsNullOrWhiteSpace(sig.Name)) EditableFunctionName = sig.Name!;
        ReplaceArguments(sig.Arguments);
        SetResultType(sig.ReturnType);
        Deterministic = sig.Deterministic;
        SyncEasyModelFromBody(sig.Body);
        return true;
    }

    protected override string? TryParseName(string? sql) => TryParseFunctionName(sql);

    internal static string? TryParseFunctionName(string? sql)
    {
        var sig = FunctionSignatureParser.Parse(sql);
        return sig.Success ? sig.Name : null;
    }

    // A function must declare a return type — block Compile with a clear message in
    // Easy mode rather than letting the generator/server reject it.
    protected override string? ValidateBeforeCompile()
    {
        if (EasyMode && string.IsNullOrWhiteSpace(ResultRow?.TypeText))
            return UiStrings.FunctionResultRequiredNotice;
        return null;
    }

    protected override Task<string> ReadDefinitionAsync(CancellationToken cancellationToken)
        => DdlReader!.FetchFunctionSourceAsync(
            new MetadataObject(FunctionName, MetadataObjectKind.Function), cancellationToken);

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        // Reads the source AND arms the change-safety gate with it — one act (see LoadDefinitionAsync).
        await SafeLoadAsync(() => LoadDefinitionAsync(cancellationToken));

        await SafeLoadAsync(async () =>
        {
            var body = await DdlReader!.FetchFunctionBodyAsync(
                new MetadataObject(FunctionName, MetadataObjectKind.Function), cancellationToken).ConfigureAwait(true);
            SyncEasyModelFromBody(body);
        });

        await SafeLoadAsync(async () =>
        {
            var sig = await Reader!.GetFunctionSignatureAsync(FunctionName, cancellationToken).ConfigureAwait(true);
            Arguments.Clear();
            foreach (var a in sig.Arguments) Arguments.Add(ProcedureParamRowViewModel.From(a, this, isOutput: false));
            SetResultType(sig.ReturnType);
            Deterministic = sig.Deterministic;
        });

        await SafeLoadAsync(async () =>
        {
            var (dependsOn, dependedOnBy) = await Reader!.GetFunctionDependenciesAsync(FunctionName, cancellationToken).ConfigureAwait(true);
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
                new MetadataObject(FunctionName, MetadataObjectKind.Function), cancellationToken).ConfigureAwait(true);
        });

        await SafeLoadAsync(async () =>
        {
            Description = await Reader!.GetFunctionDescriptionAsync(FunctionName, cancellationToken).ConfigureAwait(true);
            DescriptionLoaded = true;
        });
    }
}
