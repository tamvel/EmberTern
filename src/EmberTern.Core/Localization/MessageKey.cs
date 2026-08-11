using System;

namespace EmberTern.Core.Localization;

/// <summary>
/// A stable identifier for a message that <b>EmberTern itself</b> wants shown, produced by a layer that
/// cannot know the user's language.
///
/// <para><b>Why a type and not a <c>string</c>.</b> The whole point of decision <b>D‑3</b> is that Core and
/// Firebird hand the App a <i>key plus data</i>, never finished English prose — App owns presentation and
/// therefore owns the words. A bare <c>string</c> parameter cannot express that: the compiler is equally
/// happy with <c>"SettingsFileUnreadable"</c> and with <c>"Your settings file could not be read."</c>, so the
/// rule would live only in review discipline and would rot the first time someone was in a hurry.</para>
///
/// <para>⭐ <b>So the rule is enforced by CONSTRUCTION, not by a test convention.</b> The constructor accepts
/// only an identifier-shaped token — letters, digits, <c>_</c> and <c>.</c> — which is a shape prose cannot
/// have: any sentence carries a space or a punctuation mark and is rejected on the spot. A guard then only has
/// to prove the constructor still refuses prose, instead of trying to judge whether some string "looks
/// English", which is exactly the kind of heuristic the localization audit showed to be unreliable.</para>
///
/// <para>⚠ <b>What this does NOT promise.</b> The <i>arguments</i> travelling beside a key
/// (<see cref="LocalizableMessage"/>) are data — a table name, a count, a file path, a raw server message —
/// and data can obviously contain English. That is intended and is not a leak: the sentence around them is
/// what must be translatable, and the sentence lives in App's resource catalog under this key.</para>
///
/// <para>⚠ Zero dependencies by design (no Avalonia, no App). Architecture rule #1 is what makes this seam
/// necessary in the first place — <c>UiStrings</c> is unreachable from here — so introducing any reference
/// here would defeat the reason the type exists.</para>
/// </summary>
public readonly record struct MessageKey
{
    /// <summary>The key itself, e.g. <c>"Settings.FileUnreadable"</c>.</summary>
    public string Value { get; }

    /// <param name="value">
    /// An identifier-shaped token: letters, digits, <c>_</c> or <c>.</c>, at least one character, and no
    /// leading or trailing <c>.</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// When <paramref name="value"/> is empty or carries any character prose would need — a space, a comma,
    /// a full stop at the end, a quote. ⭐ This throw <i>is</i> the D‑3 guarantee; do not relax it to accept
    /// "just this one" message. A message that will not fit a key is a message that belongs in the resource
    /// catalog, which is the point.
    /// </exception>
    public MessageKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("A message key must not be empty.", nameof(value));
        }

        if (value[0] == '.' || value[^1] == '.')
        {
            throw new ArgumentException(
                $"A message key must not start or end with '.': '{value}'.", nameof(value));
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.')
            {
                throw new ArgumentException(
                    $"A message key must be an identifier-shaped token (letters, digits, '_' or '.'); " +
                    $"'{value}' is not. A key names a message — the words themselves live in the App's " +
                    "resource catalog, so that they can be translated.",
                    nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The key, for logs and diagnostics. ⚠ Never a substitute for resolving it — a key on screen is
    /// a defect, and the guards exist so it cannot reach one.</summary>
    public override string ToString() => Value;
}
