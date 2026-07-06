using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird custom EXCEPTION. Four tabs — Exception (the
/// editable form: name + message), Description (editable COMMENT ON EXCEPTION),
/// Dependencies (the dual dependency tree — an exception is "depended on by"
/// procedures / functions / triggers / packages that raise it), and DDL
/// (read-only reconstructed CREATE EXCEPTION).
///
/// An exception has no PSQL body and no parameters, so this is NOT a
/// <see cref="SourceObjectDetailTabViewModel"/> subclass — it's a focused form
/// modeled on <see cref="GeneratorDetailTabViewModel"/> /
/// <see cref="DomainDetailTabViewModel"/> (the simplest editors). Reuse happens at
/// the reader / DDL-generator (<see cref="DdlGenerator"/>) / dependency-tree
/// (<see cref="TableDetailTabViewModel.BuildDependencyTree"/>) level. Compile emits
/// CREATE / ALTER EXCEPTION (message) + COMMENT ON EXCEPTION (description); Firebird
/// has no ALTER EXCEPTION … TO, so the name is read-only on an existing exception.
/// </summary>
public partial class ExceptionDetailTabViewModel : ViewModelBase, IUnsavedWorkSource
{
    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Baseline (last-loaded / last-saved) values, used by Compile to emit the
    // minimum-set of ALTER EXCEPTION / COMMENT statements for what actually changed.
    private string _baselineMessage = string.Empty;
    private string _baselineDescription = string.Empty;

    public ExceptionDetailTabViewModel(string exceptionName)
        : this(exceptionName, null, null)
    {
    }

    public ExceptionDetailTabViewModel(
        string exceptionName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        ExceptionName = exceptionName;
        EditableName = exceptionName;
        _reader = reader;
        _ddlExecutor = ddlExecutor;
        DependsOnTree = new ObservableCollection<DependencyGroupNode>();
        DependedOnByTree = new ObservableCollection<DependencyGroupNode>();
        // Ctor assignments (EditableName) must not flip dirty.
        _suppressDirty = false;
    }

    /// <summary>Original exception name (immutable). An exception isn't renamed via
    /// the editor (Firebird has no ALTER EXCEPTION … TO) — <see cref="EditableName"/>
    /// is editable only in the New flow.</summary>
    public string ExceptionName { get; }

    /// <summary>True for a not-yet-created exception (the New Exception flow). The
    /// Dependencies tab stays empty until the first successful Compile, after which
    /// the owner reopens the real exception. Compile in this mode raises
    /// <see cref="ExceptionCreated"/>.</summary>
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

    /// <summary>Exception name. Editable only in the New flow (the view binds
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
        RefreshDdl();
    }

    /// <summary>The raised message text (RDB$MESSAGE). Editable on both new and
    /// existing exceptions — a change emits <c>ALTER EXCEPTION … 'message'</c>.</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    partial void OnMessageChanged(string value) { MarkDirty(); RefreshDdl(); }

    /// <summary>Loaded description (baseline mirror). Set on load; the user edits
    /// <see cref="EditableDescription"/>.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value) => EditableDescription = value ?? string.Empty;
    partial void OnEditableDescriptionChanged(string value) { MarkDirty(); RefreshDdl(); }

    public ObservableCollection<DependencyGroupNode> DependsOnTree { get; }
    public ObservableCollection<DependencyGroupNode> DependedOnByTree { get; }

    /// <summary>Read-only reconstructed DDL (CREATE EXCEPTION …) — the DDL tab. Built
    /// from the live form state via <see cref="DdlGenerator.BuildCreateException"/>, so
    /// it mirrors exactly what Compile (in the New flow) would execute.</summary>
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
        var name = string.IsNullOrWhiteSpace(EditableName) ? ExceptionName : EditableName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedNewExceptionFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedExceptionFormat, name));
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
    /// the created exception's name. The owner refreshes the tree, closes the New tab,
    /// and reopens the real exception.</summary>
    public event Action<string?>? ExceptionCreated;

    public bool CanCompile => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    /// <summary>
    /// Builds the DDL for the current form vs. the loaded baseline and runs it.
    /// New exception → CREATE EXCEPTION (+ COMMENT). Existing → the minimum set of
    /// ALTER EXCEPTION (message) + COMMENT (description) statements (empty → no-op).
    /// Runs through <see cref="FirebirdDdlExecutor"/> (autonomous, auto-committed).
    /// </summary>
    public async Task ExecuteCompileAsync(CancellationToken cancellationToken = default)
    {
        if (_ddlExecutor is null) return;
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
        if (string.IsNullOrWhiteSpace(sql)) return; // nothing changed

        try
        {
            await _ddlExecutor.ExecuteAsync(sql, cancellationToken).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ExceptionCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ExceptionCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            ExceptionCreated?.Invoke(EditableName.Trim());
            return;
        }

        // Existing exception: re-read so message + DDL + baselines refresh and dirty clears.
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Builds the Compile DDL. Pure + internal so tests can assert the shape
    /// without a database. Returns an empty string when an existing exception has no
    /// changes (no-op).</summary>
    internal string BuildCompileSql()
    {
        var statements = new List<string>();
        if (IsNew)
        {
            var name = EditableName.Trim();
            statements.Add(DdlGenerator.BuildCreateException(name, Message ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(EditableDescription))
            {
                statements.Add(DdlGenerator.BuildCommentException(name, EditableDescription));
            }
        }
        else
        {
            var name = ExceptionName;
            var newMessage = Message ?? string.Empty;
            if (!string.Equals(newMessage, _baselineMessage, StringComparison.Ordinal))
            {
                statements.Add(DdlGenerator.BuildAlterException(name, newMessage));
            }
            var desc = EditableDescription ?? string.Empty;
            if (!string.Equals(desc, _baselineDescription, StringComparison.Ordinal))
            {
                statements.Add(DdlGenerator.BuildCommentException(name, string.IsNullOrWhiteSpace(desc) ? null : desc));
            }
        }

        return string.Join(";\n", statements);
    }

    // Recompute the DDL-tab text from the live form. Guarded: a New exception with no
    // name yet has nothing to preview (BuildCreateException requires a name).
    private void RefreshDdl()
    {
        var name = (IsNew ? EditableName : ExceptionName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) { DdlText = string.Empty; return; }
        try
        {
            // DDL tab == Export: structure (live form; the message is part of the CREATE) +
            // COMMENT ON EXCEPTION, composed through the same PortableDdl the export uses.
            DdlText = PortableDdl.Compose(
                DdlGenerator.BuildCreateException(name, Message ?? string.Empty),
                new[] { PortableDdl.ObjectComment(MetadataObjectKind.Exception, name, EditableDescription) });
        }
        catch (ArgumentException) { DdlText = string.Empty; }
    }

    // ─── Revert (discard edits, reload from DB) ───────────────────────────

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var name = string.IsNullOrWhiteSpace(EditableName) ? ExceptionName : EditableName.Trim();
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

    // ─── Delete (DROP EXCEPTION) ──────────────────────────────────────────

    /// <summary>Raised after a confirmed Delete. The owner runs DROP EXCEPTION,
    /// closes this tab, and refreshes the metadata tree.</summary>
    public event Func<ExceptionDetailTabViewModel, Task>? DeleteRequested;

    public bool CanDelete => _ddlExecutor is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (!CanDelete) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.ExceptionDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.ExceptionDeleteConfirmFormat, ExceptionName),
            ConfirmLabel = UiStrings.ExceptionDeleteConfirmYes,
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
        // New exception: nothing to load until it's created.
        if (IsNew) return;
        if (_reader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                var info = await _reader.GetExceptionInfoAsync(ExceptionName, cancellationToken).ConfigureAwait(true);
                EditableName = info.Name;
                Message = info.Message;
                Description = info.Description;
                _baselineMessage = info.Message;
                _baselineDescription = info.Description;
                RefreshDdl();
            });

            await SafeLoadAsync(async () =>
            {
                var (dependsOn, dependedOnBy) = await _reader.GetExceptionDependenciesAsync(ExceptionName, cancellationToken).ConfigureAwait(true);
                DependsOnTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
                DependedOnByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
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
