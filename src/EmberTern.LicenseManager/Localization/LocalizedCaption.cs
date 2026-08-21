using System;
using System.ComponentModel;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// One COMPUTED caption's current text, as a bindable property — the sibling of
/// <see cref="LocalizedString"/> for a word that is chosen rather than named.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It exists because of a measurement, and the measurement is worth stating: a picker whose
/// items are option records DOES NOT re-read its labels when the language changes.</b>
/// <c>APickerLabel_RereadsWhenTheLanguageChanges</c> put a real <c>ComboBox</c> over real
/// <c>FilterOption</c>s in front of a two-culture catalog and read the text off the realised control —
/// before and after. It came back identical. Every option record in this application keeps its words OUT
/// of its identity (#394), so <c>Label</c> is a computed property and the C# read is perfectly live; but
/// the record raises no <c>PropertyChanged</c>, nothing about switching languages touches it or its
/// <c>ItemsSource</c>, and a binding only re-reads when something says so.</para>
///
/// <para>⛔ <b>The obvious alternative — rebuild the <c>ItemsSource</c> — was rejected.</b> It would work
/// only because identity is code-only, and it puts a transient <c>SelectedItem</c> change in the path of
/// three pickers that re-query on selection: exactly the blanked-picker failure #394 exists to prevent,
/// re-introduced to solve a rendering problem. Nothing is rebuilt here.</para>
///
/// <para>⭐ <b>Not a second mechanism.</b> It notifies off the same <see cref="Loc.LanguageChanged"/> at the
/// same moment <see cref="LocalizationSource.InvalidateAll"/> fires, and its <see cref="Value"/> resolves
/// through <see cref="Loc"/> like everything else. The only difference from <see cref="LocalizedString"/>
/// is where the text comes from: a key there, a decision here.</para>
///
/// <para>⚠ <b>Why the subscription is WEAK.</b> <see cref="Loc.LanguageChanged"/> is a <c>static</c> event,
/// i.e. a GC root, and a caption is created per read of an option's <c>Caption</c> — so a strong
/// subscription would accumulate one dead caption per template realisation, for the life of the process. A
/// caption stays alive exactly as long as the binding that reads it holds it, which is the right lifetime
/// and needs nobody to remember anything. See <see cref="LanguageChange.SubscribeWeak"/>.</para>
/// </remarks>
public sealed class LocalizedCaption : INotifyPropertyChanged
{
    private readonly Func<string> _resolve;

    /// <summary>Creates a caption over the decision that produces its words.</summary>
    /// <param name="resolve">
    /// ⚠ Must resolve at the moment of the CALL — a delegate that closes over an already-resolved string
    /// is frozen in the language it was built in, which is the <c>static readonly</c> defect wearing a
    /// lambda.
    /// </param>
    public LocalizedCaption(Func<string> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        LanguageChange.SubscribeWeak(this, static caption => caption.Invalidate());
    }

    /// <summary>The text, resolved at the moment of the read.</summary>
    public string Value => _resolve();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Invalidate() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
