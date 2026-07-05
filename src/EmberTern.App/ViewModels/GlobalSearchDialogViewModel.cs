using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Search;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The Global Search dialog (Ctrl+Shift+F): a phrase + what to search (names / source)
/// + case / whole-word options. Object kinds are fixed to the supported set — a single
/// clear box instead of IBExpert's 11-checkbox matrix (deliberate UX simplification).
/// Returns a <see cref="MetadataSearchQuery"/> or null on cancel.
/// </summary>
public partial class GlobalSearchDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _term = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private bool _matchNames = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private bool _matchSource = true;

    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _wholeWord;

    public GlobalSearchDialogViewModel(string? initialTerm = null)
    {
        if (!string.IsNullOrWhiteSpace(initialTerm)) _term = initialTerm.Trim();
    }

    public MetadataSearchQuery? Result { get; private set; }
    public event Action? RequestClose;

    private bool CanAccept() => !string.IsNullOrWhiteSpace(Term) && (MatchNames || MatchSource);

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        Result = new MetadataSearchQuery(
            Term.Trim(),
            MatchNames: MatchNames,
            MatchSource: MatchSource,
            CaseSensitive: CaseSensitive,
            WholeWord: WholeWord,
            Kinds: MetadataSearchQuery.SupportedKinds);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}
