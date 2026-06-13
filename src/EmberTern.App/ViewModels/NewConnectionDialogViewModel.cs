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

    public bool IsEditing => _editingProfileId is not null;

    public IReadOnlyList<string> Charsets => CharsetCatalog.Supported;
    public IReadOnlyList<int> Dialects { get; } = new[] { 1, 3 };
    public IReadOnlyList<TransactionProfileOption> TransactionProfiles => TransactionProfileCatalog.All;

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
    public string TransactionProfileLabel => UiStrings.DialogFieldTransactionProfile;
    public string DialectLabel => UiStrings.DialogFieldDialect;
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
    private int _dialect = 3;

    [ObservableProperty]
    private string _clientLibraryPath = string.Empty;

    // The picker binds SelectedItem to this wrapper (Avalonia has no
    // SelectedValueBinding — gotcha #57). The setter mirrors into the enum-typed
    // TransactionProfile; the description + warning re-evaluate off the option.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransactionProfileDescription))]
    [NotifyPropertyChangedFor(nameof(ShowConsistencyWarning))]
    private TransactionProfileOption _selectedTransactionProfile = TransactionProfileCatalog.All[0];

    public TransactionProfile TransactionProfile => SelectedTransactionProfile.Value;
    public string TransactionProfileDescription => SelectedTransactionProfile.Description;
    public bool ShowConsistencyWarning => SelectedTransactionProfile.IsConsistencyWarning;

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
        Dialect = profile.Dialect;
        ClientLibraryPath = profile.ClientLibraryPath;
        SelectedTransactionProfile = TransactionProfileCatalog.For(profile.TransactionProfile);
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private bool TryBuildProfile(out ConnectionProfile profile, out string error)
    {
        profile = null!;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = UiStrings.ValidationNameRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            error = UiStrings.ValidationDatabaseRequired;
            return false;
        }

        profile = new ConnectionProfile
        {
            Name = Name.Trim(),
            Host = string.IsNullOrWhiteSpace(Host) ? "localhost" : Host.Trim(),
            Port = Port > 0 ? Port : 3050,
            DatabasePath = DatabasePath.Trim(),
            Username = Username.Trim(),
            Password = Password,
            Charset = string.IsNullOrWhiteSpace(Charset) ? CharsetCatalog.Default : Charset,
            Dialect = Dialect == 1 ? 1 : 3,
            ClientLibraryPath = ClientLibraryPath?.Trim() ?? string.Empty,
            TransactionProfile = TransactionProfile,
        };

        if (_editingProfileId is not null)
        {
            profile.Id = _editingProfileId;
        }

        error = string.Empty;
        return true;
    }
}
