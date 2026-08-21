using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The base every view model with a message surface derives from.
///
/// <para>⭐ It exists so the severity→appearance decision is made ONCE. Two view models each deriving
/// their own <c>IsError</c> is exactly how the "locally styled coloured TextBlock" problem starts, one
/// harmless-looking duplication at a time — which is the reason EmberTern has a single
/// <c>MessageBanner</c> rather than 23 message surfaces.</para>
///
/// <para>⭐⭐ <b>It is also the ONE place a standing message learns that the language changed</b> (L8.2).
/// <see cref="StatusMessage"/> holds a key and its arguments, so the words already follow the language —
/// but a binding is only re-read when something says so, and nothing about switching languages touches this
/// view model's own properties. ⚠ Without the line below the strip keeps rendering the old language while
/// every other word in the window has changed, and no binding error is raised: that is defect #353's exact
/// shape in the product's Data Import, one layer earlier.</para>
/// </summary>
public abstract partial class MessageHostViewModel : ObservableObject
{
    /// <summary>Wires the strip to the language.</summary>
    /// <remarks>
    /// ⚠⚠ The subscription is WEAK and the handler is a <c>static</c> lambda, because
    /// <c>Loc.LanguageChanged</c> is a static event and would otherwise root every host forever —
    /// <c>SendLicenceViewModel</c> is rebuilt on every send. See <see cref="LanguageChange.SubscribeWeak"/>.
    /// </remarks>
    protected MessageHostViewModel() =>
        LanguageChange.SubscribeWeak(this, static host => host.OnLanguageChanged());

    /// <summary>
    /// ⭐⭐ Re-reads every word this view model composes in C#, called on a real language change.
    ///
    /// <para><b>The base answers for the strip and for nothing else</b>, and that was the whole gap L8.4
    /// found: four derived hosts compose sentences of their own as computed properties, which follow the
    /// language perfectly in C# and are never re-read by a binding, because nothing tells the binding to
    /// ask again. ⚠ The failure is silent — no binding error, no exception, just one window rendering two
    /// languages at once.</para>
    ///
    /// <para>⛔ An override must call <c>base.OnLanguageChanged()</c>: the strip's own notification lives
    /// here, and a host that forgot it would freeze the message while refreshing everything around it.</para>
    /// </summary>
    protected virtual void OnLanguageChanged() => OnPropertyChanged(nameof(MessageText));

    /// <summary>The current message, or <see langword="null"/> when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(MessageText))]
    [NotifyPropertyChangedFor(nameof(IsInfo))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(MessageIconKey))]
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

    /// <summary>
    /// Which glyph the message wears — the other half of the "how bad is this" signal, beside the
    /// severity stripe.
    ///
    /// <para>⭐ A KEY, not a <c>Geometry</c>: an Avalonia type here would breach Architecture rule 1, and
    /// EmberTern solves the identical problem the identical way (<c>IconResourceKey</c> +
    /// <c>IconGeometryConverter</c>). ⚠ The four keys mirror <c>MessageBanner.GeometryKeyFor</c> exactly
    /// — stop octagon · alert triangle · check · note — so a warning in this application wears the same
    /// mark as a warning in the product.</para>
    /// </summary>
    public string MessageIconKey => Message?.Severity switch
    {
        MessageSeverity.Error => "Icon.BreakException",
        MessageSeverity.Warning => "Icon.AlertTriangle",
        MessageSeverity.Success => "Icon.Check",
        _ => "Icon.Comment",
    };
}
