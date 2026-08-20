using System.Globalization;
using EmberTern.LicenseManager.Settings;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// Turns a stored language code into the <see cref="CultureInfo"/> the catalog is read under.
/// </summary>
/// <remarks>
/// <para>⭐ Its own type rather than a member of <see cref="ApplicationLanguages"/>, because they answer
/// two questions: that catalog says WHICH languages exist, this says what a code MEANS to the resource
/// system. It is the counterpart of the product's <c>ThemePreference</c> — the one mapping between a
/// stored preference and the framework value it stands for.</para>
///
/// <para>⭐⭐ <b>There is no per-language branch here and there must never be one.</b> A code is a culture
/// name, so it goes straight through to <see cref="CultureInfo.GetCultureInfo(string)"/> and names the
/// satellite <c>Strings.&lt;code&gt;.resx</c>. That is what makes adding a language a row in the catalog
/// plus a resource file, with no code change — pinned by
/// <c>EveryLanguageInTheCatalog_ResolvesToItsOwnCulture</c>.</para>
/// </remarks>
internal static class LanguagePreference
{
    /// <summary>The culture for <paramref name="languageKey"/>; never throws.</summary>
    /// <remarks>
    /// ⚠ Two layers of forgiveness, and both are deliberate. An unknown code normalizes to the catalog's
    /// default; a code the OPERATING SYSTEM does not know falls back to the invariant culture, which
    /// resolves to the neutral (English) set. ⛔ Neither may become an exception: a preference file is not
    /// worth a crash, and this runs before any window exists.
    /// </remarks>
    public static CultureInfo CultureFor(string? languageKey)
    {
        var normalized = ApplicationLanguages.Resolve(languageKey);

        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
