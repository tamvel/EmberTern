using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>One selectable source field in a mapping row's picker. <c>null</c> <see cref="Field"/> = "do not
/// import" — a deliberate skip, which is a decision and not an absence.</summary>
public sealed record ImportSourceFieldOption(string Display, SourceField? Field)
{
    public int Index => Field?.Index ?? -1;
}

/// <summary>
/// One row of the mapping grid: a TARGET column and the source field feeding it.
/// <para>
/// ⭐ <b>Orientation is target → source</b> (§3.5), because the target column is the side with requirements —
/// NOT NULL, a type, a length. The source field is the choice.
/// </para>
/// <para>
/// A row decides nothing. Whether a column can be written, why it cannot, and what the mapping means are all
/// computed by <see cref="ImportMappingPlanner"/> and <see cref="ImportTargetType"/>; this turns those facts
/// into text and a picker.
/// </para>
/// </summary>
public sealed partial class ImportMappingRowViewModel : ViewModelBase
{
    private readonly Action<ImportMappingRowViewModel>? _onChanged;
    private bool _suspend;

    public ImportMappingRowViewModel(
        ColumnSpec column,
        ColumnMapping mapping,
        IReadOnlyList<ImportSourceFieldOption> options,
        Action<ImportMappingRowViewModel>? onChanged)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
        _onChanged = onChanged;

        TargetColumnName = column.Name;
        TypeText = column.Type;
        IsRequired = column.NotNull && string.IsNullOrWhiteSpace(column.DefaultValue);

        var targetType = ImportTargetType.Resolve(column);
        NeverWritable = ImportTarget.IsNeverWritable(column);
        UnsupportedType = !targetType.IsSupported;
        NeedsIdentityOverride = ImportTarget.RequiresOverridingSystemValue(column);

