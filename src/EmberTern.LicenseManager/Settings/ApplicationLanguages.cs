using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmberTern.LicenseManager.Settings;

/// <summary>
/// The languages the License Manager's own INTERFACE can be shown in.
///
/// <para>⭐⭐ <b>It is a SECOND catalog on purpose, and the separation is the whole point.</b>
/// <see cref="Email.MessageLanguages"/> answers <i>"what language is the licence e-mail written in"</i> —
/// a fact about the CUSTOMER who will read it. This answers <i>"what language does the operator read"</i>
/// — a fact about the person sitting in front of the application. The two are independent settings, they
/// are shown on the same Settings page, and the message language is said on screen to be independent of
/// the interface language.</para>
///
/// <para>⚠⚠ <b>Until this type existed, both pickers were built from
/// <c>MessageLanguages.All</c></b> (<c>SettingsViewModel</c> called <c>LanguageOption.All()</c> twice).
/// That was harmless only while the interface picker was a disabled placeholder: the moment it becomes
/// real, adding a MESSAGE language would silently add an INTERFACE language the application has no
/// translation for — and the new language would render as English with a Polish-looking name in the
/// picker. ⛔ Do not merge these two lists back together.</para>
///
/// <para>⭐ <b>The default is ENGLISH</b> (decision D‑3), and it deliberately differs from
/// <see cref="Email.MessageLanguages.Default"/>, which is Polish. Neither is a copy of the other's
/// reasoning: the customers are Polish companies, while English is EmberTern's own default
/// (<c>PreferenceOptions.Language</c>) and is the NEUTRAL resource set every untranslated key falls back
/// to. A key with no Polish entry renders in English either way, so English is the only default that
/// cannot produce a half-translated screen.</para>
///
/// <para>⭐ <b>A code is a CULTURE NAME</b>, exactly as in EmberTern's own catalog, so nothing here ever
/// branches per language: the code goes straight through to <c>CultureInfo.GetCultureInfo</c> and names
/// the satellite <c>Strings.&lt;code&gt;.resx</c>. ⛔ Do not invent a code that is not a culture name.</para>
/// </summary>
public static class ApplicationLanguages
{
    /// <summary>English — the neutral resource set, and the default (D‑3).</summary>
    public const string English = "en";

    /// <summary>Polish.</summary>
    public const string Polish = "pl";

    /// <summary>
    /// What an unset, unknown or unreadable preference resolves to.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately NOT <see cref="Email.MessageLanguages.Default"/> — see the type's remarks.
    /// </remarks>
    public const string Default = English;

    /// <summary>Every interface language, in the order the picker offers them.</summary>
    /// <remarks>⭐ English first, matching the default and EmberTern's own catalog order.</remarks>
    public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>([English, Polish]);

    /// <summary>Whether <paramref name="code"/> is one this build can show the interface in.</summary>
    public static bool IsSupported(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="code"/> as one of <see cref="All"/>, or <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ Never throws. A stored preference naming a language this build does not know must land on the
    /// default rather than on nothing — the same rule <c>PreferenceOptions</c> states in the product, and
    /// for the same reason: one unusable value must not make the whole settings file unusable.
    /// </remarks>
    public static string Resolve(string? code) =>
        IsSupported(code)
            ? All.First(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
            : Default;
}
