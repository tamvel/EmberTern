using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One editable column-name row in the View Detail Easy-mode column list. A view
/// column carries no type (the SELECT determines it), so the row is just a name.
/// The name is folded to UPPERCASE on edit to match Firebird's catalog form — the
/// same convention <see cref="FieldRowViewModel"/> and the procedure field rows use.
/// The constructor sets the backing field directly (not via the property), so an
/// existing quoted/lower-case name parsed from the source is preserved verbatim
/// until the user actually edits it.
/// </summary>
public partial class ViewColumnRowViewModel : ObservableObject
{
    public ViewColumnRowViewModel(string name) => _name = name ?? string.Empty;

    [ObservableProperty]
    private string _name;

    private bool _settingUpper;
    partial void OnNameChanged(string value)
    {
        if (_settingUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingUpper = true;
            try { Name = upper; } finally { _settingUpper = false; }
        }
    }
}
