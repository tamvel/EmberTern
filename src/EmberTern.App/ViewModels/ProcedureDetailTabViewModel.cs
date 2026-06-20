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
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>Result of an Execute Procedure run — a result set or an error message.</summary>
public sealed record ProcedureExecOutcome(QueryResult? Result, string? Error);

/// <summary>
/// Detail surface for a Firebird stored PROCEDURE (V1.1). Tabs: Editor (Source ⇄
/// Easy modes) · Description · Dependencies · DDL · Result.
/// <list type="bullet">
/// <item>Source mode = the full editable CREATE OR ALTER PROCEDURE text.</item>
/// <item>Easy mode = metadata panels (Input/Output params editable; Variables/
/// Cursors/Subprograms read-only) ABOVE a body-only editor.</item>
/// </list>
/// A canonical model {name, inputs, outputs, body} backs both: switching to Easy
/// parses the source (bounded header parser; on failure keeps the model + notes it);
/// switching to Source regenerates the text (deterministic). Compile reassembles +
/// runs in the working (metadata) tx. Execute runs the procedure on the Data lane
/// with bound parameters.
/// </summary>
public partial class ProcedureDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, IFieldRowOwner
{
    private const int InputParamType = 0;
    private const int OutputParamType = 1;

    // Top-tab indices — must match the TabItem order in the view.
    public const int EditorSubTabIndex = 0;
    public const int ResultSubTabIndex = 4;

    public const string NewProcedureTemplate =
        "CREATE OR ALTER PROCEDURE NEW_PROCEDURE\nRETURNS (\n    RESULT INTEGER\n)\nAS\nBEGIN\n    RESULT = 0;\n    SUSPEND;\nEND";

    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    public ProcedureDetailTabViewModel(string procedureName)
        : this(procedureName, null, null, null)
    {
    }

