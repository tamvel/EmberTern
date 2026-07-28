using System.Globalization;
using System.Text;
using Avalonia.Input;

namespace EmberTern.App.Commands;

/// <summary>
/// The ONE place a keyboard gesture is turned into text a user reads. Every tooltip, shortcut chip and
/// status message that names a shortcut composes it from here, so a gesture is written down once — in
/// <see cref="CommandCatalog"/> — and re-binding it updates every surface that mentions it.
///
/// <para>⭐ <b>Why this exists at all.</b> Before it, ~20 <c>UiStrings</c> constants carried their gesture as
/// literal text (<c>"Continue · F5"</c>). Etap 3 moved Format SQL from <c>Alt+F</c> to <c>Ctrl+K</c> and
/// <c>ToolbarFormatSqlTooltip</c> went on saying <c>"Format SQL · Alt+F"</c> — a tooltip that confidently
/// taught the user a shortcut that no longer existed, with a green build and passing tests. Hand-typed
/// gestures do not merely duplicate the catalog; they go stale silently, which is worse than absent.</para>
///
/// <para>The label text stays in <see cref="UiStrings"/> (architecture rule #6) and is passed in — one
/// <see cref="CommandId"/> serves eleven differently-worded Compile buttons, so the text cannot live on the
/// descriptor. Only the gesture comes from the catalog.</para>
///
/// <para>⚠ Composed once, at static initialisation. That is correct while gestures are fixed; the day a
/// shortcut editor lands, this class is the single place that has to start recomposing.</para>
/// </summary>
public static class CommandTip
{
    /// <summary>The separator between an action and its gesture — the convention set by UX Polish Seam 1.</summary>
    private const string Separator = " · ";

    /// <summary>
    /// <paramref name="text"/> followed by the command's gesture: <c>"Compile the procedure · F7"</c>.
    /// Returns <paramref name="text"/> unchanged when the command has no gesture, so a caller never has to
    /// ask whether one exists.
    /// </summary>
    public static string For(CommandId id, string text)
    {
        var gesture = Gesture(id);
        return gesture.Length == 0 ? text : text + Separator + gesture;
    }

    /// <summary>
    /// The command's gesture on its own — for a <c>TextBlock.shortcut-chip</c>, which shows the key beside a
    /// button's label rather than after a sentence. Empty when the command has no gesture.
    /// </summary>
    public static string Gesture(CommandId id)
        => CommandCatalog.For(id)?.Gesture is { } gesture ? Format(gesture) : string.Empty;

    /// <summary>
    /// The command's gesture substituted into a sentence — <c>Sentence(Restart, "… Restart ({0}) runs …")</c>.
    /// For prose that names a shortcut mid-sentence, where a trailing <c>· key</c> would not read.
    /// </summary>
    public static string Sentence(CommandId id, string format)
        => string.Format(CultureInfo.CurrentCulture, format, Gesture(id));

    /// <summary>
    /// Renders a gesture the way Windows writes one: modifiers in Ctrl → Shift → Alt order, then the key.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="KeyGesture.ToString"/>: that spells the raw enum name, so
    /// <c>Ctrl+.</c> would reach the user as <c>"Ctrl+OemPeriod"</c>. The named keys below are the ones
    /// EmberTern actually shows; anything else falls back to the enum name, which is correct for letters,
    /// digits and function keys.
    /// </remarks>
    public static string Format(KeyGesture gesture)
    {
        var text = new StringBuilder();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) text.Append("Ctrl+");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) text.Append("Shift+");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) text.Append("Alt+");
        text.Append(KeyName(gesture.Key));
        return text.ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.Delete => "Del",
        Key.Space => "Space",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        _ => key.ToString(),
    };
}