        LockReason = NeverWritable ? UiStrings.ImportMappingLockedComputed
            : UnsupportedType ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportMappingLockedUnsupportedFormat, column.Type)
            : NeedsIdentityOverride ? UiStrings.ImportMappingLockedIdentity
            : string.Empty;

        Options = options;
        _isSkipped = mapping.IsSkipped;
        _selectedOption = options.FirstOrDefault(o => o.Index == mapping.SourceFieldIndex) ?? options[0];
        Origin = mapping.Origin;

        // An identity ALWAYS column that a restored configuration already maps is shown unlocked: the
        // decision was made once and the writer emits OVERRIDING SYSTEM VALUE for it (R10).
        _isIdentityUnlocked = NeedsIdentityOverride && mapping.IsMapped;
    }

    public ColumnSpec Column { get; }

    public string TargetColumnName { get; }

    public string TypeText { get; }

    /// <summary>NOT NULL without a DEFAULT — leaving it unmapped blocks the import, not merely warns.</summary>
    public bool IsRequired { get; }

    /// <summary><c>COMPUTED BY</c>: Firebird rejects an INSERT naming it, at any time, for any user.</summary>
    public bool NeverWritable { get; }

    /// <summary>A type this module cannot write — the same Unknown set the export side refuses to read.</summary>
    public bool UnsupportedType { get; }

    /// <summary>Identity <c>GENERATED ALWAYS</c>: writable, but only after a deliberate unlock.</summary>
    public bool NeedsIdentityOverride { get; }

    /// <summary>Why the picker is disabled, in the user's words. A blocked control with no reason is a UX
    /// defect (§9.1 point 3) — so a locked column is shown WITH its reason, never hidden.</summary>
    public string LockReason { get; }

    public IReadOnlyList<ImportSourceFieldOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapped))]
    private ImportSourceFieldOption _selectedOption;

    partial void OnSelectedOptionChanged(ImportSourceFieldOption value)
    {
        if (_suspend) return;

        // Any manual edit clears an automatic origin, so a marker can never describe a value the user has
        // since replaced — the ValueOrigin rule the debugger's launch configuration established (C3).
        Origin = MappingOrigin.Manual;
        IsSkipped = value.Field is null;
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(IsAssumed));
        OnPropertyChanged(nameof(IsAutomatic));
        _onChanged?.Invoke(this);
    }

    [ObservableProperty] private bool _isSkipped;

    /// <summary>
    /// Unlocks an identity <c>GENERATED ALWAYS</c> column for mapping. Off by default: Firebird refuses an
    /// INSERT that names one without <c>OVERRIDING SYSTEM VALUE</c>, and supplying that silently would decide
    /// on the user's behalf that the server's generated identity should be overwritten.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickerEnabled))]
    private bool _isIdentityUnlocked;

    partial void OnIsIdentityUnlockedChanged(bool value)
    {
        if (_suspend) return;
        if (!value && SelectedOption.Field is not null) SelectedOption = Options[0];
        _onChanged?.Invoke(this);
    }

    public MappingOrigin Origin { get; private set; }

    public bool IsMapped => !IsSkipped && SelectedOption.Field is not null;

    /// <summary>A locked column's picker stays visible and disabled — the user can see what it would map to,
    /// and why they cannot.</summary>
    public bool IsPickerEnabled
        => !NeverWritable && !UnsupportedType && (!NeedsIdentityOverride || IsIdentityUnlocked);

    // ── Origin rendering (§9.3, the debugger's ValueOrigin vocabulary) ──────────────────────────────────

    /// <summary>Matched on a provable fact (equal names) — quiet italic; it needs no attention.</summary>
    public bool IsAutomatic => Origin == MappingOrigin.Restored;

    /// <summary>Paired by the sole-remaining-pair rule — an ACCENT, not a warning: "worth a look", not
    /// "wrong". It rests on position rather than identity, which is exactly what the user should check.</summary>
    public bool IsAssumed => Origin == MappingOrigin.Assumed;

    public string OriginText => Origin switch
    {
        MappingOrigin.Assumed => UiStrings.ImportMappingOriginAssumed,
        MappingOrigin.Restored => UiStrings.ImportMappingOriginMatched,
        _ => string.Empty,
    };

    /// <summary>What the planner said about this column, already a sentence.</summary>
    [ObservableProperty] private string _diagnosticText = string.Empty;

    [ObservableProperty] private string _diagnosticBrushKey = string.Empty;

    public bool HasDiagnostic => DiagnosticText.Length > 0;

    partial void OnDiagnosticTextChanged(string value) => OnPropertyChanged(nameof(HasDiagnostic));

    /// <summary>Writes the planner's own decision back into the row without it counting as a user edit.</summary>
    internal void AdoptPlan(ColumnMapping mapping)
    {
        _suspend = true;
        try
        {
            SelectedOption = Options.FirstOrDefault(o => o.Index == mapping.SourceFieldIndex) ?? Options[0];
            IsSkipped = mapping.IsSkipped;
            Origin = mapping.Origin;
            if (NeedsIdentityOverride) IsIdentityUnlocked = mapping.IsMapped;
        }
        finally
        {
            _suspend = false;
        }

        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(IsAssumed));
        OnPropertyChanged(nameof(IsAutomatic));
        OnPropertyChanged(nameof(IsMapped));
    }

    /// <summary>The row as the ONE record sees it (§4.8.6).</summary>
    public ColumnMapping ToMapping()
    {
        if (IsSkipped || SelectedOption.Field is null)
        {
            return IsSkipped
                ? ColumnMapping.Skipped(TargetColumnName)
                : ColumnMapping.Unmapped(TargetColumnName);
        }

        return new ColumnMapping
        {
            TargetColumnName = TargetColumnName,
            SourceFieldName = SelectedOption.Field.HasRealName ? SelectedOption.Field.Name : null,
            SourceFieldIndex = SelectedOption.Field.Index,
            Origin = Origin == MappingOrigin.Unmapped ? MappingOrigin.Manual : Origin,
        };
    }
}

/// <summary>
/// The <b>Mapping</b> panel (§3.5) — which source field feeds which target column, and what is wrong with
/// that.
/// <para>
/// ⭐ It is a <b>view of <see cref="ImportMappingPlanner"/></b>, not a second opinion. Auto-matching, the
/// sole-remaining-pair rule, which columns can never be written and which findings are worth raising are all
/// computed in Core; this projects them and hands edits back. A second matching rule here is exactly how the
/// grid and the readiness strip would start telling the user different things.
/// </para>
/// </summary>
public sealed partial class ImportMappingPanelViewModel : ViewModelBase
{
    private IReadOnlyList<ImportSourceFieldOption> _options = Array.Empty<ImportSourceFieldOption>();

