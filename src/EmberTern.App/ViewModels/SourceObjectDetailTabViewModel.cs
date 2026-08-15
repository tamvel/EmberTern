using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Localization;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using EmberTern.Core.Sql;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Shared skeleton for the PSQL-routine source editors — Procedure, Trigger, and
/// Function. They are siblings (not a deep hierarchy): this base carries the large,
/// genuinely-identical surface that each was otherwise duplicating —
/// <list type="bullet">
///   <item>Source ⇄ Easy mode toggle (skeleton; parse/regenerate are object-specific hooks)</item>
///   <item>dirty tracking + WorkGuard (<see cref="IUnsavedWorkSource"/>)</item>
///   <item>Format / Comment / Uncomment on the active editor</item>
///   <item>the editable Variables grid (+ generic row helpers shared by subclass collections)</item>
///   <item>Dependencies trees, editable Description (COMMENT ON …)</item>
///   <item>Compile + Revert + the load/refresh lifecycle</item>
///   <item><see cref="IFieldRowOwner"/> (domain / type / table-column pickers for the field rows)</item>
/// </list>
/// Each object editor supplies only its essence through the abstract hooks
/// (<see cref="BuildFullSource"/>, <see cref="TryApplySource"/>, <see cref="LoadCoreAsync"/>,
/// <see cref="CommentSql"/>, <see cref="TryParseName"/>, the message strings) plus its own
/// param/header collections. View Detail is deliberately NOT on this base — a view has no
/// PSQL body / variables / params, so it is a different family.
/// </summary>
public abstract partial class SourceObjectDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor, IFieldRowOwner, IDependencyNavigator
{
    protected readonly FirebirdTableDetailReader? Reader;
    protected readonly FirebirdDdlReader? DdlReader;
    protected readonly FirebirdDdlExecutor? DdlExecutor;
    private Task? _loadTask;

    // ─── Change safety ────────────────────────────────────────────────────
    //
    // This editor compiles by REPLACING the whole routine (CREATE OR ALTER … AS <entire body>), so its
    // buffer can only be written safely while the database still holds the definition the buffer descends
    // from. The gate below is what makes that a checked fact instead of an assumption; see ObjectChangeGate.
    private readonly ObjectChangeGate _changeGate = new();

    /// <summary>The change-safety gate guarding this tab's compile. Exposed for tests.</summary>
    internal ObjectChangeGate ChangeGate => _changeGate;

    /// <summary>
    /// Owner-supplied "is this name already taken?" probe, used ONLY by the New-object flow — where the
    /// generated statement is <c>CREATE OR ALTER</c> too, so a name collision would overwrite a colleague's
    /// object rather than fail. Wired at the single construction point for each kind (which is where the
    /// object kind is already known, so this stays a plain name lookup and the editor never grows a
    /// per-kind switch).
    /// <para>Null means the check cannot run, which the gate reports as unverifiable — deliberately NOT as
    /// "the name is free".</para>
    /// </summary>
    internal Func<string, CancellationToken, Task<bool>>? ObjectExistsProbe { get; set; }

