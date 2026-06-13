using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Per-field checkbox row inside the FK dialog's source/target ListBoxes.
/// IsSelected is two-way bound to the row's CheckBox; the dialog VM watches
/// PropertyChanged on the bag to re-evaluate auto-mapping and DDL preview.
/// </summary>
public partial class SelectableFieldViewModel : ObservableObject
{
    public SelectableFieldViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Wraps a <see cref="ForeignKeyAction"/> with its UI label. The dialog
/// binds OnUpdate / OnDelete ComboBoxes to lists of these so we don't need
/// a value converter for each action's display string.
/// </summary>
public sealed record NamedForeignKeyAction(ForeignKeyAction Action, string Label);

/// <summary>
/// Drives the Foreign Key wizard. Mirrors AddFieldDialog's contract:
/// every observable property re-notifies <see cref="DdlPreview"/>, the
/// dialog opens, user fills in the form, OK / Cancel commands close it
/// returning a <see cref="ForeignKeySpec"/> or null.
///
/// Three callbacks supplied by the View:
///   - <see cref="LoadReferencedFieldsAsync"/> — given a table name,
///     returns its columns in declaration order (ListColumnsAsync).
///   - <see cref="LoadReferencedPrimaryKeyAsync"/> — given a table name,
///     returns its PK column names in PK declaration order (constraints
///     filter on PRIMARY KEY type). Empty list when the table has no PK.
/// Both are best-effort: dialog still works with empty fields (user picks
/// manually) but auto-mapping degrades to stage 3 (no proposal).
/// </summary>
public partial class ForeignKeyDialogViewModel : ViewModelBase
{
    public ForeignKeyDialogViewModel(
        string sourceTableName,
        IReadOnlyList<string> sourceFields,
        IReadOnlyList<string> availableTables,
        Func<string, Task<IReadOnlyList<string>>> loadReferencedFieldsAsync,
        Func<string, Task<IReadOnlyList<string>>> loadReferencedPrimaryKeyAsync)
    {
        SourceTableName = sourceTableName ?? string.Empty;
        SourceFields = new ObservableCollection<SelectableFieldViewModel>(
            (sourceFields ?? Array.Empty<string>()).Select(n => new SelectableFieldViewModel(n)));
        ReferencedFields = new ObservableCollection<SelectableFieldViewModel>();
        AvailableTables = new ObservableCollection<string>(availableTables ?? Array.Empty<string>());
        LoadReferencedFieldsAsync = loadReferencedFieldsAsync;
        LoadReferencedPrimaryKeyAsync = loadReferencedPrimaryKeyAsync;
        AvailableActions = new[]
        {
            new NamedForeignKeyAction(ForeignKeyAction.NoAction, UiStrings.ForeignKeyActionNoAction),
            new NamedForeignKeyAction(ForeignKeyAction.Cascade, UiStrings.ForeignKeyActionCascade),
            new NamedForeignKeyAction(ForeignKeyAction.SetNull, UiStrings.ForeignKeyActionSetNull),
        };
        _onUpdateAction = AvailableActions[0];
        _onDeleteAction = AvailableActions[0];

        SourceFields.CollectionChanged += OnFieldsCollectionChanged;
        ReferencedFields.CollectionChanged += OnFieldsCollectionChanged;
        WireFieldPropertyChanged(SourceFields);
        WireFieldPropertyChanged(ReferencedFields);
    }

    public string SourceTableName { get; }
    public ObservableCollection<SelectableFieldViewModel> SourceFields { get; }
    public ObservableCollection<string> AvailableTables { get; }
    public ObservableCollection<SelectableFieldViewModel> ReferencedFields { get; }

    public Func<string, Task<IReadOnlyList<string>>> LoadReferencedFieldsAsync { get; }
    public Func<string, Task<IReadOnlyList<string>>> LoadReferencedPrimaryKeyAsync { get; }

    public IReadOnlyList<NamedForeignKeyAction> AvailableActions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _constraintName = string.Empty;

    // Re-entrancy guard for ConstraintName auto-derivation. When the user
    // edits the field manually we lock to that value; the auto-derive only
    // fills the field when it's still empty OR still matches a previous
    // auto-derived value.
    private string _lastAutoDerivedName = string.Empty;
    private bool _coercingName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string? _selectedReferencedTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private NamedForeignKeyAction _onUpdateAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private NamedForeignKeyAction _onDeleteAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>Live-preview DDL. Re-evaluates on every form-state change.
    /// Falls back to a stub when validation prevents emitting real DDL —
    /// the preview tab still shows something meaningful while the user
    /// fills the form.</summary>
    public string DdlPreview
    {
        get
        {
            var spec = TryBuildPreviewSpec();
            if (spec is null) return UiStrings.ForeignKeyDdlPreviewIncomplete;
            try
            {
                return DdlGenerator.BuildAddForeignKey(SourceTableName, spec);
            }
            catch (ArgumentException)
            {
                // Spec failed validation in the emitter (count mismatch, empty
                // list, etc.). Keep the dialog open with the hint.
                return UiStrings.ForeignKeyDdlPreviewIncomplete;
            }
        }
    }

