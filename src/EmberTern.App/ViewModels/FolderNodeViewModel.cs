using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;

namespace EmberTern.App.ViewModels;

/// <summary>
/// A sidebar tree node representing a connection folder. Holds an ordered
/// child list of <see cref="ConnectionNodeViewModel"/> instances; folders
/// don't nest. Rename uses the same TextBlock/TextBox swap pattern as
/// <see cref="SavedQueryViewModel"/>; Commit writes the new name into the
/// <see cref="FolderEntry"/> and asks the owner to persist folder state.
/// </summary>
public partial class FolderNodeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel? _owner;

    public FolderNodeViewModel(FolderEntry entry, MainWindowViewModel? owner = null)
    {
        Entry = entry;
        _owner = owner;
        Connections = new ObservableCollection<ConnectionNodeViewModel>();
    }

    public FolderEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;

    public ObservableCollection<ConnectionNodeViewModel> Connections { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
    private bool _isRenaming;

    public bool IsNotRenaming => !IsRenaming;

    [ObservableProperty]
    private string _editingName = string.Empty;

    [RelayCommand]
    private void BeginRename()
    {
        EditingName = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsRenaming) return;
        var newName = (EditingName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(newName) && newName != Name)
        {
            Entry.Name = newName;
            OnPropertyChanged(nameof(Name));
            _owner?.PersistFolderState();
        }
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private Task Delete() => _owner?.DeleteFolderAsync(this) ?? Task.CompletedTask;

    // Folder right-click "Dodaj połączenie": delegates to the owner, which fires
    // AddConnectionRequested(Id) — the view opens the NewConnectionDialog and on
    // confirm calls PlaceConnectionInFolder(profileId, Id).
    [RelayCommand]
    private Task AddConnection() => _owner?.RequestAddConnectionAsync(Id) ?? Task.CompletedTask;
}
