using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One collapsible group in the debugger's Variables panel (Stage X / D7) — Pinned / Parameters / Locals
/// (spec §9.4). A thin container: a localized <see cref="Header"/>, the <see cref="Rows"/> it holds (the same
/// mutable row instances the roster owns — one roster, two projections), and a session-scoped
/// <see cref="IsExpanded"/> that survives step-by-step rebuilds because the group instance is reused.
/// </summary>
public sealed partial class DebugVariableGroupViewModel : ObservableObject
{
    public DebugVariableGroupViewModel(string header)
    {
        Header = header;
        Rows = new ObservableCollection<DebugVariableRowViewModel>();
    }

    public string Header { get; }

    public ObservableCollection<DebugVariableRowViewModel> Rows { get; }

    /// <summary>Expanded/collapsed — persists across pauses (the group VM is not recreated).</summary>
    [ObservableProperty]
    private bool _isExpanded = true;
}
