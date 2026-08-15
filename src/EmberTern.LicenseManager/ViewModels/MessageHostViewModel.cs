using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The base every view model with a message surface derives from.
///
/// <para>⭐ It exists so the severity→appearance decision is made ONCE. Two view models each deriving
/// their own <c>IsError</c> is exactly how the "locally styled coloured TextBlock" problem starts, one
/// harmless-looking duplication at a time — which is the reason EmberTern has a single
/// <c>MessageBanner</c> rather than 23 message surfaces.</para>
/// </summary>
public abstract partial class MessageHostViewModel : ObservableObject
{
    /// <summary>The current message, or <see langword="null"/> when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(MessageText))]
    [NotifyPropertyChangedFor(nameof(IsInfo))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private StatusMessage? _message;

    /// <summary>Whether the strip is shown at all.</summary>
    public bool HasMessage => Message is not null;

    /// <summary>What it says.</summary>
    public string MessageText => Message?.Text ?? string.Empty;

    /// <summary>Neutral.</summary>
    public bool IsInfo => Message?.Severity == MessageSeverity.Info;

    /// <summary>Something worked.</summary>
    public bool IsSuccess => Message?.Severity == MessageSeverity.Success;

    /// <summary>Needs attention.</summary>
    public bool IsWarning => Message?.Severity == MessageSeverity.Warning;

    /// <summary>Something failed.</summary>
    public bool IsError => Message?.Severity == MessageSeverity.Error;
}