    public ProcedureDetailTabViewModel(
        string procedureName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        ProcedureName = procedureName;
        EditableProcedureName = procedureName;
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        InputParams = new ObservableCollection<ProcedureParamRowViewModel>();
        OutputParams = new ObservableCollection<ProcedureParamRowViewModel>();
        Variables = new ObservableCollection<ProcedureVariableRowViewModel>();
        Cursors = new ObservableCollection<ProcedureCursorRowViewModel>();
        Subprograms = new ObservableCollection<ProcedureSubprogramRowViewModel>();
        AvailableDomains = new ObservableCollection<DomainSpec>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();

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
        Variables.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(VariablesTabHeader));
            DeleteVariableCommand.NotifyCanExecuteChanged();
            MoveVariableUpCommand.NotifyCanExecuteChanged();
            MoveVariableDownCommand.NotifyCanExecuteChanged();
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
        // toggle). See the ViewDetail dirty-tracking note for why an explicit flag.
        TrackDirty(InputParams);
        TrackDirty(OutputParams);
        TrackDirty(Variables);
        TrackDirty(Cursors);
        TrackDirty(Subprograms);
        // Release the ctor-time suppression now that all fields are assigned.
        _suppressDirty = false;
    }

    // ─── Dirty tracking (drives IUnsavedWorkSource + future auto-draft) ─────
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

    partial void OnSourceTextChanged(string value) => MarkDirty();
    partial void OnExecutableBodyChanged(string value) => MarkDirty();

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

    // Unsaved-work for the WorkGuard. Untouched tab (just opened / fresh New
    // Procedure before any edit) → null. New Procedure clears dirty after seeding
    // the template so an untouched new tab doesn't prompt.
    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        var name = string.IsNullOrWhiteSpace(EditableProcedureName) ? ProcedureName : EditableProcedureName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedNewProcedureFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedProcedureFormat, name));
    }

    /// <summary>Domains available on the active connection — populated best-effort by
    /// the owner so the Variables grid's Domain combo has something to offer. Shared
    /// with each variable row (mirrors the New Table grid).</summary>
    public ObservableCollection<DomainSpec> AvailableDomains { get; }

    /// <summary>Basic SQL types for the Variables grid (reused list — no second
    /// type system).</summary>
    public IReadOnlyList<string> BasicTypes { get; } = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };

    /// <summary>Owner injects the live domain list after the active-connection
    /// metadata load. Cleared via the SearchableComboBox ✕ (no "(none)" sentinel).</summary>
    public void SetAvailableDomains(IEnumerable<DomainSpec> domains)
    {
        AvailableDomains.Clear();
        foreach (var d in domains) AvailableDomains.Add(d);
    }

    public string ProcedureName { get; }

    /// <summary>True for a not-yet-created procedure (New Procedure flow). Can be
    /// authored in Easy mode too — the New Procedure flow starts there with an editable
    /// name field that supplies the object name (Source mode keeps it in the header).</summary>
    public bool IsNew { get; init; }

    public bool CanUseEasyMode => true;

    /// <summary>Object name used by Easy mode (the CREATE OR ALTER PROCEDURE header
    /// isn't shown there). Seeded from <see cref="ProcedureName"/> / the parsed source;
    /// editable in the New Procedure flow, read-only display for an existing procedure.</summary>
    [ObservableProperty]
    private string _editableProcedureName = string.Empty;

    // ─── Mode ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceMode))]
    private bool _easyMode;

    public bool IsSourceMode => !EasyMode;

    partial void OnEasyModeChanged(bool value)
    {
        // A pure Source⇄Easy toggle is not an edit — suppress the dirty flips that
        // re-populating the params/model/source would otherwise cause.
        var prev = _suppressDirty;
        _suppressDirty = true;
        try
        {
            if (value)
            {
                // Nothing loaded yet (e.g. the mode preference is applied at tab
                // creation, before lazy load) — don't parse an empty source / show a
                // spurious notice; LoadAsync will populate the model.
                if (string.IsNullOrWhiteSpace(SourceText)) { ErrorMessage = null; return; }
                // Source → Easy: parse the current source into the structured model
                // (params + variables + cursors + subprograms + executable body) so source
                // edits carry over. On failure keep the last-good model + note it.
                var sig = ProcedureSignatureParser.Parse(SourceText);
                if (sig.Success)
                {
                    if (!string.IsNullOrWhiteSpace(sig.Name)) EditableProcedureName = sig.Name!;
                    ReplaceParams(InputParams, sig.Inputs, isOutput: false);
                    ReplaceParams(OutputParams, sig.Outputs, isOutput: true);
                    SyncEasyModelFromBody(sig.Body);
                    ErrorMessage = null;
                }
                else
                {
                    ErrorMessage = UiStrings.ProcedureParseFailedNotice;
                }
            }
            else
            {
                // Easy → Source: regenerate the full text from the structured model.
                SourceText = BuildFullSource();
            }
        }
        finally
        {
            _suppressDirty = prev;
        }
    }

    /// <summary>Reassembles the full CREATE OR ALTER PROCEDURE text from the Easy-mode
    /// structured model (params + regenerated DECLARE section + executable body).</summary>
    internal string BuildFullSource()
        => DdlGenerator.BuildCreateOrAlterProcedure(
            string.IsNullOrWhiteSpace(EditableProcedureName) ? ProcedureName : EditableProcedureName.Trim(),
            InputParams.Select(p => p.ToParameter()).ToList(),
            OutputParams.Select(p => p.ToParameter()).ToList(),
            DdlGenerator.BuildProcedureBody(BuildBodyModel()));

    /// <summary>Collects the editable Variables / Cursors / Subprograms / executable
    /// body into a Core <see cref="ProcedureBodyModel"/>.</summary>
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

    // ─── Editor text ──────────────────────────────────────────────────────

    /// <summary>Full editable CREATE OR ALTER PROCEDURE source — Source mode.</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>The executable BEGIN…END block only — the Easy-mode body editor.
    /// The DECLARE section comes from the structured Variables/Cursors/Subprograms
    /// model, so this editor never holds declarations.</summary>
    [ObservableProperty]
    private string _executableBody = string.Empty;

    /// <summary>Read-only reconstructed DDL — the DDL tab.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

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

    // ─── Generic row helpers (shared by params + locals) ──────────────────

    private static T? DeleteRow<T>(ObservableCollection<T> coll, T? sel) where T : class
    {
        if (sel is null) return null;
        var idx = coll.IndexOf(sel);
        if (idx < 0) return sel;
        coll.RemoveAt(idx);
        return coll.Count > 0 ? coll[Math.Min(idx, coll.Count - 1)] : null;
    }

    private static bool CanUp<T>(ObservableCollection<T> coll, T? sel) where T : class
        => sel is not null && coll.IndexOf(sel) > 0;

    private static bool CanDown<T>(ObservableCollection<T> coll, T? sel) where T : class
        => sel is not null && coll.IndexOf(sel) is var ix && ix >= 0 && ix < coll.Count - 1;

    // RemoveAt + Insert (not Move) — Avalonia DataGrid doesn't reliably re-render a
    // NotifyCollectionChangedAction.Move (same gotcha as the New Table grid).
    private static T? MoveRow<T>(ObservableCollection<T> coll, T? sel, int delta) where T : class
    {
        if (sel is null) return sel;
        var idx = coll.IndexOf(sel);
        var t = idx + delta;
        if (idx < 0 || t < 0 || t >= coll.Count) return sel;
        coll.RemoveAt(idx);
        coll.Insert(t, sel);
        return sel;
    }

    // ─── Editable locals — Variables (grid) ───────────────────────────────

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
    private void DeleteVariable() => SelectedVariable = DeleteRow(Variables, SelectedVariable);

    public bool CanMoveVariableUp => CanUp(Variables, SelectedVariable);
    public bool CanMoveVariableDown => CanDown(Variables, SelectedVariable);
    [RelayCommand(CanExecute = nameof(CanMoveVariableUp))]
    private void MoveVariableUp() => SelectedVariable = MoveRow(Variables, SelectedVariable, -1);
    [RelayCommand(CanExecute = nameof(CanMoveVariableDown))]
    private void MoveVariableDown() => SelectedVariable = MoveRow(Variables, SelectedVariable, +1);

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
    // TabControl's SelectedIndex. This is the contract future Trigger/Function/Package
    // editors reuse — they expose the same four commands routing to their own active
    // collection, and the main toolbar gains no new pattern.

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

    // ─── Description (editable COMMENT ON PROCEDURE) ──────────────────────

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
        var sql = DdlGenerator.BuildCommentProcedure(ProcedureName, comment);
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

    /// <summary>Set by the view — returns the ACTIVE editor's selection (source or
    /// body, by mode), or null when nothing is selected.</summary>
    public Func<string?>? SelectedTextProvider { get; set; }

    /// <summary>Set by the view — replaces the active editor's selection (or whole
    /// document when no selection) with the given text.</summary>
    public Action<string>? ReplaceSelectedOrAllText { get; set; }

    /// <summary>Raised by the Comment Body / Uncomment Body commands; the view
    /// wraps/unwraps the outer BEGIN…END body in the active editor.</summary>
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

    // In Easy mode the active editor is the executable-body editor (declarations
    // come from the structured grids, never typed into the editor); in Source mode
    // it's the full-source editor.
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

    public event Action<string?>? ProcedureCreated;

    public bool CanCompile => _ddlExecutor is not null;

    /// <summary>Reassembles the active mode's content into a CREATE OR ALTER
    /// PROCEDURE and returns it (Easy → from the structured model; Source → the
    /// raw text). Internal so tests can assert the reassembly without a DB.</summary>
    internal string BuildCompileSql() => EasyMode ? BuildFullSource() : SourceText;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        if (_ddlExecutor is null) return;
        var sql = BuildCompileSql();
        if (string.IsNullOrWhiteSpace(sql)) return;

        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            ProcedureCreated?.Invoke(TryParseProcedureName(sql));
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    internal static string? TryParseProcedureName(string? sql)
    {
        var sig = ProcedureSignatureParser.Parse(sql);
        return sig.Success ? sig.Name : null;
    }

    // ─── Execute Procedure (Data lane, parameterized) ────────────────────

    /// <summary>Set by the view — opens the parameter dialog for the given input
    /// params and returns ordered values (null entry = SQL NULL), or null when the
    /// user cancels.</summary>
    public Func<IReadOnlyList<ProcedureParamRowViewModel>, Task<IReadOnlyList<object?>?>>? ExecuteParamsRequested { get; set; }

    /// <summary>Set by the owner — runs the built statement on the Data lane with
    /// bound parameters and returns the outcome.</summary>
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

    // Persistent execution info (status / time / rows / affected / error) shown in
    // the bottom panel — feedback even for procedures that return no result set.
    [ObservableProperty]
    private string _execInfo = string.Empty;

    [ObservableProperty]
    private bool _execInfoIsError;

    public bool HasExecInfo => !string.IsNullOrEmpty(ExecInfo);

    partial void OnExecInfoChanged(string value) => OnPropertyChanged(nameof(HasExecInfo));

    // ── Result grid: client-side paging over the materialized result set
    // (≤5000 rows, executor-capped). Mirrors the SQL editor's paging shape, but a
    // procedure may have side effects so we NEVER re-execute per page — slice the
    // already-fetched rows in memory. ──
    public const int ExecResultPageSize = 200;
    private List<object?[]> _execRows = new();
    private int _execPage = 1;

    public IReadOnlyList<object?[]> PagedExecRows { get; private set; } = Array.Empty<object?[]>();
    private int TotalExecPages => _execRows.Count == 0 ? 1 : (_execRows.Count + ExecResultPageSize - 1) / ExecResultPageSize;
    public int ExecPage => _execPage;
    public bool HasExecPreviousPage => _execPage > 1;
    public bool HasExecNextPage => _execPage < TotalExecPages;
    public string ExecPaginationHint => HasExecResult
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.ResultsPaginationHintFormat, _execPage, TotalExecPages, _execRows.Count)
        : string.Empty;

    partial void OnExecResultChanged(QueryResult? value)
    {
        _execRows = value?.Rows is { } rows ? new List<object?[]>(rows) : new List<object?[]>();
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

        OnPropertyChanged(nameof(PagedExecRows));
        OnPropertyChanged(nameof(ExecPage));
        OnPropertyChanged(nameof(HasExecPreviousPage));
        OnPropertyChanged(nameof(HasExecNextPage));
        OnPropertyChanged(nameof(ExecPaginationHint));
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
            ExecInfoIsError = true;
        }
        else
        {
            ExecResult = outcome.Result;
            ExecError = string.Empty;
            ExecInfo = BuildExecInfo(outcome.Result);
            ExecInfoIsError = false;
            // Only jump to the Result tab when there are rows — a no-result-set
            // procedure (EXECUTE PROCEDURE) gives feedback via the bottom info panel
            // instead of an empty grid.
            if (HasExecResult) ActiveSubTabIndex = ResultSubTabIndex;
        }
    }

    private static string BuildExecInfo(QueryResult? r)
    {
        if (r is null) return UiStrings.ProcedureExecCompleted;
        var ms = (long)r.Elapsed.TotalMilliseconds;
        if (r.HasResultSet)
            return string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoRowsFormat, r.Rows.Count, ms);
        if (r.RecordsAffected is { } n && n >= 0)
            return string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoAffectedFormat, n, ms);
        return string.Format(CultureInfo.CurrentCulture, UiStrings.ProcedureExecInfoCompletedFormat, ms);
    }

    /// <summary>Builds the EXECUTE statement + bound parameters from ordered input
    /// values. Selectable (SELECT * FROM) when the procedure has outputs and its
    /// body contains a SUSPEND; otherwise EXECUTE PROCEDURE. Internal + pure for tests.</summary>
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
        // Programmatic population — not user edits. Reset to a clean state in finally.
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                SourceText = await _ddlReader.FetchProcedureSourceAsync(
                    new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                var body = await _ddlReader.FetchProcedureBodyAsync(
                    new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
                // Split the body into the editable structured model (Variables /
                // Cursors / Subprograms + executable body).
                SyncEasyModelFromBody(body);
            });

            await SafeLoadAsync(async () =>
            {
                var inputs = await _reader.GetProcedureParametersAsync(ProcedureName, InputParamType, cancellationToken).ConfigureAwait(true);
                InputParams.Clear();
                foreach (var p in inputs) InputParams.Add(ProcedureParamRowViewModel.From(p, this, isOutput: false));
            });

            await SafeLoadAsync(async () =>
            {
                var outputs = await _reader.GetProcedureParametersAsync(ProcedureName, OutputParamType, cancellationToken).ConfigureAwait(true);
                OutputParams.Clear();
                foreach (var p in outputs) OutputParams.Add(ProcedureParamRowViewModel.From(p, this, isOutput: true));
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetProcedureDependenciesAsync(ProcedureName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
            });

            await SafeLoadAsync(async () =>
            {
                DdlText = await _ddlReader.FetchDdlAsync(
                    new MetadataObject(ProcedureName, MetadataObjectKind.Procedure), cancellationToken).ConfigureAwait(true);
            });

            await SafeLoadAsync(async () =>
            {
                Description = await _reader.GetProcedureDescriptionAsync(ProcedureName, cancellationToken).ConfigureAwait(true);
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
