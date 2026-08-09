using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Result of the Add-Check dialog. <see cref="Expression"/> is the raw
/// condition the user typed (bare or full <c>CHECK (...)</c> clause);
/// <see cref="DdlGenerator.BuildAddCheck"/> normalizes it.
/// </summary>
public sealed record CheckConstraintSpec(string Name, string Expression);

/// <summary>
/// Drives the Add-Check-Constraint dialog: a constraint name + a CHECK
/// condition. Mirrors the other dialog VMs — observable properties re-notify
/// <see cref="DdlPreview"/>; Accept / Cancel close returning a
/// <see cref="CheckConstraintSpec"/> or null.
/// </summary>
public partial class CheckConstraintDialogViewModel : ViewModelBase, IDdlPreviewSource
{
    public CheckConstraintDialogViewModel(string tableName, IReadOnlyList<string>? existingNames = null)
    {
        TableName = tableName ?? string.Empty;
        _existingNames = existingNames;
        ConstraintName = DefaultName();
    }

    public string TableName { get; }
    private readonly IReadOnlyList<string>? _existingNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _constraintName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DdlPreview))]
    private string _checkExpression = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public string DdlPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConstraintName) || string.IsNullOrWhiteSpace(CheckExpression))
            {
                return UiStrings.ConstraintDdlPreviewIncomplete;
            }
            try
            {
                return DdlGenerator.BuildAddCheck(TableName, ConstraintName.Trim(), CheckExpression);
            }
            catch (ArgumentException)
            {
                return UiStrings.ConstraintDdlPreviewIncomplete;
            }
        }
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ConstraintName))
        {
            ValidationMessage = UiStrings.ConstraintValidationNameRequired;
            return false;
        }
        if (string.IsNullOrWhiteSpace(CheckExpression))
        {
            ValidationMessage = UiStrings.CheckConstraintValidationExpressionRequired;
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }

    public CheckConstraintSpec BuildResult()
        => new(ConstraintName.Trim(), CheckExpression.Trim());

    private string DefaultName()
    {
        var t = TableName.Trim().ToUpperInvariant();
        var baseName = string.IsNullOrEmpty(t) ? "CHK" : "CHK_" + t;
        return ConstraintNaming.MakeUnique(baseName, _existingNames);
    }

    public event Action? RequestClose;
    public CheckConstraintSpec? Result { get; private set; }

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
