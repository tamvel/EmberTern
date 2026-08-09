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
/// Editable scope on an EXISTING domain mirrors what Firebird's ALTER DOMAIN actually
/// supports (re-verified on FB 5.0.3): type / length / precision / scale / sub-type
/// (TYPE), character set for char types (TYPE … CHARACTER SET), NOT NULL (SET/DROP NOT
/// NULL), DEFAULT, CHECK, name (rename via TO), and description. ONLY the collation is
/// read-only after create — Firebird has no ALTER syntax for it (SQL error -104). The
/// "Compile" button assembles the minimum-set of ALTER statements for what changed; a
/// rename closes + reopens the tab under the new name.
/// </summary>
public partial class DomainDetailTabViewModel : ViewModelBase, IUnsavedWorkSource, ISavableObjectEditor, IDependencyNavigator
{
    private readonly FirebirdTableDetailReader? _reader;
    private readonly FirebirdDdlExecutor? _ddlExecutor;
    private Task? _loadTask;

    // Baseline (last-loaded / last-saved) snapshot, used by Compile to emit the
    // minimum set of ALTER DOMAIN / COMMENT statements for what actually changed.
    // Null until the first load (a unit-constructed VM with no reader) — in that
    // case type/not-null/rename diffs are skipped (we can't know the prior shape),
    // and default/check/description compare against empty.
    private DomainInfo? _baselineInfo;

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
    /// DDL tabs stay empty until the first successful Compile, after which the owner
    /// reopens the real domain. Compile in this mode raises <see cref="DomainCreated"/>.</summary>
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

    // Type-definition fields. On an EXISTING domain these map to ALTER DOMAIN:
    //   type/length/precision/scale/sub-type → TYPE … ; charset → TYPE … CHARACTER SET
    //   (char types only); NOT NULL → SET/DROP NOT NULL. All editable (Firebird
    //   supports them — verified on FB5). Collation is the ONE exception: no ALTER
    //   syntax exists, so it's editable only while creating.
    [ObservableProperty] private string _dataType = "VARCHAR";
    [ObservableProperty] private decimal? _length;
    [ObservableProperty] private decimal? _precision;
    [ObservableProperty] private decimal? _scale;
    [ObservableProperty] private decimal? _subType;
    [ObservableProperty] private string? _characterSet;
    [ObservableProperty] private string? _collation;
    [ObservableProperty] private bool _notNull;

    partial void OnDataTypeChanged(string value)
    {
        MarkDirty();
        RefreshDdl();
        // Charset/collation editability depends on whether the selected type is a
        // character type.
        OnPropertyChanged(nameof(IsCharTypeSelected));
        OnPropertyChanged(nameof(IsCharsetEditable));
        OnPropertyChanged(nameof(IsCollationEditable));
    }
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
    partial void OnEditableDescriptionChanged(string value) { MarkDirty(); RefreshDdl(); }

    public ObservableCollection<DependencyGroupNode> UsedByTree { get; }

    /// <summary>Read-only reconstructed DDL (CREATE DOMAIN …) — the DDL tab. Built
    /// from the live form state via <see cref="DdlGenerator.BuildCreateDomain"/>, so
    /// it mirrors exactly what Compile (in the New flow) would execute.</summary>
    [ObservableProperty]
    private string _ddlText = string.Empty;

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Type / length / precision / scale / sub-type / NOT NULL are editable on
    /// BOTH new and existing domains (existing → ALTER DOMAIN TYPE / SET-DROP NOT NULL).</summary>
    public bool IsTypeEditable => true;

    /// <summary>True when the selected data type is a character type (CHAR / VARCHAR /
    /// CSTRING) — gates the charset/collation fields.</summary>
    public bool IsCharTypeSelected
    {
        get
        {
            var t = (DataType ?? string.Empty).Trim().ToUpperInvariant();
            return t is "CHAR" or "VARCHAR" or "CSTRING";
        }
    }

    /// <summary>Character set is editable for char types on both new and existing
    /// domains (existing → ALTER DOMAIN TYPE … CHARACTER SET …, verified on FB5).</summary>
    public bool IsCharsetEditable => IsCharTypeSelected;

    /// <summary>Collation is settable only at CREATE (char types). Firebird has NO
    /// ALTER syntax for a domain's collation (SQL error -104), so it's read-only on an
    /// existing domain.</summary>
    public bool IsCollationEditable => IsNew && IsCharTypeSelected;

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

    // ─── Compile ──────────────────────────────────────────────────────────────

    /// <summary>Raised after a successful Compile in <see cref="IsNew"/> mode, with the
    /// created domain's name. The owner refreshes the tree, closes the New tab, and
    /// reopens the real domain.</summary>
    public event Action<string?>? DomainCreated;

    /// <summary>Raised after an EXISTING domain is renamed (ALTER DOMAIN … TO …), with
    /// the new name. The owner refreshes the tree, closes this tab, and reopens the
    /// domain under its new name (the tab is keyed on the old name, now gone).</summary>
    public event Action<string>? RenameReopenRequested;

    public bool CanCompile => _ddlExecutor is not null;

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => ExecuteCompileAsync();