    private ForeignKeySpec? TryBuildPreviewSpec()
    {
        if (string.IsNullOrWhiteSpace(SelectedReferencedTable)) return null;
        var local = GetSelectedNames(SourceFields);
        var refs = GetSelectedNames(ReferencedFields);
        if (local.Count == 0 || refs.Count == 0) return null;
        return new ForeignKeySpec
        {
            ConstraintName = string.IsNullOrWhiteSpace(ConstraintName) ? "?" : ConstraintName.Trim(),
            LocalFields = local,
            ReferencedTable = SelectedReferencedTable!.Trim(),
            ReferencedFields = refs,
            OnUpdate = OnUpdateAction.Action,
            OnDelete = OnDeleteAction.Action,
        };
    }

    /// <summary>True when the form is complete enough to convert to a
    /// <see cref="ForeignKeySpec"/>. Mirrors the spec validation list:
    /// (1) constraint name, (2) at least one local field, (3) referenced
    /// table, (4) at least one referenced field, (5) counts match.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ConstraintName))
        {
            ValidationMessage = UiStrings.ForeignKeyValidationConstraintNameRequired;
            return false;
        }
        if (string.IsNullOrWhiteSpace(SelectedReferencedTable))
        {
            ValidationMessage = UiStrings.ForeignKeyValidationReferencedTableRequired;
            return false;
        }
        var local = GetSelectedNames(SourceFields);
        if (local.Count == 0)
        {
            ValidationMessage = UiStrings.ForeignKeyValidationLocalFieldsRequired;
            return false;
        }
        var refs = GetSelectedNames(ReferencedFields);
        if (refs.Count == 0)
        {
            ValidationMessage = UiStrings.ForeignKeyValidationReferencedFieldsRequired;
            return false;
        }
        if (local.Count != refs.Count)
        {
            ValidationMessage = UiStrings.ForeignKeyValidationFieldCountMismatch;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    public ForeignKeySpec BuildSpec() => new()
    {
        ConstraintName = ConstraintName.Trim(),
        LocalFields = GetSelectedNames(SourceFields),
        ReferencedTable = SelectedReferencedTable?.Trim() ?? string.Empty,
        ReferencedFields = GetSelectedNames(ReferencedFields),
        OnUpdate = OnUpdateAction.Action,
        OnDelete = OnDeleteAction.Action,
    };

    /// <summary>Field-selection state changes (CheckBox toggles or
    /// referenced-field list re-population) trickle through here. We
    /// re-derive the default constraint name (if the user hasn't pinned
    /// it) and re-notify <see cref="DdlPreview"/>.</summary>
    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WireFieldPropertyChanged(sender as ObservableCollection<SelectableFieldViewModel>);
        OnPropertyChanged(nameof(DdlPreview));
    }