    public ImportMappingPanelViewModel()
    {
        Rows = new ObservableCollection<ImportMappingRowViewModel>();
        VisibleRows = new ObservableCollection<ImportMappingRowViewModel>();
    }

    /// <summary>Raised when the user changed a mapping, so the coordinator re-runs the chain (§4.7).</summary>
    public event EventHandler? Changed;

    /// <summary>Asks the coordinator to re-plan from scratch with a different strategy.</summary>
    public event EventHandler<ImportMappingStrategy>? StrategyRequested;

    /// <summary>Every target column, in catalog order — including the ones that can never be written, so the
    /// grid is a direct projection of the table and a blocked column is shown WITH its reason (§3.5).</summary>
    public ObservableCollection<ImportMappingRowViewModel> Rows { get; }

    /// <summary>What the grid renders: <see cref="Rows"/>, or only the unmapped ones when filtered.</summary>
    public ObservableCollection<ImportMappingRowViewModel> VisibleRows { get; }

    [ObservableProperty] private bool _hasTarget;

    /// <summary>"Matched 3 of 4 columns by name." — numbers, never adjectives (§9.1 point 4).</summary>
    [ObservableProperty] private string _headline = string.Empty;

    /// <summary>Source fields nobody consumes. Listing them is how "I forgot a column" becomes visible
    /// BEFORE the import rather than after it.</summary>
    [ObservableProperty] private string _unusedFieldsText = string.Empty;

    public bool HasUnusedFields => UnusedFieldsText.Length > 0;

    partial void OnUnusedFieldsTextChanged(string value) => OnPropertyChanged(nameof(HasUnusedFields));

    [ObservableProperty] private bool _showOnlyUnmapped;

    partial void OnShowOnlyUnmappedChanged(bool value) => PublishVisibleRows();

    [RelayCommand]
    private void MatchByPosition() => StrategyRequested?.Invoke(this, ImportMappingStrategy.ByPosition);

    [RelayCommand]
    private void ClearMapping() => StrategyRequested?.Invoke(this, ImportMappingStrategy.Clear);

