using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

/// <summary>Tiny "create role" dialog (IBExpert-style): a name field + OK/Cancel.
/// Name is coerced UPPERCASE (gotcha #141). Returns the trimmed name or null.</summary>
public partial class NewRoleDialogViewModel : ViewModelBase
{
    private bool _settingNameUpper;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _roleName = string.Empty;

    partial void OnRoleNameChanged(string value)
    {
        if (_settingNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (upper != value)
        {
            _settingNameUpper = true;
            RoleName = upper;
            _settingNameUpper = false;
        }
    }

    public string? Result { get; private set; }
    public event Action? RequestClose;

    private bool CanAccept() => !string.IsNullOrWhiteSpace(RoleName);

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        Result = RoleName.Trim();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
