using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Result of the Add-Index dialog. The VM hands this to
/// <c>TableDetailTabViewModel.ExecuteAddIndexAsync</c>, which emits DDL via
/// <see cref="DdlGenerator.BuildCreateIndex"/>.
/// </summary>
public sealed record IndexSpec(
    string Name,
    IReadOnlyList<string> Fields,
    bool Unique,
    bool Descending,
    string? ComputedExpression);

/// <summary>
/// Drives the Add-Index dialog. Same contract as
/// <see cref="ConstraintFieldDialogViewModel"/> / <see cref="ForeignKeyDialogViewModel"/>:
/// every observable property re-notifies <see cref="DdlPreview"/>; Accept /
/// Cancel close returning an <see cref="IndexSpec"/> or null. Field selection
/// reuses <see cref="SelectableFieldViewModel"/>. When a COMPUTED BY expression
/// is entered, the field list is ignored (expression index) — the UI disables
/// the field picker to match Firebird semantics.
/// </summary>
public partial class IndexDialogViewModel : ViewModelBase
{
    public IndexDialogViewModel(string tableName, IReadOnlyList<string> fields)
    {
        TableName = tableName ?? string.Empty;
        Fields = new ObservableCollection<SelectableFieldViewModel>(
            (fields ?? Array.Empty<string>()).Select(n => new SelectableFieldViewModel(n)));
        Fields.CollectionChanged += OnFieldsCollectionChanged;
        WireFieldPropertyChanged();

        ConstraintName = DefaultName();
    }

    public string TableName { get; }
    public ObservableCollection<SelectableFieldViewModel> Fields { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _constraintName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _unique;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private bool _descending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    [NotifyPropertyChangedFor(nameof(HasComputed))]
    [NotifyPropertyChangedFor(nameof(IsFieldPickerEnabled))]
    private string _computedExpression = string.Empty;

    /// <summary>True when a COMPUTED BY expression is set — the field list is
    /// then ignored (expression index) and its picker disabled.</summary>
    public bool HasComputed => !string.IsNullOrWhiteSpace(ComputedExpression);

    /// <summary>Field multi-select is enabled only for a plain (non-expression) index.</summary>
    public bool IsFieldPickerEnabled => !HasComputed;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public string DdlPreview
    {
        get
        {
            var spec = TryBuildPreviewSpec();
            if (spec is null) return UiStrings.IndexDdlPreviewIncomplete;
            try
            {
                return DdlGenerator.BuildCreateIndex(
                    TableName, spec.Name, spec.Fields, spec.Unique, spec.Descending, spec.ComputedExpression);
            }
            catch (ArgumentException)
            {
                return UiStrings.IndexDdlPreviewIncomplete;
            }
        }
    }

    private IndexSpec? TryBuildPreviewSpec()
    {
        if (string.IsNullOrWhiteSpace(ConstraintName)) return null;
        var computed = HasComputed ? ComputedExpression.Trim() : null;
        var selected = SelectedFieldNames();
        if (computed is null && selected.Count == 0) return null;
        return new IndexSpec(ConstraintName.Trim(), selected, Unique, Descending, computed);
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ConstraintName))
        {
            ValidationMessage = UiStrings.IndexValidationNameRequired;
            return false;
        }
        if (!HasComputed && SelectedFieldNames().Count == 0)
        {
            ValidationMessage = UiStrings.IndexValidationFieldsRequired;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    public IndexSpec BuildResult()
        => new(ConstraintName.Trim(),
               SelectedFieldNames(),
               Unique,
               Descending,
               HasComputed ? ComputedExpression.Trim() : null);

    private string DefaultName()
    {
        var t = TableName.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(t) ? "IDX" : "IDX_" + t;
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
    public IndexSpec? Result { get; private set; }

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
