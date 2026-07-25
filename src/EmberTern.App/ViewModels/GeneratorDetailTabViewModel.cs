using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird GENERATOR (sequence). Three tabs — Generator
/// (the editable form: name / current value / initial value / increment /
/// description), Dependencies (reuses TableDetail's dual dependency tree), and
/// DDL (read-only reconstructed CREATE SEQUENCE). A generator has no PSQL body,
/// so this is NOT a <see cref="SourceObjectDetailTabViewModel"/> subclass — it's
/// a focused form modeled on <see cref="ViewDetailTabViewModel"/> but simpler
/// (no Source/Easy mode, no data preview, no fields). Reuse happens at the
/// reader / DDL-generator / dependency-tree level, not via inheritance.
/// </summary>
public partial class GeneratorDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor
{
    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlReader? _ddlReader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Baseline (last-loaded / last-saved) values, used by Save to emit the
    // minimum-set of ALTER statements for what actually changed.
    private long _baselineCurrent;
    private long _baselineInitial;
    private long _baselineIncrement = 1;
    private string _baselineDescription = string.Empty;

    public GeneratorDetailTabViewModel(string generatorName)
        : this(generatorName, null, null, null)
    {
    }

    public GeneratorDetailTabViewModel(
        string generatorName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        GeneratorName = generatorName;
        EditableName = generatorName;
        _reader = reader;
        _ddlReader = ddlReader;
        _ddlExecutor = ddlExecutor;
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        // Ctor assignments (EditableName) must not flip dirty.
        _suppressDirty = false;
    }

    /// <summary>Original generator name (immutable). A generator isn't renamed via
    /// the editor — <see cref="EditableName"/> is editable only in the New flow.</summary>
    public string GeneratorName { get; }

    /// <summary>True for a not-yet-created generator (the New Generator flow). The
    /// Dependencies / DDL tabs stay empty until the first successful Compile, after
    /// which the owner reopens the real generator. Compile in this mode raises
    /// <see cref="GeneratorCreated"/>.</summary>
    public bool IsNew { get; init; }

    // ─── Dirty tracking (drives IUnsavedWorkSource + Revert) ──────────────
    private bool _isDirty;
    private bool _suppressDirty = true;

