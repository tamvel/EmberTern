using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird INDEX — two tabs: Index (the properties form) and
/// DDL (read-only reconstructed CREATE INDEX). An index is almost entirely read-only
/// in Firebird (verified on FB 5.0.3): the ONLY mutable properties are Active/Inactive
/// (plain indexes only — PK/FK/UNIQUE backing indexes reject deactivation) and the
/// Description (COMMENT ON INDEX). Everything structural requires DROP + CREATE; there
/// is no rename. Index <i>creation</i> lives exclusively in Table Detail → Indexes, so
/// there is no New-Index flow here.
///
/// Form-based, modeled on <see cref="GeneratorDetailTabViewModel"/> (no PSQL body, so
/// NOT a <see cref="SourceObjectDetailTabViewModel"/> subclass). Reuse happens at the
/// reader / DDL-generator / dependency level, not via inheritance.
/// </summary>
public partial class IndexDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor
{
    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Loaded (immutable) catalog facts — table / fields / unique / direction /
    // expression / constraint type / statistics — used to rebuild the DDL preview
    // around the two editable properties (Active, Description).
    private IndexDetailInfo _loaded = new();
    private bool _baselineActive = true;
    private string _baselineDescription = string.Empty;

    public IndexDetailTabViewModel(string indexName)
        : this(indexName, null, null)
    {
    }

    public IndexDetailTabViewModel(
        string indexName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        IndexName = indexName;
        _reader = reader;
        _ddlExecutor = ddlExecutor;
        _suppressDirty = false;
    }

    /// <summary>Index name (immutable — Firebird has no index rename).</summary>
    public string IndexName { get; }

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

    // ─── Read-only properties (display) ────────────────────────────────────

    [ObservableProperty]
    private string _table = string.Empty;

    [ObservableProperty]
    private string _fields = string.Empty;

    [ObservableProperty]
    private string? _expression;

    [ObservableProperty]
    private bool _isUnique;

    [ObservableProperty]
    private bool _isDescending;

    /// <summary>"PRIMARY KEY" / "FOREIGN KEY" / "UNIQUE" / "" — drives
    /// <see cref="IsConstraintBacked"/> and the explanatory note.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConstraintBacked))]
    [NotifyPropertyChangedFor(nameof(IsActiveEditable))]
    [NotifyPropertyChangedFor(nameof(ConstraintBackedNote))]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private string _constraintType = string.Empty;

    [ObservableProperty]
    private double? _statistics;

    [ObservableProperty]
    private bool _isSystem;

    public string SortDirectionText => IsDescending ? "DESCENDING" : "ASCENDING";

    /// <summary>True when this index backs a PRIMARY KEY / UNIQUE / FOREIGN KEY
    /// constraint — Firebird manages it through the constraint, so Active/Drop are
    /// disabled. Shown to the user via <see cref="ConstraintBackedNote"/>.</summary>
    public bool IsConstraintBacked => !string.IsNullOrEmpty(ConstraintType);