    /// <summary>
    /// Builds the DDL for the current form vs. the loaded baseline and runs it.
    /// New domain → CREATE DOMAIN (+ COMMENT). Existing → the minimum set of
    /// ALTER DOMAIN statements (type / not-null / default / check / rename) + COMMENT
    /// (empty → no-op). Runs through <see cref="FirebirdDdlExecutor"/> (autonomous,
    /// auto-committed). A rename closes + reopens the tab under the new name.
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
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.DomainCompileFailedFormat, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.DomainCompileFailedFormat, ex.Message);
            return;
        }

        if (IsNew)
        {
            DomainCreated?.Invoke(EditableName.Trim());
            return;
        }

        // Existing domain. A rename (ALTER DOMAIN … TO …) changes the object's key, so
        // the owner must reopen the tab under the new name; otherwise just re-read.
        var newName = (EditableName ?? string.Empty).Trim();
        if (newName.Length > 0 && !string.Equals(newName, DomainName, StringComparison.Ordinal))
        {
            RenameReopenRequested?.Invoke(newName);
            return;
        }

        // Re-read so values + DDL + baseline refresh and dirty clears.
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Builds the Compile DDL. Pure + internal so tests can assert the shape
    /// without a database. Returns an empty string when an existing domain has no
    /// changes (no-op).</summary>
    internal string BuildCompileSql()
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
            var name = DomainName; // all non-rename ALTERs reference the existing name

            // Type / charset (TYPE …) and NOT NULL diffs need a loaded baseline — a
            // unit-constructed VM (no reader) skips them rather than fabricate a change.
            if (_baselineInfo is not null)
            {
                var current = BuildCurrentDomainInfo();
                if (!string.Equals(
                        DdlGenerator.ComposeDomainTypeWithCharset(current),
                        DdlGenerator.ComposeDomainTypeWithCharset(_baselineInfo),
                        StringComparison.Ordinal))
                {
                    statements.Add(DdlGenerator.BuildAlterDomainType(name, current));
                }

                if (NotNull != _baselineInfo.NotNull)
                {
                    statements.Add(NotNull
                        ? DdlGenerator.BuildAlterDomainSetNotNull(name)
                        : DdlGenerator.BuildAlterDomainDropNotNull(name));
                }
            }

            // Default: SET DEFAULT both adds and replaces; DROP DEFAULT only when we
            // had one and the user cleared it.
            var newDefault = (DefaultValue ?? string.Empty).Trim();
            var oldDefault = (_baselineInfo?.DefaultValue ?? string.Empty).Trim();
            if (!string.Equals(newDefault, oldDefault, StringComparison.Ordinal))
            {
                statements.Add(newDefault.Length == 0
                    ? DdlGenerator.BuildAlterDomainDropDefault(name)
                    : DdlGenerator.BuildAlterDomainSetDefault(name, newDefault));
            }

            // Check: a domain has at most one CHECK. Change = DROP CONSTRAINT (if one
            // existed) then ADD CHECK (if a new one is supplied).
            var newCheck = (CheckConstraint ?? string.Empty).Trim();
            var oldCheck = (_baselineInfo?.CheckConstraint ?? string.Empty).Trim();
            if (!string.Equals(newCheck, oldCheck, StringComparison.Ordinal))
            {
                if (oldCheck.Length > 0) statements.Add(DdlGenerator.BuildAlterDomainDropConstraint(name));
                if (newCheck.Length > 0) statements.Add(DdlGenerator.BuildAlterDomainAddCheck(name, newCheck));
            }

            // Description.
            var desc = EditableDescription ?? string.Empty;
            if (!string.Equals(desc, _baselineInfo?.Description ?? string.Empty, StringComparison.Ordinal))
            {
                statements.Add(DdlGenerator.BuildCommentDomain(name, string.IsNullOrWhiteSpace(desc) ? null : desc));
            }

            // Rename LAST — every statement above references the old name; the rename
            // changes it (and triggers the tab reopen).
            if (_baselineInfo is not null)
            {
                var newName = (EditableName ?? string.Empty).Trim();
                if (newName.Length > 0 && !string.Equals(newName, DomainName, StringComparison.Ordinal))
                {
                    statements.Add(DdlGenerator.BuildAlterDomainRename(DomainName, newName));
                }
            }
        }

        return string.Join(";\n", statements);
    }

    // Snapshots the live form into a DomainInfo — for the New-flow CREATE and the
    // DDL preview. Single source so the DDL tab matches what Compile executes.
    private DomainInfo BuildCurrentDomainInfo() => new()
    {
        Name = (string.IsNullOrWhiteSpace(EditableName) ? DomainName : EditableName).Trim(),
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
        try
        {
            // DDL tab == Export: structure (live form) + COMMENT ON DOMAIN, composed through
            // the same PortableDdl the MetadataExportService uses. Live (reflects unsaved
            // edits) yet identical to the exported script at rest.
            var info = BuildCurrentDomainInfo();
            DdlText = PortableDdl.Compose(
                DdlGenerator.BuildCreateDomain(info),
                new[] { PortableDdl.ObjectComment(MetadataObjectKind.Domain, info.Name, info.Description) });
        }
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
                ApplyLoadedInfo(info);
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

    // Mirrors a loaded DomainInfo into the form fields, records it as the baseline
    // (so Compile diffs against it), rebuilds the DDL preview, and refreshes the
    // charset/collation editability gates. Callers must wrap in _suppressDirty.
    private void ApplyLoadedInfo(DomainInfo info)
    {
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
        _baselineInfo = info;
        DdlText = DdlGenerator.BuildCreateDomain(info);
        OnPropertyChanged(nameof(IsCharTypeSelected));
        OnPropertyChanged(nameof(IsCharsetEditable));
        OnPropertyChanged(nameof(IsCollationEditable));
    }

    /// <summary>Test seam: seed a loaded baseline without a live database (LoadAsync
    /// needs a real reader). Applies <paramref name="info"/> to the form + baseline as
    /// a load would, so a subsequent field edit yields the right ALTER diff from
    /// <see cref="BuildCompileSql"/>.</summary>
    internal void SeedBaselineForTest(DomainInfo info)
    {
        _suppressDirty = true;
        try { ApplyLoadedInfo(info); }
        finally { _suppressDirty = false; ClearDirty(); }
    }
}