    /// <summary>
    /// Rebuilds the grid from a plan. Rows are REPLACED rather than patched because the target's column set
    /// is what defines them — a different table is a different grid.
    /// </summary>
    public void Update(ImportTarget? target, SourceSchema? schema, ImportMappingPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        HasTarget = target is not null && target.Columns.Count > 0;
        if (!HasTarget)
        {
            Rows.Clear();
            VisibleRows.Clear();
            Headline = string.Empty;
            UnusedFieldsText = string.Empty;
            return;
        }

        _options = BuildOptions(schema);

        var byColumn = plan.Mapping.ToDictionary(m => m.TargetColumnName, StringComparer.OrdinalIgnoreCase);

        Rows.Clear();
        foreach (var column in target!.Columns)
        {
            var mapping = byColumn.TryGetValue(column.Name, out var found)
                ? found
                : ColumnMapping.Unmapped(column.Name);

            Rows.Add(new ImportMappingRowViewModel(column, mapping, _options, OnRowChanged));
        }

        ApplyDiagnostics(plan.Diagnostics);
        PublishVisibleRows();

        var mapped = Rows.Count(r => r.IsMapped);
        var mappable = Rows.Count(r => r.IsPickerEnabled || r.IsMapped);
        Headline = string.Format(CultureInfo.CurrentCulture, UiStrings.ImportMappingHeadlineFormat, mapped, mappable);

        UnusedFieldsText = plan.UnusedSourceFields.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportMappingUnusedFieldsFormat,
                string.Join(", ", plan.UnusedSourceFields.Select(f => f.Name)));
    }

    /// <summary>The grid as the ONE record sees it (§4.8.6).</summary>
    /// <summary>
    /// The row the readiness strip is complaining about — what the Mapping chip should take the user to.
    /// <para>
    /// Ordered by how badly the row needs a decision: a <b>required</b> column with nothing mapped BLOCKS the
    /// import, so it comes first; then anything the planner flagged; then merely unmapped. Rows the user can
    /// do nothing about (computed, unsupported) are skipped — sending someone to a locked control is worse
    /// than sending them nowhere.
    /// </para>
    /// </summary>
    public ImportMappingRowViewModel? FirstRowNeedingAttention()
    {
        ImportMappingRowViewModel? flagged = null;
        ImportMappingRowViewModel? unmapped = null;

        foreach (var row in Rows)
        {
            if (!row.IsPickerEnabled) continue;

            if (row.IsRequired && !row.IsMapped) return row;
            if (flagged is null && row.HasDiagnostic) flagged = row;
            if (unmapped is null && !row.IsMapped) unmapped = row;
        }

        return flagged ?? unmapped;
    }

    public IReadOnlyList<ColumnMapping> BuildMapping()
        => Rows.Select(r => r.ToMapping()).ToList();

    /// <summary>Applies a re-plan to the existing rows without rebuilding them, so the grid does not jump
    /// under the user when a strategy button is pressed.</summary>
    public void AdoptPlan(ImportMappingPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var byColumn = plan.Mapping.ToDictionary(m => m.TargetColumnName, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            if (byColumn.TryGetValue(row.TargetColumnName, out var mapping)) row.AdoptPlan(mapping);
        }

        ApplyDiagnostics(plan.Diagnostics);
        PublishVisibleRows();

        var mapped = Rows.Count(r => r.IsMapped);
        var mappable = Rows.Count(r => r.IsPickerEnabled || r.IsMapped);
        Headline = string.Format(CultureInfo.CurrentCulture, UiStrings.ImportMappingHeadlineFormat, mapped, mappable);

        UnusedFieldsText = plan.UnusedSourceFields.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportMappingUnusedFieldsFormat,
                string.Join(", ", plan.UnusedSourceFields.Select(f => f.Name)));
    }

    /// <summary>
    /// Hangs each per-column finding on its row.
    /// <para>
    /// Only column-scoped codes land here; the counting ones ("3 columns unmapped") belong to the readiness
    /// strip, which already says them once. Repeating a finding in two places is how two places start
    /// disagreeing.
    /// </para>
    /// </summary>
    private void ApplyDiagnostics(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        foreach (var row in Rows)
        {
            row.DiagnosticText = string.Empty;
            row.DiagnosticBrushKey = string.Empty;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (string.IsNullOrEmpty(diagnostic.Subject)) continue;

            var row = Rows.FirstOrDefault(
                r => string.Equals(r.TargetColumnName, diagnostic.Subject, StringComparison.OrdinalIgnoreCase));
            if (row is null) continue;

            // The sentence comes from the ONE code→text table the readiness strip uses (rule #6), so a
            // column's note and the strip's line about the same code cannot drift apart.
            row.DiagnosticText = ImportReadinessItemViewModel.Describe(
                new ReadinessItem(
                    diagnostic.Code,
                    diagnostic.Severity,
                    IsBlocking: diagnostic.Severity == ImportSeverity.Error,
                    Section: ImportSection.Mapping,
                    Subject: diagnostic.Subject,
                    Count: diagnostic.Count));
            row.DiagnosticBrushKey = Controls.MessageBanner.BrushKeyFor(
                ImportReadinessItemViewModel.ToBannerSeverity(diagnostic.Severity));
        }
    }

    private void PublishVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var row in Rows)
        {
            if (ShowOnlyUnmapped && row.IsMapped) continue;
            VisibleRows.Add(row);
        }
    }

    private void OnRowChanged(ImportMappingRowViewModel row)
    {
        PublishVisibleRows();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The picker's contents: "do not import" first, then every source field.
    /// <para>
    /// A field with a real header shows its own spelling — that is what the user will look for. A headerless
    /// source shows the positional label it already sees in the source preview and in Excel.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ImportSourceFieldOption> BuildOptions(SourceSchema? schema)
    {
        var options = new List<ImportSourceFieldOption>(1 + (schema?.Fields.Count ?? 0))
        {
            new(UiStrings.ImportMappingDoNotImport, null),
        };

        if (schema is null) return options;

        foreach (var field in schema.Fields)
        {
            var label = field.HasRealName
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.ImportMappingFieldLabelFormat,
                    SourceField.PositionalName(field.Index),
                    field.Name)
                : field.Name;

            options.Add(new ImportSourceFieldOption(label, field));
        }

        return options;
    }
}

/// <summary>How the coordinator should re-plan the mapping when a strategy button is pressed.</summary>
public enum ImportMappingStrategy
{
    /// <summary>Pair column i with field i — the fallback for a source whose names say nothing.</summary>
    ByPosition,

    /// <summary>Unmap everything and start again.</summary>
    Clear,
}
