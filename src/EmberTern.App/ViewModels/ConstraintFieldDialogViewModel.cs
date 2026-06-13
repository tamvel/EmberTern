using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>Which field-list constraint this dialog builds.</summary>
public enum ConstraintFieldKind
{
    PrimaryKey,
    Unique,
}

/// <summary>
/// Result of the field-picker dialog (Primary Key / Unique). The VM hands
/// this to <c>TableDetailTabViewModel.ExecuteAddPrimaryKeyAsync</c> /
/// <c>ExecuteAddUniqueAsync</c>, which emit DDL via <see cref="DdlGenerator"/>.
/// </summary>
public sealed record ConstraintFieldSpec(
    string Name,
    IReadOnlyList<string> Fields,
    string? IndexName = null,
    bool Descending = false);

/// <summary>
/// Drives the Add-Primary-Key / Add-Unique dialog. One VM for both kinds —
/// they differ only in the DDL keyword, default name prefix, and header
/// (per "nie tworzyć drugiego rozwiązania"). Mirrors
/// <see cref="ForeignKeyDialogViewModel"/>'s contract: every observable
/// property re-notifies <see cref="DdlPreview"/>; Accept / Cancel close the
/// dialog returning a <see cref="ConstraintFieldSpec"/> or null. Field
/// selection reuses <see cref="SelectableFieldViewModel"/>.
/// </summary>
public partial class ConstraintFieldDialogViewModel : ViewModelBase
{
    public ConstraintFieldDialogViewModel(
        ConstraintFieldKind kind,
        string tableName,
        IReadOnlyList<string> fields)
    {
        Kind = kind;
        TableName = tableName ?? string.Empty;
        Fields = new ObservableCollection<SelectableFieldViewModel>(
            (fields ?? Array.Empty<string>()).Select(n => new SelectableFieldViewModel(n)));
        Fields.CollectionChanged += OnFieldsCollectionChanged;
        WireFieldPropertyChanged();

        // Seed a default constraint name (PK_<TABLE> / UNQ_<TABLE>). The user
        // can overwrite it freely — no auto-re-derive once the dialog is open.
        ConstraintName = DefaultName();
    }

    public ConstraintFieldKind Kind { get; }
    public string TableName { get; }
    public ObservableCollection<SelectableFieldViewModel> Fields { get; }

    public string DialogTitle => Kind == ConstraintFieldKind.PrimaryKey
        ? UiStrings.PrimaryKeyDialogTitle
        : UiStrings.UniqueDialogTitle;

    public string Header => Kind == ConstraintFieldKind.PrimaryKey
        ? UiStrings.PrimaryKeyDialogHeader
        : UiStrings.UniqueDialogHeader;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _constraintName = string.Empty;

    // Optional backing-index configuration (Firebird's USING [ASC|DESC] INDEX
    // clause on PK / UNIQUE). The Ograniczenia grid already SHOWS index name +
    // sort, so the dialog lets the user SET them. Empty index name + ascending
    // → no USING clause (FB default index named after the constraint).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _indexName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _descending;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>Live preview. Falls back to a stub while the form is
    /// incomplete (no name / no field selected).</summary>
    public string DdlPreview
    {
        get
        {
            var spec = TryBuildPreviewSpec();
            if (spec is null) return UiStrings.ConstraintDdlPreviewIncomplete;
            try
            {
                return Kind == ConstraintFieldKind.PrimaryKey
                    ? DdlGenerator.BuildAddPrimaryKey(TableName, spec.Name, spec.Fields, spec.IndexName, spec.Descending)
                    : DdlGenerator.BuildAddUnique(TableName, spec.Name, spec.Fields, spec.IndexName, spec.Descending);
            }
            catch (ArgumentException)
            {
                return UiStrings.ConstraintDdlPreviewIncomplete;
            }
        }
    }

    private ConstraintFieldSpec? TryBuildPreviewSpec()
    {
        var selected = SelectedFieldNames();
        if (selected.Count == 0) return null;
        return new ConstraintFieldSpec(
            string.IsNullOrWhiteSpace(ConstraintName) ? "?" : ConstraintName.Trim(),
            selected,
            string.IsNullOrWhiteSpace(IndexName) ? null : IndexName.Trim(),
            Descending);
    }

    /// <summary>True when ready to convert to a spec: name present + at least
    /// one field selected. Sets <see cref="ValidationMessage"/> on failure.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ConstraintName))
        {
            ValidationMessage = UiStrings.ConstraintValidationNameRequired;
            return false;
        }
        if (SelectedFieldNames().Count == 0)
        {
            ValidationMessage = UiStrings.ConstraintValidationFieldsRequired;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    public ConstraintFieldSpec BuildResult()
        => new(ConstraintName.Trim(),
               SelectedFieldNames(),
               string.IsNullOrWhiteSpace(IndexName) ? null : IndexName.Trim(),
               Descending);

    private string DefaultName()
    {
        var t = TableName.Trim().ToUpperInvariant();
        var prefix = Kind == ConstraintFieldKind.PrimaryKey ? "PK_" : "UNQ_";
        return string.IsNullOrEmpty(t) ? prefix.TrimEnd('_') : prefix + t;
    }

    private List<string> SelectedFieldNames()
    {
        var list = new List<string>();
        foreach (var f in Fields)
        {
            if (f.IsSelected) list.Add(f.Name);
        }
        return list;
    }

    private void OnFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WireFieldPropertyChanged();
        OnPropertyChanged(nameof(DdlPreview));
    }

    private void WireFieldPropertyChanged()
    {
        foreach (var f in Fields)
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

    public event Action? RequestClose;
    public ConstraintFieldSpec? Result { get; private set; }

    [RelayCommand]
    private void Accept()
    {
        if (!IsValid()) return;
        Result = BuildResult();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
