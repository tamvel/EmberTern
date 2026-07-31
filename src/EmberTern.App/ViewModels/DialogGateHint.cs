using EmberTern.App.Controls;

namespace EmberTern.App.ViewModels;

/// <summary>
/// ⭐ Why a dialog's primary button is disabled, said in the app's own severity language.
///
/// <para><b>A greyed-out button with no reason is a UX defect</b> — the Data Import readiness strip states that
/// rule outright (§9.1 point 3) and is the surface this projection copies. Etap 5b's export dialog had the reason
/// but rendered it as plain <c>SubtleForegroundBrush</c> text, which is what <see cref="MessageSeverity.Info"/>
/// looks like — so a genuine input error ("the two passphrases are not the same") read exactly like a hint, and
/// QA reported the user being left to wonder why the button stayed dead.</para>
///
/// <para>⭐ <b>The colour and the icon come from <see cref="MessageBanner"/>'s shared map, never from a literal
/// here.</b> That is the same move <c>ImportReadinessItemViewModel</c> and <c>QueryMessageViewModel</c> make: one
/// severity reads as one colour everywhere in the IDE, and a second brush table is the drift. ⛔ Do not paint this
/// with a local <c>ErrorBrush</c> in XAML — the point is that it cannot disagree with a banner.</para>
///
/// <para>⚠ <b>The two severities mean different things, and the split is deliberate.</b>
/// <see cref="MessageSeverity.Error"/> = <i>what is there is wrong</i> (mismatched passphrases, nothing selected);
/// <see cref="MessageSeverity.Warning"/> = <i>a required step has not happened yet</i> (no passphrase typed).
/// Painting every blocked state red would make a freshly-opened dialog red before the user has done anything, and
/// a colour that is always on says nothing. Both are unmistakable, which is what the QA finding asked for.</para>
///
/// <para>Immutable: a view model publishes a new instance and raises one property change, so the text, the colour
/// and the icon can never be observed disagreeing with each other.</para>
/// </summary>
public sealed class DialogGateHint
{
    /// <summary>Nothing blocks the action — the row renders as empty.</summary>
    public static readonly DialogGateHint None = new(string.Empty, MessageSeverity.Info);

    private DialogGateHint(string text, MessageSeverity severity)
    {
        Text = text;
        Severity = severity;
    }

    /// <summary>The current state is wrong and the user must change something.</summary>
    public static DialogGateHint Error(string text) => new(text, MessageSeverity.Error);

    /// <summary>A required step is simply outstanding — visible, but not an accusation.</summary>
    public static DialogGateHint Pending(string text) => new(text, MessageSeverity.Warning);

    public string Text { get; }

    public MessageSeverity Severity { get; }

    public bool IsVisible => !string.IsNullOrEmpty(Text);

    public string BrushKey => MessageBanner.BrushKeyFor(Severity);

    public string GeometryKey => MessageBanner.GeometryKeyFor(Severity);
}