    /// <summary>One-line explanation shown when constraint-backed, so it's
    /// immediately obvious WHY Active/Drop are disabled.</summary>
    public string ConstraintBackedNote
        => IsConstraintBacked
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.IndexConstraintBackedNoteFormat, ConstraintType)
            : string.Empty;

    // ─── Editable properties ───────────────────────────────────────────────

    /// <summary>Active flag. Editable only for a plain (non-constraint) index — the
    /// engine rejects ALTER INDEX … INACTIVE on PK/FK/UNIQUE backing indexes.</summary>
    [ObservableProperty]
    private bool _isActive = true;

    /// <summary>Active toggle is enabled only for a non-constraint index.</summary>
    public bool IsActiveEditable => !IsConstraintBacked;

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnIsActiveChanged(bool value) { MarkDirty(); RefreshDdl(); }
    partial void OnEditableDescriptionChanged(string value) { MarkDirty(); RefreshDdl(); }

    /// <summary>Read-only reconstructed DDL (CREATE INDEX …) — the DDL tab. Rebuilt
    /// live from the loaded facts + the current Active/Description so the preview
    /// reflects pending (uncompiled) edits.</summary>
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
        => IsDirty
            ? new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedIndexFormat, IndexName))
            : null;

    // ─── Confirmation (Revert + Delete) ────────────────────────────────────

    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    // ─── Compile (apply Active + Description changes) ───────────────────────

    // Description (COMMENT ON INDEX) is editable for ANY index; the Active toggle is
    // only reachable for a non-constraint index. So Compile is available whenever an
    // executor is wired — its diff decides what (if anything) to emit.
    public bool CanCompile => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

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

        // Diff-based editor: an empty diff means there is genuinely nothing to write, so this stays an
        // ordinary no-op rather than a reported failure (Seam 6b — the documented exception).
        var sql = BuildCompileSql();
        if (string.IsNullOrWhiteSpace(sql)) return; // nothing changed

        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexCompileFailedFormat, ex.Message);
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Builds the Compile DDL (pure + internal so tests can assert the shape).
    /// Emits ALTER INDEX … ACTIVE/INACTIVE when the Active flag changed (non-constraint
    /// indexes only) and COMMENT ON INDEX when the description changed. Empty → no-op.</summary>
    internal string BuildCompileSql()
    {
        var statements = new List<string>();

        if (!IsConstraintBacked && IsActive != _baselineActive)
        {
            statements.Add(IsActive
                ? DdlGenerator.BuildAlterIndexActive(IndexName)
                : DdlGenerator.BuildAlterIndexInactive(IndexName));
        }

        var desc = EditableDescription ?? string.Empty;
        if (!string.Equals(desc, _baselineDescription, StringComparison.Ordinal))
        {
            statements.Add(DdlGenerator.BuildCommentIndex(IndexName, string.IsNullOrWhiteSpace(desc) ? null : desc));
        }

        return string.Join(";\n", statements);
    }

    // ─── Recompute statistics (SET STATISTICS INDEX) ───────────────────────
    //
    // Works on all indexes (incl. constraint-backed) — verified on FB 5.0.3. Runs
    // autonomously (auto-committed) then re-reads so the displayed selectivity updates.

    public bool CanRecomputeStatistics => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanRecomputeStatistics))]
    private async Task RecomputeStatistics()
    {
        if (_ddlExecutor is null) return;
        ErrorMessage = null;
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildSetIndexStatistics(IndexName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexCompileFailedFormat, ex.Message);
            return;
        }
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── Revert (discard edits, reload from DB) ───────────────────────────

    public bool CanRevertChanges => IsDirty;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.RevertChangesConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.RevertChangesConfirmFormat, IndexName),
            ConfirmLabel = UiStrings.RevertChangesConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await RefreshAsync().ConfigureAwait(true);
    }

    // ─── Delete (DROP INDEX) ──────────────────────────────────────────────

    /// <summary>Raised after a confirmed Delete. The owner runs DROP INDEX, closes
    /// this tab, and refreshes the metadata tree.</summary>
    public event Func<IndexDetailTabViewModel, Task>? DeleteRequested;

    // Constraint-backed indexes can't be dropped directly (Firebird manages them
    // through the constraint) — gate Delete on IsConstraintBacked.
    public bool CanDelete => _ddlExecutor is not null && !IsConstraintBacked;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (!CanDelete) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.IndexDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexDeleteConfirmFormat, IndexName),
            ConfirmLabel = UiStrings.IndexDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        await (DeleteRequested?.Invoke(this) ?? Task.CompletedTask).ConfigureAwait(true);
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
        if (_reader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            var info = await _reader.GetIndexDetailAsync(IndexName, cancellationToken).ConfigureAwait(true);
            if (info is null)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.IndexNotFoundFormat, IndexName);
                return;
            }

            _loaded = info;
            Table = info.Table;
            Fields = info.Fields;
            Expression = info.Expression;
            IsUnique = info.IsUnique;
            IsDescending = info.IsDescending;
            ConstraintType = info.ConstraintType;
            Statistics = info.Statistics;
            IsSystem = info.IsSystem;
            IsActive = info.IsActive;
            EditableDescription = info.Description;
            _baselineActive = info.IsActive;
            _baselineDescription = info.Description;
            RefreshDdl();
        }
        catch (MetadataReadException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _suppressDirty = false;
            ClearDirty();
        }
    }

    // Rebuild the DDL preview from the loaded facts + the current (possibly edited)
    // Active / Description, so the DDL tab reflects what Compile will produce.
    private void RefreshDdl()
    {
        if (string.IsNullOrEmpty(_loaded.Name)) return;
        DdlText = DdlGenerator.BuildIndexDdl(new IndexDetailInfo
        {
            Name = _loaded.Name,
            Table = _loaded.Table,
            Fields = _loaded.Fields,
            IsUnique = _loaded.IsUnique,
            IsDescending = _loaded.IsDescending,
            IsActive = IsActive,
            Statistics = _loaded.Statistics,
            Expression = _loaded.Expression,
            ConstraintType = _loaded.ConstraintType,
            Description = EditableDescription ?? string.Empty,
            IsSystem = _loaded.IsSystem,
        });
    }
}