    public bool IsDirty => _isDirty;
    internal void ClearDirty() => SetDirty(false);
    private void MarkDirty() { if (!_suppressDirty) SetDirty(true); }

    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        OnPropertyChanged(nameof(IsDirty));
        RevertChangesCommand.NotifyCanExecuteChanged();
    }

    // ─── Editable form fields ─────────────────────────────────────────────

    /// <summary>Generator name. Editable only in the New flow (the view binds
    /// <c>IsReadOnly="{Binding !IsNew}"</c>); UPPERCASED on input (gotcha #141).</summary>
    [ObservableProperty]
    private string _editableName = string.Empty;

    private bool _settingNameUpper;
    partial void OnEditableNameChanged(string value)
    {
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableName = upper; } finally { _settingNameUpper = false; }
                return;
            }
        }
        MarkDirty();
    }

    /// <summary>Current value of the generator in the database (read via
    /// GEN_ID(name, 0)). Editing it emits <c>ALTER SEQUENCE … RESTART WITH</c>.
    /// Distinct from <see cref="InitialValue"/>.</summary>
    [ObservableProperty]
    private decimal? _currentValue;

    /// <summary>Initial value from the sequence definition (RDB$INITIAL_VALUE).</summary>
    [ObservableProperty]
    private decimal? _initialValue;

    /// <summary>Increment / step from the sequence definition
    /// (RDB$GENERATOR_INCREMENT). Default 1.</summary>
    [ObservableProperty]
    private decimal? _increment = 1;

    partial void OnCurrentValueChanged(decimal? value) => MarkDirty();
    partial void OnInitialValueChanged(decimal? value) => MarkDirty();
    partial void OnIncrementChanged(decimal? value) => MarkDirty();

    /// <summary>Loaded description (baseline mirror). Set on load; the user edits
    /// <see cref="EditableDescription"/>.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value) => EditableDescription = value ?? string.Empty;
    partial void OnEditableDescriptionChanged(string value) => MarkDirty();

    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

    /// <summary>Read-only reconstructed DDL (CREATE SEQUENCE …) — the DDL tab.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ─── Unsaved-work (WorkGuard) ──────────────────────────────────────────

    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        var name = string.IsNullOrWhiteSpace(EditableName) ? GeneratorName : EditableName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedNewGeneratorFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedGeneratorFormat, name));
    }

    // ─── Dependency tree open (reuses TableDetail's routing) ───────────────

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

    // ─── Confirmation (Revert + Delete) ────────────────────────────────────

    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    // ─── Compile ──────────────────────────────────────────────────────────────

    /// <summary>Raised after a successful Compile in <see cref="IsNew"/> mode, with
    /// the created generator's name. The owner refreshes the metadata tree, closes
    /// the New tab, and reopens the real generator.</summary>
    public event Action<string?>? GeneratorCreated;

    public bool CanCompile => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    /// <summary>
    /// Builds the DDL for the current form state vs. the loaded baseline and runs
    /// it. New generator → CREATE SEQUENCE (+ COMMENT). Existing → the minimum-set
    /// of ALTER SEQUENCE / COMMENT statements for what changed (empty → no-op).
    /// Runs through <see cref="FirebirdDdlExecutor"/> (autonomous, auto-committed).
    /// </summary>
    // ─── ISavableObjectEditor (Save-and-close / Save-and-disconnect WorkGuard) ──
    // Thin adapter over ExecuteCompileAsync (the ONE save path) — not a second mechanism.
    public async Task<EditorSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        await ExecuteCompileAsync(cancellationToken).ConfigureAwait(true);
        return ErrorMessage is null ? new EditorSaveResult(true, null) : new EditorSaveResult(false, ErrorMessage);
    }

    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        // Reports instead of exiting silently (Seam 6b) — see the contract on ISavableObjectEditor.
        if (_ddlExecutor is null)
        {
            ErrorMessage = UiStrings.NoConnectionMessage;
            return;
        }
        ErrorMessage = null;

        string sql;
        try
        {
            sql = BuildCompileSql();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }
        // Diff-based editor: an empty diff means there is genuinely nothing to write, so this stays an
        // ordinary no-op rather than a reported failure (Seam 6b — the documented exception).
        if (string.IsNullOrWhiteSpace(sql)) return; // nothing changed

        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.GeneratorCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.GeneratorCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            GeneratorCreated?.Invoke(EditableName.Trim());
            return;
        }

        // Existing generator: re-read so values + DDL + baselines refresh and dirty clears.
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Builds the Compile DDL. Pure + internal so tests can assert the shape
    /// without a database. Returns an empty string when an existing generator has
    /// no changes (no-op).</summary>
    internal string BuildCompileSql()
    {
        var statements = new List<string>();
        if (IsNew)
        {
            // New generator. Two statements:
            //  1. CREATE … START WITH <initial> [INCREMENT BY <inc>] — sets the
            //     DEFINITION (RDB$INITIAL_VALUE). forceStartWith so the initial value
            //     is recorded even for 0 (a plain CREATE leaves RDB$INITIAL_VALUE=1 on
            //     FB4+).
            //  2. SET GENERATOR … TO <initial> — pins the RUNTIME counter to the
            //     initial value so Current Value == Initial Value right after creation
            //     on ALL Firebird versions. Without it, FB4+ START WITH leaves the
            //     counter at <initial − increment> (e.g. 9999 for START WITH 10000),
            //     which the user reported as "current is one less than initial".
            //     SET GENERATOR is version-INDEPENDENT (unlike RESTART WITH); verified
            //     on FB3 + FB5. (NOT the old bug — that appended RESTART to the
            //     UNRELATED default 0, destroying the initial value; this pins to the
            //     SAME initial value, intentionally.)
            var name = EditableName.Trim();
            var initial = AsLong(InitialValue);
            statements.Add(DdlGenerator.BuildCreateSequence(name, initial, AsLong(Increment, 1), forceStartWith: true));
            statements.Add(DdlGenerator.BuildSetGenerator(name, initial));
            if (!string.IsNullOrWhiteSpace(EditableDescription))
            {
                statements.Add(DdlGenerator.BuildCommentSequence(name, EditableDescription));
            }
        }
        else
        {
            // Existing generator: Initial Value + Increment are read-only definition
            // facts (FB3 can't ALTER them; the model deliberately doesn't conflate the
            // definition with the runtime state — Problem 4). The only runtime change is
            // Current Value, set with the version-INDEPENDENT SET GENERATOR (GEN_ID(,0)
            // becomes exactly the typed value on both FB3 and FB5 — RESTART WITH would be
            // off-by-one on FB5).
            var name = GeneratorName;
            if (AsLong(CurrentValue) != _baselineCurrent)
            {
                statements.Add(DdlGenerator.BuildSetGenerator(name, AsLong(CurrentValue)));
            }
            var desc = EditableDescription ?? string.Empty;
            if (!string.Equals(desc, _baselineDescription, StringComparison.Ordinal))
            {
                statements.Add(DdlGenerator.BuildCommentSequence(name, string.IsNullOrWhiteSpace(desc) ? null : desc));
            }
        }

        return string.Join(";\n", statements);
    }

    private static long AsLong(decimal? value, long fallback = 0)
        => value is { } v ? (long)v : fallback;

    /// <summary>
    /// Presentation normalization for the Current Value. Firebird's raw counter for a
    /// freshly created / never-used sequence can be a negative pre-first-use sentinel
    /// (e.g. FB5 stores <c>START WITH − increment</c>, so a <c>START WITH 0</c> sequence
    /// reads -1). A generator must never DISPLAY a negative current value after
    /// creation — map &lt;0 to 0, matching Firebird-tools convention. The reader still
    /// returns the technically-correct raw value; only the displayed/baselined value is
    /// normalized.
    /// </summary>
    internal static long NormalizeDisplayCurrentValue(long raw) => raw < 0 ? 0 : raw;

    // ─── Field editability (de-conflate definition vs runtime — Problem 4) ──
    //
    // Two worlds: the DEFINITION (Initial Value + Increment → CREATE SEQUENCE) is set
    // once at creation and is read-only afterward (FB3 can't ALTER it, and editing it
    // post-create would conflate definition with runtime). The RUNTIME counter (Current
    // Value → SET GENERATOR) is meaningless before the object exists, so it's read-only
    // while creating and editable only on an existing generator.

    /// <summary>Initial Value + Increment are editable only for a NEW generator.</summary>
    public bool IsDefinitionEditable => IsNew;

    /// <summary>Current Value is editable only on an EXISTING generator (it's a runtime
    /// consequence of the definition, not independently set at creation).</summary>
    public bool IsCurrentValueEditable => !IsNew;

    // ─── Revert (discard edits, reload from DB) ───────────────────────────

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var name = string.IsNullOrWhiteSpace(EditableName) ? GeneratorName : EditableName.Trim();
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

    // ─── Delete (DROP SEQUENCE) ───────────────────────────────────────────

    /// <summary>Raised after a confirmed Delete. The owner runs DROP SEQUENCE,
    /// closes this tab, and refreshes the metadata tree.</summary>
    public event Func<GeneratorDetailTabViewModel, Task>? DeleteRequested;

    public bool CanDelete => _ddlExecutor is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (!CanDelete) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.GeneratorDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.GeneratorDeleteConfirmFormat, GeneratorName),
            ConfirmLabel = UiStrings.GeneratorDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await (DeleteRequested?.Invoke(this) ?? Task.CompletedTask).ConfigureAwait(true);
    }

    // ─── Refresh Current Value (Problem 5) ────────────────────────────────
    //
    // Re-reads ONLY the live runtime counter (GEN_ID(name, 0)) — a generator may be
    // in active use by a running ERP, so the user must be able to re-check its current
    // value without reloading the whole object. Does NOT reload Initial Value /
    // Increment / Description / Dependencies / DDL, does NOT flip dirty state, and
    // updates the baseline so the refreshed value isn't seen as a pending change.

    public bool CanRefreshCurrentValue => _reader is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRefreshCurrentValue))]
    private async Task RefreshCurrentValue()
    {
        if (_reader is null) return;
        try
        {
            var raw = await _reader.GetGeneratorCurrentValueAsync(GeneratorName).ConfigureAwait(true);
            var norm = NormalizeDisplayCurrentValue(raw);
            // Suppress dirty while updating: a refresh is not a user edit. Update the
            // baseline too so a subsequent Save doesn't emit a spurious SET GENERATOR.
            var prev = _suppressDirty;
            _suppressDirty = true;
            try { CurrentValue = norm; }
            finally { _suppressDirty = prev; }
            _baselineCurrent = norm;
        }
        catch (MetadataReadException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

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
        // New generator: nothing to load until it's created.
        if (IsNew) return;
        if (_reader is null || _ddlReader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                var info = await _reader.GetGeneratorInfoAsync(GeneratorName, cancellationToken).ConfigureAwait(true);
                // Present a never-used generator's negative pre-first-use sentinel as 0
                // (Firebird-tools convention); baseline the same value so an untouched
                // generator doesn't emit a spurious SET GENERATOR on Save.
                var displayCurrent = NormalizeDisplayCurrentValue(info.CurrentValue);
                EditableName = info.Name;
                CurrentValue = displayCurrent;
                InitialValue = info.InitialValue;
                Increment = info.Increment;
                Description = info.Description;
                _baselineCurrent = displayCurrent;
                _baselineInitial = info.InitialValue;
                _baselineIncrement = info.Increment;
                _baselineDescription = info.Description;
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetGeneratorDependenciesAsync(GeneratorName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
            });

            await SafeLoadAsync(async () =>
            {
                // DDL tab == Export (structure + COMMENT ON SEQUENCE via MetadataExportService).
                DdlText = await new MetadataExportService(_ddlReader, _reader).BuildObjectScriptAsync(
                    new MetadataObject(GeneratorName, MetadataObjectKind.Generator), cancellationToken).ConfigureAwait(true);
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