    protected SourceObjectDetailTabViewModel(
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        Reader = reader;
        DdlReader = ddlReader;
        DdlExecutor = ddlExecutor;

        AvailableDomains = new ObservableCollection<DomainSpec>();
        AvailableTables = new ObservableCollection<string>();
        Variables = new ObservableCollection<ProcedureVariableRowViewModel>();
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();

        Variables.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(VariablesTabHeader));
            DeleteVariableCommand.NotifyCanExecuteChanged();
            MoveVariableUpCommand.NotifyCanExecuteChanged();
            MoveVariableDownCommand.NotifyCanExecuteChanged();
        };
        TrackDirty(Variables);
        TrackAmbient(Variables);

        // Subclasses release the ctor-time suppression at the END of their own ctor,
        // once their fields/collections are assigned — do NOT release it here.
    }

    // ─── Ambient-symbol change tracking (Easy-mode diagnostics/completion refresh) ──────────

    /// <summary>Raised whenever the out-of-text declarations that feed <see cref="BuildAmbientSymbols"/>
    /// change — a parameter/variable added, removed, reordered, or <b>renamed</b> in the Easy-mode grids.
    /// The Easy-mode body editor holds only the BODY; its params/variables live in these grids and reach
    /// the semantic model as ambient symbols. Without this signal the model (and therefore diagnostics,
    /// completion, highlighting, Quick Info) would go stale until the user next edited the body text —
    /// e.g. a squiggle under <c>:test</c> would linger after the user added <c>test</c> to the Variables
    /// grid. The view subscribes and asks each ambient-seeded editor to rebuild its model. Debounced on
    /// the consuming side, so a name typed character-by-character coalesces into one rebuild.</summary>
    public event EventHandler? AmbientSymbolsChanged;

    /// <summary>Signals that the ambient-symbol set changed. Not gated by <see cref="_suppressDirty"/>:
    /// a rebuild must also happen on the programmatic load/toggle that first populates the grids, so the
    /// initial model reflects the final grid state regardless of load order; the consumer debounces the
    /// burst.</summary>
    protected void RaiseAmbientSymbolsChanged() => AmbientSymbolsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="AmbientSymbolsChanged"/> on any add/remove/reorder of the collection
    /// AND on a row's <see cref="ProcedureFieldRowBase.Name"/> edit (the only row property that affects
    /// symbol resolution — type/domain/size/scale/default do not). Subclasses call this for their own
    /// ambient-feeding grids (Input/Output params, function arguments); the base tracks Variables.</summary>
    protected void TrackAmbient(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, e) =>
        {
            RaiseAmbientSymbolsChanged();
            if (e.NewItems is not null)
            {
                foreach (INotifyPropertyChanged row in e.NewItems)
                {
                    row.PropertyChanged += OnAmbientRowPropertyChanged;
                }
            }
        };
    }

    private void OnAmbientRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcedureFieldRowBase.Name)) RaiseAmbientSymbolsChanged();
    }

    // ─── Dirty tracking (drives IUnsavedWorkSource + Revert) ──────────────
    private bool _isDirty;

    /// <summary>True while a structured/Easy-mode load or a pure Source⇄Easy toggle is
    /// in flight — suppresses the dirty flips those programmatic mutations cause. Left
    /// true through the base ctor; each subclass ctor clears it last.</summary>
    protected bool _suppressDirty = true;

    public bool IsDirty => _isDirty;
    internal void ClearDirty() => SetDirty(false);
    protected void MarkDirty() { if (!_suppressDirty) SetDirty(true); }

    // Centralized so a dirty transition keeps the Revert button's enabled state (and any
    // IsDirty binding) in sync — Revert is only available when there are edits to undo.
    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        OnPropertyChanged(nameof(IsDirty));
        RevertChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Marks the editor dirty on any add/remove/reorder of the collection AND on
    /// any row-internal edit (subscribes each added row's PropertyChanged). Suppressed
    /// during programmatic load / mode toggle.</summary>
    protected void TrackDirty(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, e) =>
        {
            MarkDirty();
            if (e.NewItems is not null)
            {
                foreach (INotifyPropertyChanged row in e.NewItems)
                {
                    row.PropertyChanged += (_, _) => MarkDirty();
                }
            }
        };
    }

    // ─── IUnsavedWorkSource ───────────────────────────────────────────────
    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UnsavedNewFormat, ObjectDisplayName))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UnsavedModifiedFormat, ObjectDisplayName));
    }

    // ─── Object identity / mode ───────────────────────────────────────────

    /// <summary>True for a not-yet-created object (New … flow). Authored in Easy mode
    /// with an editable name field; subclasses seed an initial template + ClearDirty.</summary>
    public bool IsNew { get; init; }

    public virtual bool CanUseEasyMode => true;

    /// <summary>Diagnostics panel (Stage 7 / S4) for this editor's own Diagnostics sub-tab — the same view
    /// and VM the SQL Editor uses, hosted per object exactly as <c>Performance</c> is (one context per host,
    /// no shared global state). Fed by the View's <c>DiagnosticsPanelHost</c> from the active SQL document's
    /// cached diagnostics; this VM computes nothing.</summary>
    public DiagnosticsPanelViewModel DiagnosticsPanel { get; } = new();

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
                // Nothing loaded yet (e.g. the mode preference is applied at tab
                // creation, before lazy load) — don't parse an empty source / show a
                // spurious notice; LoadAsync will populate the model.
                if (string.IsNullOrWhiteSpace(SourceText)) { ErrorMessage = null; return; }
                // Source → Easy: parse the current source into the structured model so
                // source edits carry over. On failure keep the last-good model + note it.
                ErrorMessage = TryApplySource(SourceText) ? null : ParseFailedNotice;
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

    // ─── Editor text ──────────────────────────────────────────────────────

    /// <summary>Full editable CREATE OR ALTER … source — Source mode.</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    partial void OnSourceTextChanged(string value) => MarkDirty();

    /// <summary>The executable BEGIN…END block only — the Easy-mode body editor. The
    /// DECLARE section comes from the structured Variables/… model, never typed here.</summary>
    [ObservableProperty]
    private string _executableBody = string.Empty;

    partial void OnExecutableBodyChanged(string value) => MarkDirty();

    /// <summary>Read-only reconstructed DDL — the DDL tab.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    // In Easy mode the active editor is the executable-body editor; in Source mode it's
    // the full-source editor.
    private string ActiveEditorText
    {
        get => EasyMode ? ExecutableBody : SourceText;
        set { if (EasyMode) ExecutableBody = value; else SourceText = value; }
    }

    // ─── IFieldRowOwner (Variables grid Type / Domain combos) ─────────────

    /// <summary>Domains available on the active connection — populated best-effort by the
    /// owner so the field grids' Domain picker has something to offer.</summary>
    public ObservableCollection<DomainSpec> AvailableDomains { get; }

    /// <summary>Basic SQL types for the type combos (reused list — no second type system).</summary>
    public IReadOnlyList<string> BasicTypes { get; } = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB",
    };

    /// <summary>Tables for the merged Domain/Column picker's "Table column" tab
    /// (TYPE OF COLUMN). Populated best-effort by the owner; columns load lazily.</summary>
    public ObservableCollection<string> AvailableTables { get; }

    /// <summary>Lazy column loader for the picker's "Table column" tab — wired by the
    /// owner to its catalog reader. Null in unit tests → that tab stays empty.</summary>
    public IColumnsLoader? ColumnsLoader { get; set; }

    public void SetAvailableDomains(IEnumerable<DomainSpec> domains)
    {
        AvailableDomains.Clear();
        // No "(none)" sentinel — the SearchableComboBox clears via its ✕ button.
        foreach (var d in domains) AvailableDomains.Add(d);
    }

    public void SetAvailableTables(IEnumerable<string> tables)
    {
        AvailableTables.Clear();
        foreach (var t in tables) AvailableTables.Add(t);
    }

    // ─── Variables (editable grid — shared by all routine editors) ────────

    public ObservableCollection<ProcedureVariableRowViewModel> Variables { get; }

    /// <summary>
    /// The routine's declarations as semantic symbols, for the Easy-mode BODY editor. That editor's
    /// text is only the body — the DECLAREd variables (and, for a procedure/function, the
    /// parameters) live in the surrounding grids, so a text-only semantic model cannot see them and
    /// Ctrl+Space offered no params/locals. These are seeded into the model's root scope, which
    /// makes them visible to every model client (completion, Quick Info, navigation, highlighting).
    /// <para>Base = the shared Variables grid; routine kinds with parameters override and add them.
    /// Source mode passes nothing (the text already declares everything).</para>
    /// </summary>
    public virtual IReadOnlyList<Symbol> BuildAmbientSymbols()
    {
        var symbols = new List<Symbol>();
        AddVariableSymbols(symbols);
        return symbols;
    }

    /// <summary>Appends the Variables grid as <see cref="VariableSymbol"/>s. Blank/duplicate names
    /// are skipped — a half-typed row must never poison the scope.</summary>
    protected void AddVariableSymbols(List<Symbol> symbols)
    {
        foreach (var v in Variables)
        {
            var name = v.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            symbols.Add(new VariableSymbol(name));
        }
    }

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

    // ─── Generic row helpers (shared by Variables + subclass collections) ──

    protected static T? DeleteRow<T>(ObservableCollection<T> coll, T? sel) where T : class
    {
        if (sel is null) return null;
        var idx = coll.IndexOf(sel);
        if (idx < 0) return sel;
        coll.RemoveAt(idx);
        return coll.Count > 0 ? coll[Math.Min(idx, coll.Count - 1)] : null;
    }

    protected static bool CanUp<T>(ObservableCollection<T> coll, T? sel) where T : class
        => sel is not null && coll.IndexOf(sel) > 0;

    protected static bool CanDown<T>(ObservableCollection<T> coll, T? sel) where T : class
        => sel is not null && coll.IndexOf(sel) is var ix && ix >= 0 && ix < coll.Count - 1;

    // RemoveAt + Insert (not Move) — Avalonia DataGrid doesn't reliably re-render a
    // NotifyCollectionChangedAction.Move (same gotcha as the New Table grid).
    protected static T? MoveRow<T>(ObservableCollection<T> coll, T? sel, int delta) where T : class
    {
        if (sel is null) return sel;
        var idx = coll.IndexOf(sel);
        var t = idx + delta;
        if (idx < 0 || t < 0 || t >= coll.Count) return sel;
        coll.RemoveAt(idx);
        coll.Insert(t, sel);
        return sel;
    }

    // ─── Description (editable COMMENT ON …) ──────────────────────────────

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

    public bool CanEditDescription => DdlExecutor is not null;

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
        if (DdlExecutor is null) return;
        ErrorMessage = null;
        var comment = string.IsNullOrWhiteSpace(EditableDescription) ? null : EditableDescription;
        var sql = CommentSql(comment);
        try
        {
            await DdlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.TableDescriptionSaveFailedFormat, ErrorText.Of(ex));
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

    /// <summary>Set by the view — returns the ACTIVE editor's selection (source or body,
    /// by mode), or null when nothing is selected.</summary>
    public Func<string?>? SelectedTextProvider { get; set; }

    /// <summary>Set by the view — replaces the active editor's selection (or whole
    /// document when no selection) with the given text.</summary>
    public Action<string>? ReplaceSelectedOrAllText { get; set; }

    /// <summary>Raised by the Comment Body / Uncomment Body commands; the view
    /// wraps/unwraps the outer BEGIN…END body in the active editor.</summary>
    public event Action? CommentRequested;
    public event Action? UncommentRequested;

    /// <summary>
    /// The casing style the Format SQL action uses — supplied by the tab factory, which has the app's one
    /// <c>PreferencesService</c> in hand (<c>WorkspaceTabViewModel.Create*Detail</c>).
    /// <para>⚠ A <b>provider</b>, not a captured value: apply-on-change means the preference can change
    /// while this tab is open, and a captured style would silently format with the previous setting.</para>
    /// <para>⚠ Non-nullable with a real default, so a view model constructed without a factory (every unit
    /// test) formats deterministically in the shipped style — "nullable meaning unset" would hand the
    /// default decision to each reader, the shape <c>Preferences</c>' own contract forbids.</para>
    /// </summary>
    public Func<FormatterStyle> CurrentFormatterStyle { get; set; } = () => FormatterStyle.Default;

    [RelayCommand]
    private void FormatSql()
    {
        var selected = SelectedTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : ActiveEditorText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = EmberTern.Core.Sql.SqlFormatter.Format(source, CurrentFormatterStyle());
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace) replace(formatted);
        else if (!hasSelection) ActiveEditorText = formatted;
    }

    [RelayCommand]
    private void CommentBody() => CommentRequested?.Invoke();

    [RelayCommand]
    private void UncommentBody() => UncommentRequested?.Invoke();

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

    /// <summary>
    /// What a successful compile actually produced. ⭐ The two cases go through ONE event because from the
    /// tab's point of view they are the same act: <b>an object now exists under a name this tab is not bound
    /// to</b>, so the owner must refresh the tree, close this tab and open that object. The payload says which
    /// case it was, because only the WORDING differs.
    /// </summary>
    /// <param name="Name">The name the statement created, as parsed from the statement that ran.</param>
    /// <param name="PreviousName">
    /// The name this tab was loaded under, when the compile was a RENAME attempt; <c>null</c> for an ordinary
    /// create. ⚠ Non-null means a second object now exists — the original was NOT removed, because Firebird
    /// has no rename for these kinds (measured on FB 5.0: <c>ALTER PROCEDURE … TO …</c> is <c>-104 Token
    /// unknown</c>). The user must be told all three facts.
    /// </param>
    public sealed record ObjectCompileOutcome(string? Name, string? PreviousName = null)
    {
        /// <summary>The compile created a SECOND object rather than altering the one this tab was showing.</summary>
        public bool IsRename => !string.IsNullOrEmpty(PreviousName);
    }

    /// <summary>Raised after a successful CREATE of an object under a name this tab is not bound to — a new
    /// object, or a rename (see <see cref="ObjectCompileOutcome"/>). The owner refreshes the tree, closes this
    /// tab and opens the object that now exists.</summary>
    public event Action<ObjectCompileOutcome>? ObjectCreated;

    /// <summary>Raised after a successful Compile of an EXISTING object — the owner offers to
    /// recompile its dependents (Part 2). Not raised in the New flow (a new object has no
    /// dependents yet; ObjectCreated fires instead).</summary>
    public event Action? CompiledExistingObject;

    public bool CanCompile => DdlExecutor is not null;

    /// <summary>Reassembles the active mode's content (Easy → from the structured model;
    /// Source → the raw text). Internal so tests can assert reassembly without a DB.</summary>
    internal string BuildCompileSql() => EasyMode ? BuildFullSource() : SourceText;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        // Both pre-condition refusals REPORT (Seam 6b): a compile that never ran must not leave
        // ErrorMessage null, or SaveAsync claims success having written nothing — see the contract
        // on ISavableObjectEditor. Here an empty buffer means the user emptied the source, which is
        // exactly the case where a wrong "success" would let the WorkGuard discard real code.
        if (DdlExecutor is null)
        {
            ErrorMessage = UiStrings.NoConnectionMessage;
            return;
        }

        // Object-specific pre-flight validation (e.g. a trigger needs a table + an
        // event in Easy mode) — a clear message instead of a server error.
        if (ValidateBeforeCompile() is { } validationError)
        {
            ErrorMessage = validationError;
            return;
        }

        var sql = BuildCompileSql();
        if (string.IsNullOrWhiteSpace(sql))
        {
            ErrorMessage = UiStrings.EditorNothingToCompile;
            return;
        }

        // ⭐⭐ IS THIS A RENAME? Asked BEFORE change safety, because the answer decides WHICH safety question is
        // the right one. A compile whose statement names a different object than this tab loaded is not an
        // ALTER of this object at all — Firebird will CREATE a second one. Checking "is the definition I
        // loaded still there?" would then re-read the ORIGINAL, find it unchanged, and answer Safe about an
        // object the statement does not touch (measured 2026-08-07: that is exactly how a rename onto an
        // EXISTING name silently overwrote it — the gate was not bypassed, it was asked about the wrong
        // object).
        var renameTarget = ResolveRenameTarget(sql);
        var safety = renameTarget is null
            ? await CheckChangeSafetyAsync(sql, cancellationToken).ConfigureAwait(true)
            : await CheckRenameSafetyAsync(renameTarget, cancellationToken).ConfigureAwait(true);
        if (!safety.MayProceed)
        {
            ErrorMessage = safety.RefusalMessage;
            return;
        }

        ErrorMessage = null;
        try
        {
            await DdlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, CompileFailedFormat, ErrorText.Of(ex));
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, CompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            ObjectCreated?.Invoke(new ObjectCompileOutcome(TryParseName(sql)));
            return;
        }

        if (renameTarget is not null)
        {
            // ⚠ Deliberately NOT followed by RefreshAsync(): this tab is bound to the ORIGINAL name, so a
            // reload would fetch the object the user just renamed AWAY from and the editor would snap back to
            // it — the reported "Compile did nothing" symptom. The owner closes this tab and opens the object
            // that now exists, and tells the user what happened to the original.
            ObjectCreated?.Invoke(new ObjectCompileOutcome(renameTarget, LoadedObjectName));
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        CompiledExistingObject?.Invoke();
    }

    /// <summary>
    /// The name the compile would create, when that is NOT the object this tab is bound to; <c>null</c> for an
    /// ordinary compile of the loaded object.
    /// </summary>
    /// <remarks>
    /// ⚠ Compares against <see cref="LoadedObjectName"/> — the immutable name the tab was OPENED with — never
    /// against <see cref="ObjectDisplayName"/>, which follows the editable field and therefore already carries
    /// the NEW name by the time this runs. That conflation is the defect: one property answered both "what do
    /// we call this to the user" and "which object is this tab", and those stop being the same answer the
    /// moment the name becomes editable.
    /// </remarks>
    internal string? ResolveRenameTarget(string sql)
    {
        if (IsNew) return null;
        var target = TryParseName(sql);
        if (string.IsNullOrWhiteSpace(target)) return null;
        var loaded = LoadedObjectName;
        if (string.IsNullOrWhiteSpace(loaded)) return null;
        // Firebird folds unquoted identifiers to upper case, so the comparison is case-insensitive. A tab
        // opened on "MYPROC" and a statement naming "myproc" address the same object.
        return string.Equals(target.Trim(), loaded.Trim(), StringComparison.OrdinalIgnoreCase) ? null : target.Trim();
    }

    /// <summary>
    /// A rename is a CREATE as far as safety goes: the question is whether <paramref name="targetName"/> is
    /// free, because <c>CREATE OR ALTER</c> would otherwise overwrite somebody else's object — the very thing
    /// <see cref="ObjectChangeGate"/> exists to refuse. The original object is not at risk here (nothing
    /// touches it), so its definition is not re-read.
    /// </summary>
    private Task<ObjectChangeCheck> CheckRenameSafetyAsync(string targetName, CancellationToken cancellationToken)
    {
        var probe = ObjectExistsProbe;
        return _changeGate.CheckCreateAsync(
            targetName,
            probe is null ? null : ct => probe(targetName, ct),
            cancellationToken);
    }

    /// <summary>
    /// The name this tab was OPENED with — its identity, fixed for the tab's life. Distinct from
    /// <see cref="ObjectDisplayName"/>, which is a label and follows the editable name field.
    /// </summary>
    protected abstract string LoadedObjectName { get; }

    /// <summary>
    /// Asks the gate whether <paramref name="sql"/> may be written. Two different questions, because the two
    /// flows rest on different evidence:
    /// <list type="bullet">
    ///   <item><b>Existing object</b> — is the database still holding the definition this tab loaded? Answered
    ///   by re-reading it through the same <see cref="ReadDefinitionAsync"/> that produced the baseline.</item>
    ///   <item><b>New object</b> — is the chosen name free? The name is taken from the statement about to run
    ///   (<see cref="TryParseName"/>), not from the editable field, so the check is against the name that will
    ///   ACTUALLY be created — including a name typed directly into Source mode.</item>
    /// </list>
    /// </summary>
    private Task<ObjectChangeCheck> CheckChangeSafetyAsync(string sql, CancellationToken cancellationToken)
    {
        if (!IsNew)
        {
            // The lambda (rather than a method group) is only nullability plumbing: Task<string> does not
            // convert to the gate's Task<string?> delegate, and the gate must accept null to represent
            // "nothing was read".
            return _changeGate.CheckOverwriteAsync(
                ObjectDisplayName, async ct => await ReadDefinitionAsync(ct).ConfigureAwait(true), cancellationToken);
        }

        var targetName = TryParseName(sql);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            // The statement's name could not be read, so there is nothing to look up. The generator produced
            // this text from the editor's own model, so this is a shape we do not recognise rather than a
            // hazard we detected — and the server still refuses a genuinely malformed CREATE. Let it through
            // rather than blocking a legitimate create on our own parsing limits.
            return Task.FromResult(ObjectChangeCheck.Allowed);
        }

        var probe = ObjectExistsProbe;
        return _changeGate.CheckCreateAsync(
            targetName,
            probe is null ? null : ct => probe(targetName, ct),
            cancellationToken);
    }

    // ─── ISavableObjectEditor (Save-and-close / Save-and-disconnect WorkGuard) ──
    // Thin adapter over ExecuteCompileAsync (the ONE save path) — not a second
    // mechanism. Compile reports failure by setting ErrorMessage and returning, so
    // success is "no error after the attempt".
    public async Task<EditorSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        await ExecuteCompileAsync(cancellationToken).ConfigureAwait(true);
        return ErrorMessage is null ? new EditorSaveResult(true, null) : new EditorSaveResult(false, ErrorMessage);
    }

    // ─── Revert (discard uncompiled edits, reload from DB) ────────────────
    //
    // The source-editor analog of the Table designer's "discard pending changes":
    // reload the object from the database, throwing away uncompiled edits. Confirms
    // first (the edits can't be recovered) so an accidental click never loses work.
    // Existing objects only — a not-yet-created object has no DB state to revert to, so
    // the button is disabled in the New flow (use Close to abandon it).

    /// <summary>Confirmation gate for the destructive Revert — the owner wires this to the
    /// shared ConfirmDialog. With no handler (tests) it proceeds (Task.FromResult(true)).</summary>
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.RevertChangesConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.RevertChangesConfirmFormat, ObjectDisplayName),
            ConfirmLabel = UiStrings.RevertChangesConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await RefreshAsync().ConfigureAwait(true);
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
        if (Reader is null || DdlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        // Programmatic population — not user edits. Reset to a clean state in finally.
        _suppressDirty = true;
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
            _suppressDirty = false;
            ClearDirty();
        }
    }

    /// <summary>Runs one load step, trapping a metadata read failure so the remaining
    /// steps still run (first error wins for the tab-level <see cref="ErrorMessage"/>).</summary>
    protected async Task SafeLoadAsync(Func<Task> step)
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

    // ─── Object-specific hooks ────────────────────────────────────────────

    /// <summary>Display name for Revert / unsaved-work messages — the editable name when
    /// set, otherwise the canonical name.</summary>
    protected abstract string ObjectDisplayName { get; }

    /// <summary>Notice shown when Source → Easy parsing fails (kept the last-good model).</summary>
    protected abstract string ParseFailedNotice { get; }

    /// <summary>Format string ("… {0}") for a failed Compile.</summary>
    protected abstract string CompileFailedFormat { get; }

    /// <summary>Unsaved-work label formats ("… {0}") for a new / modified object.</summary>
    protected abstract string UnsavedNewFormat { get; }
    protected abstract string UnsavedModifiedFormat { get; }

    /// <summary>Builds the COMMENT ON … statement for the editable Description tab.</summary>
    protected abstract string CommentSql(string? comment);

    /// <summary>Reassembles the full CREATE OR ALTER … text from the Easy-mode model.</summary>
    internal abstract string BuildFullSource();

    /// <summary>Parses the given source into the structured Easy-mode model (params /
    /// header + variables + body). Returns false when the text isn't recognisable — the
    /// caller keeps the last-good model and surfaces <see cref="ParseFailedNotice"/>.</summary>
    protected abstract bool TryApplySource(string source);

    /// <summary>Extracts the object name from a CREATE … statement (for the New flow's
    /// reopen-by-name), or null when it can't be parsed.</summary>
    protected abstract string? TryParseName(string? sql);

    /// <summary>The per-object load steps (source, header/params, body, dependencies,
    /// DDL, description). Wrapped by <see cref="LoadAsync"/> (suppression + dirty reset).</summary>
    protected abstract Task LoadCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads this object's definition as the database holds it right now — the ONE read that both populates
    /// the editor and answers "has it changed since?". Each subclass supplies its own catalog reconstruction
    /// (<c>FetchProcedureSourceAsync</c> and friends) for its canonical name.
    /// <para>It must be the same read on both paths. A baseline taken over one artifact and compared against
    /// another would report a conflict on every compile.</para>
    /// </summary>
    protected abstract Task<string> ReadDefinitionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The load step that reads the definition into <see cref="SourceText"/> AND captures the change-safety
    /// baseline. Subclasses call this as their first <see cref="LoadCoreAsync"/> step instead of fetching the
    /// source themselves, so loading and arming the gate are literally one act and cannot drift apart.
    /// <para>The baseline is dropped BEFORE the read: a re-read that fails must leave the gate unverifiable,
    /// never holding a stale baseline that would authorise overwriting a definition nobody has looked at.
    /// (<see cref="SafeLoadAsync"/> traps the read failure, so this returns having captured nothing.)</para>
    /// </summary>
    protected async Task LoadDefinitionAsync(CancellationToken cancellationToken)
    {
        _changeGate.Forget();
        var definition = await ReadDefinitionAsync(cancellationToken).ConfigureAwait(true);
        SourceText = definition;
        _changeGate.CaptureBaseline(definition);
    }

    /// <summary>Optional Easy-mode pre-compile validation — returns an error message to
    /// block Compile, or null to proceed. Default: no extra validation.</summary>
    protected virtual string? ValidateBeforeCompile() => null;
}
