using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// The languages a licence e-mail can be written in.
///
/// <para>⭐⭐ <b>THE ONE PLACE THIS LIST EXISTS.</b> It is the same discipline EmberTern's Settings Center
/// records as its own hardest-won rule: every option a control offers is generated from one declaration,
/// because a second list in XAML drifts silently — add a language, forget the picker, and the build stays
/// green while the option is unreachable. The settings picker, the store's validation and the template
/// resolver all read THIS.</para>
///
/// <para>⭐ <b>A language is a CODE, never a template file name.</b> Adding a third language is two
/// resource files plus one row here — ⛔ never an <c>if</c> anywhere. See
/// <see cref="LicenseEmailTemplates"/>.</para>
///
/// <para>⚠ <b>This is the MESSAGE's language, and it is independent of the application's.</b> The
/// operator's interface may be English while the customer reads Polish, or the other way round; they are
/// two values and neither derives from the other. ⛔ Do not couple them when the application gains its own
/// localization (L8).</para>
///
/// <para>⚠ It is also a GLOBAL choice rather than a per-customer one: decision D‑4 declined a
/// <c>language</c> column on <c>customers</c>, so sending to a customer who reads the other language means
/// switching this setting first. Stated rather than hidden.</para>
/// </summary>
public static class MessageLanguages
{
    /// <summary>Polish.</summary>
    public const string Polish = "pl";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>
    /// ⭐ Polish, by decision D‑9. The customers are Polish companies — the same argument the register's
    /// own search already makes about culture-aware comparison.
    /// </summary>
    public const string Default = Polish;

    /// <summary>Every supported code, in the order a picker should offer them.</summary>
    public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>([Polish, English]);

    /// <summary>Whether a code names a language this build can write a message in.</summary>
    public static bool IsSupported(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The code to actually use for <paramref name="code"/>.
    ///
    /// <para>⭐ An unknown or missing code resolves to <see cref="Default"/> rather than throwing. That is
    /// the correct reading of a settings file written by a build that knew a language this one does not —
    /// a message still has to be composable, and refusing to send because a preference is unrecognised
    /// would fail the operation over a preference.</para>
    /// </summary>
    public static string Resolve(string? code) =>
        IsSupported(code)
            ? All.First(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
            : Default;
}
