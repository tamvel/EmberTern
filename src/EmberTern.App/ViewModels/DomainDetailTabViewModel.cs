using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird DOMAIN. Four tabs — Domain (the definition form:
/// name / type / length / precision / scale / sub-type / charset / collation /
/// default / check / not null), Description (editable COMMENT ON DOMAIN), Used By
/// (a SINGLE dependency tree of the table/view columns + PSQL objects that use the
/// domain), and DDL (read-only reconstructed CREATE DOMAIN).
///
/// A domain has no PSQL body, so this is NOT a <see cref="SourceObjectDetailTabViewModel"/>
/// subclass — it's a focused form modeled on <see cref="GeneratorDetailTabViewModel"/>.
/// Reuse happens at the reader / DDL-generator (<see cref="DdlGenerator"/>) /
/// dependency-tree (<see cref="TableDetailTabViewModel.BuildDependencyTree"/>) level.
///
/// Editable scope on an EXISTING domain is Default / Check / Description — Firebird's
/// ALTER DOMAIN supports exactly those (plus type/rename/not-null, kept out of scope).
/// CHARACTER SET and COLLATION can NEVER be ALTERed (FB3 + FB5: SQL error -104), so
/// they — and the rest of the type definition — are read-only after creation.
/// </summary>
public partial class DomainDetailTabViewModel : ViewModelBase, IUnsavedWorkSource
{
    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Baseline (last-loaded / last-saved) values, used by Save to emit the minimum
    // set of ALTER DOMAIN / COMMENT statements for what actually changed.
    private string? _baselineDefault;
    private string? _baselineCheck;
    private string _baselineDescription = string.Empty;

    public DomainDetailTabViewModel(string domainName)
        : this(domainName, null, null)
    {
    }

    public DomainDetailTabViewModel(
        string domainName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlExecutor? ddlExecutor)
    {
        DomainName = domainName;
        EditableName = domainName;
        _reader = reader;
        _ddlExecutor = ddlExecutor;
        UsedByTree = new ObservableCollection<DependencyGroupNode>();
        // Ctor assignments (EditableName) must not flip dirty.
        _suppressDirty = false;
    }

    /// <summary>Original domain name (immutable). A domain isn't renamed via the
    /// editor — <see cref="EditableName"/> is editable only in the New flow.</summary>
    public string DomainName { get; }

    /// <summary>True for a not-yet-created domain (the New Domain flow). The Used By /
    /// DDL tabs stay empty until the first successful Save, after which the owner
    /// reopens the real domain. Save in this mode raises <see cref="DomainCreated"/>.</summary>
    public bool IsNew { get; init; }

    // Reuse the field editor's basic-type list + the charset catalog — no second
    // type vocabulary.
    public static IReadOnlyList<string> AvailableDataTypes => AddFieldDialogViewModel.BasicTypes;
    public static IReadOnlyList<string> AvailableCharsets => CharsetCatalog.Supported;

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

    /// <summary>Domain name. Editable only in the New flow (the view binds
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

    // Type-definition fields. Editable only when creating (IsDefinitionEditable);
    // read-only on an existing domain (Firebird can't ALTER charset/collation, and
    // the rest is out of EmberTern's edit scope).
    [ObservableProperty] private string _dataType = "VARCHAR";
    [ObservableProperty] private decimal? _length;
    [ObservableProperty] private decimal? _precision;
    [ObservableProperty] private decimal? _scale;
    [ObservableProperty] private decimal? _subType;
    [ObservableProperty] private string? _characterSet;
    [ObservableProperty] private string? _collation;
    [ObservableProperty] private bool _notNull;

    partial void OnDataTypeChanged(string value) { MarkDirty(); RefreshDdl(); }
    partial void OnLengthChanged(decimal? value) { MarkDirty(); RefreshDdl(); }
    partial void OnPrecisionChanged(decimal? value) { MarkDirty(); RefreshDdl(); }
    partial void OnScaleChanged(decimal? value) { MarkDirty(); RefreshDdl(); }
    partial void OnSubTypeChanged(decimal? value) { MarkDirty(); RefreshDdl(); }
    partial void OnCharacterSetChanged(string? value) { MarkDirty(); RefreshDdl(); }
    partial void OnCollationChanged(string? value) { MarkDirty(); RefreshDdl(); }
    partial void OnNotNullChanged(bool value) { MarkDirty(); RefreshDdl(); }

    // Editable on BOTH new and existing domains.
    [ObservableProperty] private string? _defaultValue;
    [ObservableProperty] private string? _checkConstraint;

    partial void OnDefaultValueChanged(string? value) { MarkDirty(); RefreshDdl(); }
    partial void OnCheckConstraintChanged(string? value) { MarkDirty(); RefreshDdl(); }

    /// <summary>Loaded description (baseline mirror). Set on load; the user edits
    /// <see cref="EditableDescription"/>.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    partial void OnDescriptionChanged(string value) => EditableDescription = value ?? string.Empty;
    partial void OnEditableDescriptionChanged(string value) => MarkDirty();

    public ObservableCollection<DependencyGroupNode> UsedByTree { get; }

    /// <summary>Read-only reconstructed DDL (CREATE DOMAIN …) — the DDL tab. Built
    /// from the live form state via <see cref="DdlGenerator.BuildCreateDomain"/>, so
    /// it mirrors exactly what Save (in the New flow) would execute.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Type-definition fields (type/length/…/charset/collation/not-null)
    /// are editable only for a NEW domain.</summary>
    public bool IsDefinitionEditable => IsNew;

    // ─── Unsaved-work (WorkGuard) ──────────────────────────────────────────

    public UnsavedWorkItem? GetUnsavedWork()
    {
        if (!IsDirty) return null;
        var name = string.IsNullOrWhiteSpace(EditableName) ? DomainName : EditableName.Trim();
        return IsNew
            ? new UnsavedWorkItem(UnsavedWorkKind.NewObject,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedNewDomainFormat, name))
            : new UnsavedWorkItem(UnsavedWorkKind.ModifiedSource,
                string.Format(CultureInfo.CurrentCulture, UiStrings.UnsavedModifiedDomainFormat, name));
    }

    // ─── Used By tree open (reuses TableDetail's routing) ──────────────────

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

    // ─── Save ───────────────────────────────────────────────────────────────

    /// <summary>Raised after a successful Save in <see cref="IsNew"/> mode, with the
    /// created domain's name. The owner refreshes the tree, closes the New tab, and
    /// reopens the real domain.</summary>
    public event Action<string?>? DomainCreated;

    public bool CanSave => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => ExecuteSaveAsync();

    /// <summary>
    /// Builds the DDL for the current form vs. the loaded baseline and runs it.
    /// New domain → CREATE DOMAIN (+ COMMENT). Existing → the minimum set of
    /// ALTER DOMAIN (default / check) + COMMENT statements (empty → no-op). Runs
    /// through <see cref="FirebirdDdlExecutor"/> (autonomous, auto-committed).
    /// </summary>
    public async Task ExecuteSaveAsync(CancellationToken cancellationToken = default)
    {
        if (_ddlExecutor is null) return;
        ErrorMessage = null;

        string sql;
        try
        {
            sql = BuildSaveSql();
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
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.DomainSaveFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.DomainSaveFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            DomainCreated?.Invoke(EditableName.Trim());
            return;
        }

        // Existing domain: re-read so values + DDL + baselines refresh and dirty clears.
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Builds the Save DDL. Pure + internal so tests can assert the shape
    /// without a database. Returns an empty string when an existing domain has no
    /// changes (no-op).</summary>
    internal string BuildSaveSql()
    {
        var statements = new List<string>();
        if (IsNew)
        {
            var info = BuildCurrentDomainInfo();
            statements.Add(DdlGenerator.BuildCreateDomain(info));
            if (!string.IsNullOrWhiteSpace(EditableDescription))
            {
                statements.Add(DdlGenerator.BuildCommentDomain(info.Name, EditableDescription));
            }
        }
        else
        {
            var name = DomainName;

            // Default: SET DEFAULT both adds and replaces; DROP DEFAULT only when we
            // had one and the user cleared it.
            var newDefault = (DefaultValue ?? string.Empty).Trim();
            var oldDefault = (_baselineDefault ?? string.Empty).Trim();
            if (!string.Equals(newDefault, oldDefault, StringComparison.Ordinal))
            {
                statements.Add(newDefault.Length == 0
                    ? DdlGenerator.BuildAlterDomainDropDefault(name)
                    : DdlGenerator.BuildAlterDomainSetDefault(name, newDefault));
            }

            // Check: a domain has at most one CHECK. Change = DROP CONSTRAINT (if one
            // existed) then ADD CHECK (if a new one is supplied).
            var newCheck = (CheckConstraint ?? string.Empty).Trim();
            var oldCheck = (_baselineCheck ?? string.Empty).Trim();
            if (!string.Equals(newCheck, oldCheck, StringComparison.Ordinal))
            {
                if (oldCheck.Length > 0) statements.Add(DdlGenerator.BuildAlterDomainDropConstraint(name));
                if (newCheck.Length > 0) statements.Add(DdlGenerator.BuildAlterDomainAddCheck(name, newCheck));
            }

            // Description.
            var desc = EditableDescription ?? string.Empty;
            if (!string.Equals(desc, _baselineDescription, StringComparison.Ordinal))
            {
                statements.Add(DdlGenerator.BuildCommentDomain(name, string.IsNullOrWhiteSpace(desc) ? null : desc));
            }
        }

        return string.Join(";\n", statements);
    }

    // Snapshots the live form into a DomainInfo — for the New-flow CREATE and the
    // DDL preview. Single source so the DDL tab matches what Save executes.
    private DomainInfo BuildCurrentDomainInfo() => new()
    {
        Name = (IsNew ? EditableName : DomainName).Trim(),
        DataType = DataType,
        Length = AsInt(Length),
        Precision = AsInt(Precision),
        Scale = AsInt(Scale),
        SubType = AsInt(SubType),
        CharacterSet = string.IsNullOrWhiteSpace(CharacterSet) ? null : CharacterSet,
        Collation = string.IsNullOrWhiteSpace(Collation) ? null : Collation,
        DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
        CheckConstraint = string.IsNullOrWhiteSpace(CheckConstraint) ? null : CheckConstraint,
        NotNull = NotNull,
        Description = EditableDescription,
    };

    private static int? AsInt(decimal? value) => value is { } v ? (int)v : null;

    // Recompute the DDL-tab text from the live form. Guarded: a New domain with no
    // name yet has nothing to preview (BuildCreateDomain requires a name).
    private void RefreshDdl()
    {
        var name = (IsNew ? EditableName : DomainName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) { DdlText = string.Empty; return; }
        try { DdlText = DdlGenerator.BuildCreateDomain(BuildCurrentDomainInfo()); }
        catch (ArgumentException) { DdlText = string.Empty; }
    }

    // ─── Revert (discard edits, reload from DB) ───────────────────────────

    public bool CanRevertChanges => IsDirty && !IsNew;

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private async Task RevertChanges()
    {
        if (!CanRevertChanges) return;
        var name = string.IsNullOrWhiteSpace(EditableName) ? DomainName : EditableName.Trim();
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

    // ─── Delete (DROP DOMAIN) ─────────────────────────────────────────────

    /// <summary>Raised after a confirmed Delete. The owner runs DROP DOMAIN, closes
    /// this tab, and refreshes the metadata tree.</summary>
    public event Func<DomainDetailTabViewModel, Task>? DeleteRequested;

    public bool CanDelete => _ddlExecutor is not null && !IsNew;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (!CanDelete) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.DomainDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.DomainDeleteConfirmFormat, DomainName),
            ConfirmLabel = UiStrings.DomainDeleteConfirmYes,
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
        // New domain: nothing to load until it's created.
        if (IsNew) return;
        if (_reader is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _suppressDirty = true;
        try
        {
            await SafeLoadAsync(async () =>
            {
                var info = await _reader.GetDomainInfoAsync(DomainName, cancellationToken).ConfigureAwait(true);
                EditableName = info.Name;
                DataType = info.DataType;
                Length = info.Length;
                Precision = info.Precision;
                Scale = info.Scale;
                SubType = info.SubType;
                CharacterSet = info.CharacterSet;
                Collation = info.Collation;
                NotNull = info.NotNull;
                DefaultValue = info.DefaultValue;
                CheckConstraint = info.CheckConstraint;
                Description = info.Description;
                _baselineDefault = info.DefaultValue;
                _baselineCheck = info.CheckConstraint;
                _baselineDescription = info.Description;
                DdlText = DdlGenerator.BuildCreateDomain(info);
            });

            await SafeLoadAsync(async () =>
            {
                var usage = await _reader.GetDomainUsageAsync(DomainName, cancellationToken).ConfigureAwait(true);
                UsedByTree.Clear();
                foreach (var g in TableDetailTabViewModel.BuildDependencyTree(usage)) UsedByTree.Add(g);
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
