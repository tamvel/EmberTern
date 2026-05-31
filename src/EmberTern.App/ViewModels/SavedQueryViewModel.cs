using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

public partial class SavedQueryViewModel : ViewModelBase
{
    public SavedQueryViewModel(string id, string name, string sqlText)
    {
        Id = id;
        _name = name;
        _sqlText = sqlText;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _sqlText;
}
