using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Security;

namespace EmberTern.App.ViewModels;

/// <summary>Add/Edit form for a Firebird server user. On Add the password is
/// required; on Edit a blank password keeps the existing one. The user name is
/// coerced UPPERCASE (gotcha #141) and is read-only when editing.</summary>
public partial class UserEditDialogViewModel : ViewModelBase
{
    private bool _settingNameUpper;

    public UserEditDialogViewModel(UserInfo? existing)
    {
        IsNew = existing is null;
        if (existing is not null)
        {
            UserName = existing.UserName;
            FirstName = existing.FirstName ?? string.Empty;
            MiddleName = existing.MiddleName ?? string.Empty;
            LastName = existing.LastName ?? string.Empty;
            Active = existing.Active;
            Admin = existing.Admin;
            Description = existing.Description ?? string.Empty;
        }
    }

    public bool IsNew { get; }
    public bool CanEditName => IsNew;
    public string Title => IsNew
        ? UiStrings.SecurityUserDialogAddTitle
        : string.Format(CultureInfo.CurrentCulture, UiStrings.SecurityUserDialogEditTitle, UserName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _userName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _middleName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private bool _active = true;
    [ObservableProperty] private bool _admin;
    [ObservableProperty] private string _description = string.Empty;

    partial void OnUserNameChanged(string value)
    {
        if (_settingNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (upper != value)
        {
            _settingNameUpper = true;
            UserName = upper;
            _settingNameUpper = false;
        }
    }

    public UserEditResult? Result { get; private set; }
    public event Action? RequestClose;

    private bool CanAccept()
    {
        if (string.IsNullOrWhiteSpace(UserName)) return false;
        if (IsNew && string.IsNullOrEmpty(Password)) return false;
        // When a password is being set (always on Add, optionally on Edit) it must match.
        if (!string.IsNullOrEmpty(Password) && Password != ConfirmPassword) return false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        var user = new UserInfo
        {
            UserName = UserName.Trim(),
            FirstName = FirstName,
            MiddleName = MiddleName,
            LastName = LastName,
            Active = Active,
            Admin = Admin,
            Description = Description,
        };
        Result = new UserEditResult(user, Password, IsNew);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