    private void WireFieldPropertyChanged(ObservableCollection<SelectableFieldViewModel>? collection)
    {
        if (collection is null) return;
        foreach (var f in collection)
        {
            f.PropertyChanged -= OnFieldPropertyChanged;
            f.PropertyChanged += OnFieldPropertyChanged;
        }
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableFieldViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(DdlPreview));
        }
    }

    partial void OnSelectedReferencedTableChanged(string? value)
    {
        // Auto-derive the default name (FK_SRC_TGT) every time the target
        // table changes — but only if the user hasn't overridden it. Track
        // the last auto-derived value; if ConstraintName still equals it
        // (or is empty), we can replace it.
        TryAutoDeriveConstraintName();

        // Fetch referenced fields + propose auto-mapping. Fire-and-forget —
        // the await happens inside; any failure leaves ReferencedFields
        // empty and the user picks manually.
        _ = LoadReferencedFieldsForCurrentTableAsync();
    }

    private void TryAutoDeriveConstraintName()
    {
        if (string.IsNullOrWhiteSpace(SelectedReferencedTable)) return;
        var proposed = $"FK_{SourceTableName.Trim().ToUpperInvariant()}_{SelectedReferencedTable.Trim().ToUpperInvariant()}";
        // Replace ONLY when the field is empty or matches a previous auto-derive.
        if (string.IsNullOrWhiteSpace(ConstraintName)
            || string.Equals(ConstraintName, _lastAutoDerivedName, StringComparison.Ordinal))
        {
            _coercingName = true;
            try { ConstraintName = proposed; } finally { _coercingName = false; }
            _lastAutoDerivedName = proposed;
        }
    }

    partial void OnConstraintNameChanged(string value)
    {
        // User edit: stop tracking the auto-derive value so subsequent table
        // changes don't clobber the manual override.
        if (_coercingName) return;
        _lastAutoDerivedName = string.Empty;
    }

    private async Task LoadReferencedFieldsForCurrentTableAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedReferencedTable)) return;
        var tableName = SelectedReferencedTable!;
        IReadOnlyList<string> fields = Array.Empty<string>();
        try
        {
            fields = await LoadReferencedFieldsAsync(tableName).ConfigureAwait(true);
        }
        catch
        {
            // Best effort. Failures (table renamed mid-flight, permission
            // denied, etc.) leave the list empty — user picks manually.
        }

        ReferencedFields.Clear();
        foreach (var name in fields) ReferencedFields.Add(new SelectableFieldViewModel(name));
        WireFieldPropertyChanged(ReferencedFields);
        // CollectionChanged on Add doesn't fire PropertyChanged for DdlPreview
        // until WireFieldPropertyChanged has hooked the inner events — call
        // explicitly to keep the preview in sync.
        OnPropertyChanged(nameof(DdlPreview));

        // Stale-check: user might have changed SelectedReferencedTable while
        // we awaited. Skip the auto-mapping pass in that case — the latest
        // load already kicked off another mapping for the new table.
        if (!string.Equals(SelectedReferencedTable, tableName, StringComparison.Ordinal)) return;

        await RunAutoMappingAsync(tableName).ConfigureAwait(true);
    }

    /// <summary>Auto-mapping pipeline. Stage 1: by name. Stage 2: by
    /// referenced-table PK. Stage 3: no proposal. Only suggests — never
    /// overrides existing user selections that already cover everything.</summary>
    private async Task RunAutoMappingAsync(string referencedTable)
    {
        var localSelected = GetSelectedFields(SourceFields);
        if (localSelected.Count == 0)
        {
            // Nothing to map against; clear any prior auto-pick on the ref side.
            // User will toggle one of the local fields and we'll re-run.
            return;
        }

        // ── Stage 1: by name ────────────────────────────────────────────
        // For each selected local field, look for an identically-named
        // field in the referenced table. If ALL of them match, take that
        // as the proposal. Partial matches fall through to Stage 2 — mixing
        // name-matched and PK-matched in one proposal is more confusing
        // than helpful.
        var refIndex = ReferencedFields.ToDictionary(
            f => f.Name,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var byName = new List<SelectableFieldViewModel>(localSelected.Count);
        foreach (var l in localSelected)
        {
            if (refIndex.TryGetValue(l.Name, out var refField))
            {
                byName.Add(refField);
            }
            else
            {
                byName.Clear();
                break;
            }
        }
        if (byName.Count == localSelected.Count)
        {
            ApplyProposal(byName);
            return;
        }

        // ── Stage 2: by PK ──────────────────────────────────────────────
        // Fetch the PK of the referenced table. If its column count matches
        // the local selection, propose those columns in PK declaration order.
        IReadOnlyList<string> pk = Array.Empty<string>();
        try
        {
            pk = await LoadReferencedPrimaryKeyAsync(referencedTable).ConfigureAwait(true);
        }
        catch { /* best effort */ }

        if (pk.Count > 0 && pk.Count == localSelected.Count)
        {
            var byPk = new List<SelectableFieldViewModel>(pk.Count);
            bool allFound = true;
            foreach (var pkName in pk)
            {
                if (refIndex.TryGetValue(pkName, out var refField))
                {
                    byPk.Add(refField);
                }
                else
                {
                    allFound = false;
                    break;
                }
            }
            if (allFound)
            {
                ApplyProposal(byPk);
                return;
            }
        }

        // ── Stage 3: no proposal ────────────────────────────────────────
        // Leave ReferencedFields untouched. The user will pick manually.
    }

    /// <summary>Toggles ReferencedFields IsSelected to match the proposed
    /// list (clears everything else). Suggestion only — user can change
    /// after we've applied.</summary>
    private void ApplyProposal(IReadOnlyList<SelectableFieldViewModel> proposed)
    {
        var proposedSet = new HashSet<SelectableFieldViewModel>(proposed);
        foreach (var f in ReferencedFields)
        {
            f.IsSelected = proposedSet.Contains(f);
        }
    }

    private static IReadOnlyList<SelectableFieldViewModel> GetSelectedFields(
        ObservableCollection<SelectableFieldViewModel> collection)
    {
        var list = new List<SelectableFieldViewModel>();
        foreach (var f in collection)
        {
            if (f.IsSelected) list.Add(f);
        }
        return list;
    }

    private static IReadOnlyList<string> GetSelectedNames(
        ObservableCollection<SelectableFieldViewModel> collection)
    {
        var list = new List<string>();
        foreach (var f in collection)
        {
            if (f.IsSelected) list.Add(f.Name);
        }
        return list;
    }

    public event Action? RequestClose;
    public ForeignKeySpec? Result { get; private set; }

    [RelayCommand]
    private void Accept()
    {
        if (!IsValid()) return;
        Result = BuildSpec();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
