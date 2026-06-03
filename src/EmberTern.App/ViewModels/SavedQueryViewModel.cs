using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

public partial class SavedQueryViewModel : ViewModelBase
{
    private readonly MainWindowViewModel? _owner;

    public SavedQueryViewModel(string id, string name, string sqlText, MainWindowViewModel? owner = null)
    {
        Id = id;
        _name = name;
        _sqlText = sqlText;
        _owner = owner;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _sqlText;

    // Inline-rename state. The list-item template shows a TextBlock when IsRenaming
    // is false and a TextBox bound to EditingName when true. Commit copies EditingName
    // back into Name (which a future CaptureWorkspace will pick up on app close —
    // same persistence path as SqlText edits, in-memory until window close).
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
        if (!string.IsNullOrEmpty(newName))
        {
            Name = newName;
        }
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    // Delegates to MainWindowViewModel.DeleteSavedQueryAsync(this) which runs the
    // confirm dialog + RemoveSavedQuery. No-op when constructed without an owner
    // (unit-test scenarios with bare-VM instances).
    [RelayCommand]
    private Task Delete() => _owner?.DeleteSavedQueryAsync(this) ?? Task.CompletedTask;
}
