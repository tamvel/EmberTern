using System.Globalization;
using EmberTern.Core.Settings;

namespace EmberTern.App.Localization;

/// <summary>
/// The ONE place a stored language key (<c>"en"</c>, later <c>"pl"</c>) becomes a
/// <see cref="CultureInfo"/> — the exact counterpart of <see cref="Settings.ThemePreference"/>, and
/// deliberately shaped the same way, because it solves the same problem: a preference travels as a string so
/// that a view model never names a framework type, and exactly one class turns it into an effect.
///
/// <para>⭐ <b>There is no per-language branch here, and there never may be.</b> The key <i>is</i> the culture
/// name, so the mapping is data: a second language is a row in
/// <see cref="PreferenceOptions.Language"/> plus a satellite <c>Strings.&lt;key&gt;.resx</c>, and not one line
/// of this file changes. That is the property decision <b>D‑2</b> asked for, and
/// <c>LocalizationMechanismTests</c> pins it by driving this method from the catalog rather than from a list
/// of its own (a list of its own would be a transcribed premise — gotcha #333).</para>
///
/// <para>⚠ <b>Normalization is the fallback</b>, not a separate safety net bolted on afterwards.
/// <see cref="PreferenceOptions.Language"/> already answers "unknown value ⇒ the catalog default", which is
/// English, so a hand-edited or imported <c>settings.dat</c> carrying <c>"kl"</c> lands on English by the same
/// mechanism that governs every other preference. Adding a second, independent "if unknown then English"
/// check here would be a second answer to one question.</para>
/// </summary>
internal static class LanguagePreference
{
    /// <summary>Stored key → culture. Never throws; anything unusable ends at
    /// <see cref="CultureInfo.InvariantCulture"/>, which resolves to the neutral (English) resources.</summary>
    public static CultureInfo CultureFor(string? languageKey)
    {
        var normalized = PreferenceOptions.Language.Normalize(languageKey);
        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            // A catalog row that the OS does not recognise as a culture. Cannot happen with the shipped
            // catalog, and it is caught rather than thrown because failing to pick a language must never be
            // the thing that stops the app from starting — the same reasoning as ThemePreference.Apply's
            // no-op. Invariant reads the neutral resources, i.e. English.
            return CultureInfo.InvariantCulture;
        }
    }
}
