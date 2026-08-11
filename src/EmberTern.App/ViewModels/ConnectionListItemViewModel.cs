using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;

namespace EmberTern.App.ViewModels;

public partial class ConnectionListItemViewModel : ViewModelBase
{
    private readonly MainWindowViewModel? _parent;

    public ConnectionListItemViewModel(ConnectionProfile profile, MainWindowViewModel? parent = null)
    {
        Profile = profile;
        _parent = parent;
    }

    public ConnectionProfile Profile { get; }

    public string Name => Profile.Name;
    public string Endpoint => $"{Profile.Host}:{Profile.Port}";
    public string DatabaseLabel => string.IsNullOrWhiteSpace(Profile.DatabasePath)
        ? UiStrings.ConnectionNoPath
        : System.IO.Path.GetFileName(Profile.DatabasePath);

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private Task ConnectAsync() => _parent?.ConnectAsync(Profile) ?? Task.CompletedTask;

    [RelayCommand]
    private Task DisconnectAsync() => _parent?.DisconnectAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private void Delete() => _parent?.Delete(Profile);

    [RelayCommand]
    private void Edit() => _parent?.RequestEdit(Profile);
}
