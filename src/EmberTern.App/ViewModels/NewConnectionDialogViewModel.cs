using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public enum TestConnectionStatus
{
    Idle,
    Testing,
    Success,
    Failure,
}

public partial class NewConnectionDialogViewModel : ViewModelBase
{
    private readonly FirebirdConnectionService _service;
    private string? _editingProfileId;

    public NewConnectionDialogViewModel(FirebirdConnectionService service)
    {
        _service = service;
    }

    // SQL Dialect is no longer exposed in the UI (Dialect 3 is universal); the value is
    // carried through unchanged so an existing Dialect=1 connection keeps working.
    private int _carriedDialect = 3;

    public bool IsEditing => _editingProfileId is not null;

    public IReadOnlyList<string> Charsets => CharsetCatalog.Supported;

    public string DialogTitle => IsEditing
        ? UiStrings.DialogEditConnectionTitle
        : UiStrings.DialogNewConnectionTitle;
    public string GeneralSectionLabel => UiStrings.DialogSectionGeneral;
    public string AdvancedSectionLabel => UiStrings.DialogSectionAdvanced;
    public string NameLabel => UiStrings.DialogFieldName;
    public string HostLabel => UiStrings.DialogFieldHost;
    public string PortLabel => UiStrings.DialogFieldPort;
    public string DatabasePathLabel => UiStrings.DialogFieldDatabasePath;
    public string UsernameLabel => UiStrings.DialogFieldUsername;
    public string PasswordLabel => UiStrings.DialogFieldPassword;
    public string CharsetLabel => UiStrings.DialogFieldCharset;
    public string ClientLibraryLabel => UiStrings.DialogFieldClientLibrary;
    public string ClientLibraryHint => UiStrings.DialogFieldClientLibraryHint;
    public string TestConnectionLabel => UiStrings.DialogTestConnection;
    public string SaveLabel => UiStrings.DialogSave;
    public string CancelLabel => UiStrings.DialogCancel;
    public string BrowseLabel => UiStrings.DialogBrowse;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = "localhost";

    [ObservableProperty]
    private int _port = 3050;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _username = "SYSDBA";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _charset = CharsetCatalog.Default;

    [ObservableProperty]
    private string _clientLibraryPath = string.Empty;

    // Single user-facing switch (replaces the TPB profile pickers): OFF = DDL fail-fast,
    // ON = DDL waits for in-use objects (lock timeout). Only affects DDL, never data.
    [ObservableProperty]
    private bool _developerMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTesting))]
    [NotifyPropertyChangedFor(nameof(IsTestSuccess))]
    [NotifyPropertyChangedFor(nameof(IsTestFailure))]
    private TestConnectionStatus _testStatus = TestConnectionStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestMessage))]
    private string _testMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool IsTesting => TestStatus == TestConnectionStatus.Testing;
    public bool IsTestSuccess => TestStatus == TestConnectionStatus.Success;
    public bool IsTestFailure => TestStatus == TestConnectionStatus.Failure;
    public bool HasTestMessage => !string.IsNullOrEmpty(TestMessage);
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public event System.Action? RequestClose;
    public ConnectionProfile? Result { get; private set; }

    [RelayCommand(CanExecute = nameof(CanRunTest))]
    private async Task TestConnectionAsync()
    {
        if (!TryBuildProfile(out var profile, out var error))
        {
            TestStatus = TestConnectionStatus.Failure;
            TestMessage = error;
            return;
        }

        TestStatus = TestConnectionStatus.Testing;
        TestMessage = UiStrings.TestInProgress;

        try
        {
            await _service.TestConnectionAsync(profile).ConfigureAwait(true);
            TestStatus = TestConnectionStatus.Success;
            TestMessage = UiStrings.TestSuccess;
        }
        catch (ConnectionFailedException ex)
        {
            TestStatus = TestConnectionStatus.Failure;
            TestMessage = ex.Message;
        }
    }

    private bool CanRunTest() => !IsTesting;

    [RelayCommand]
    private void Save()
    {
        if (!TryBuildProfile(out var profile, out var error))
        {
            ValidationMessage = error;
            return;
        }

        Result = profile;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }

    public void LoadFromProfile(ConnectionProfile profile)
    {
        _editingProfileId = profile.Id;
        Name = profile.Name;
        Host = profile.Host;
        Port = profile.Port;
        DatabasePath = profile.DatabasePath;
        Username = profile.Username;
        Password = profile.Password;
        Charset = profile.Charset;
        _carriedDialect = profile.Dialect;
        ClientLibraryPath = profile.ClientLibraryPath;
        DeveloperMode = profile.DeveloperMode;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private bool TryBuildProfile(out ConnectionProfile profile, out string error)
    {
        profile = null!;

        // Database path is required first — the name can be DERIVED from it (below), but nothing
        // can be derived from a missing path.
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            error = UiStrings.ValidationDatabaseRequired;
            return false;
        }

        // Convenience (IBExpert parity): a blank name defaults to the database file's base name —
        // "D:\Bazy\Firma\Magazyn.fdb" → "Magazyn". Persist it so the field the user next sees is
        // populated too, not just the saved profile.
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = DeriveConnectionName(DatabasePath);
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = UiStrings.ValidationNameRequired;
            return false;
        }

        // Clamp the name as a backstop — the dialog TextBox already caps input at
        // ConnectionNameMaxLength, but a pasted / restored / imported value could be
        // longer. Trim first, then truncate so the persisted name can never overflow
        // the titlebar chip / sidebar rows.
        var trimmedName = Name.Trim();
        if (trimmedName.Length > UiStrings.ConnectionNameMaxLength)
        {
            trimmedName = trimmedName.Substring(0, UiStrings.ConnectionNameMaxLength);
        }

        profile = new ConnectionProfile
        {
            Name = trimmedName,
            Host = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim(),
            Port = Port > 0 ? Port : 3050,
            DatabasePath = DatabasePath.Trim(),
            Username = Username.Trim(),
            Password = Password,
            Charset = string.IsNullOrWhiteSpace(Charset) ? CharsetCatalog.Default : Charset,
            Dialect = _carriedDialect == 1 ? 1 : 3,
            ClientLibraryPath = ClientLibraryPath?.Trim() ?? string.Empty,
            DeveloperMode = DeveloperMode,
        };

        if (_editingProfileId is not null)
        {
            profile.Id = _editingProfileId;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The database file's base name, used as the default connection name when the user
    /// leaves the field blank. "D:\Bazy\Firma\Magazyn.fdb" → "Magazyn". Handles either path
    /// separator; falls back to the raw path for an alias with no file component.</summary>
    private static string DeriveConnectionName(string databasePath)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(databasePath.Trim());
        return string.IsNullOrWhiteSpace(name) ? databasePath.Trim() : name;
    }
}
