using System.Collections.Generic;
using System.Collections.ObjectModel;
using EmberTern.LicenseManager.Email;

namespace EmberTern.LicenseManager.Settings;

/// <summary>One page in the Settings window's left-hand list.</summary>
/// <param name="Id">A stable identifier. ⛔ Never shown; never translated.</param>
/// <param name="IconKey">A geometry key from the linked <c>IconGeometries.axaml</c>.</param>
public sealed record SettingsCategory(string Id, string IconKey);

/// <summary>
/// Every word the Settings window says, in ONE place.
///
/// <para>⭐⭐ <b>This is the whole of L6.1a's preparation for L8, and it is deliberately not a
/// mechanism.</b> Forty lines of properties, no resource files, no <c>ResourceManager</c>, no culture
/// plumbing. What it buys is that the day the License Manager is localized, the words are already
/// gathered: each property body changes from a literal to a resolved lookup and ⛔ not one view, view
/// model or binding has to change.</para>
///
/// <para>⚠⚠ <b>PROPERTIES, never <c>const</c> and never <c>static readonly</c> — and never a table built
/// in a static constructor.</b> This is the single most expensive lesson EmberTern's own settings carry:
/// its <c>SettingsCatalog</c> built its tables in a static constructor, which froze the entire settings
/// vocabulary to whatever language happened to be in force when the type was first touched. The Polish QA
/// round found the General page's heading rendering "Ogólne" while the category list beside it rendered
/// "General" — the same word, two paths, one of them frozen, and only a restart cleared it. ⛔ Do not
/// "optimise" these into fields.</para>
///
/// <para>⚠ The category IDs and icon keys are the exception and are captured as constants on purpose: an
/// identifier is not a word, and an icon key is not a word — neither moves with the language.</para>
/// </summary>
public static class ManagerSettingsCatalog
{
    /// <summary>The General page — application-wide preferences.</summary>
    public const string CategoryGeneral = "general";

    /// <summary>The E-mail page — how a licence reaches the customer.</summary>
    public const string CategoryEmail = "email";

    // ⭐ Icon keys named here beside the ids, and for the same reason EmberTern names its own here: a
    //   resource key is as vulnerable to a typo as an option key, and IconGeometryConverter answers an
    //   unknown key with null — the icon simply vanishes, with a green build.
    private const string IconGeneral = "Icon.Settings";
    private const string IconEmail = "Icon.FileText";

    /// <summary>The pages, in the order the navigation offers them.</summary>
    /// <remarks>
    /// ⭐ General first: it is the page an operator lands on, and the one whose contents are about the
    /// application rather than about one of its jobs. ⚠ A plain list rather than a lookup — with two
    /// entries a dictionary would be ceremony, and the view models key off <see cref="SettingsCategory.Id"/>.
    /// </remarks>
    public static IReadOnlyList<SettingsCategory> Categories { get; } =
        new ReadOnlyCollection<SettingsCategory>(
        [
            new SettingsCategory(CategoryGeneral, IconGeneral),
            new SettingsCategory(CategoryEmail, IconEmail),
        ]);

    /// <summary>The title of a page, for the navigation list and the page heading alike.</summary>
    /// <remarks>⚠ ONE answer for both, so the two can never disagree — which is exactly how EmberTern's
    /// frozen-table defect became visible.</remarks>
    public static string TitleOf(string categoryId) => categoryId switch
    {
        CategoryEmail => Email,
        _ => General,
    };

    // ── The words ───────────────────────────────────────────────────────────────────────────────────
    // ⚠ Properties, for the reason in the class comment. In L8 each body becomes a lookup.

    /// <summary>The window's own title.</summary>
    public static string WindowTitle => "Settings";

    /// <summary>The General page.</summary>
    public static string General => "General";

    /// <summary>The E-mail page.</summary>
    public static string Email => "E-mail";

    /// <summary>The interface-language row's caption.</summary>
    public static string ApplicationLanguage => "Application language";

    /// <summary>
    /// ⭐ Why the interface-language row is disabled. Decision D‑8: the control is SHOWN so the structure
    /// is real and L8 has a place to land, but it stores nothing — a preference that changes nothing is
    /// the defect that removed <c>ClientLibraryPath</c> from EmberTern's connection dialog.
    /// </summary>
    public static string ApplicationLanguageUnavailable =>
        "The License Manager is English-only in this version. Translating its interface is planned as a " +
        "later stage; until then this choice is shown but not stored, because a preference that changes " +
        "nothing is worse than one that is honestly unavailable.";

    /// <summary>The message-language row's caption.</summary>
    public static string MessageLanguage => "Message language";

    /// <summary>
    /// ⚠ Says the two independences out loud, because the pairing is the surprising part: the interface
    /// and the message do not have to be in the same language, and the message language is global rather
    /// than per-customer.
    /// </summary>
    public static string MessageLanguageDescription =>
        "The language a licence e-mail is written in. It is independent of the interface language, and it " +
        "applies to every customer — switch it before sending to a customer who reads the other language.";

    /// <summary>The SMTP group's caption on the E-mail page.</summary>
    public static string SmtpSettings => "SMTP settings";

    /// <summary>How a language code is offered to a human.</summary>
    /// <remarks>
    /// ⭐ Each language is named IN ITSELF — "Polski", not "Polish" — which is what a language picker owes
    /// its reader: the one person who cannot read the current interface language is exactly the person
    /// using it. ⛔ So these two do NOT become lookups in L8; they stay as they are.
    ///
    /// <para>⚠ It answers for BOTH pickers — the message language and the interface language — because a
    /// language's own name is the same fact in both places. ⛔ That is the ONLY thing the two share: the
    /// LISTS are <see cref="Email.MessageLanguages"/> and <see cref="ApplicationLanguages"/>, and they are
    /// deliberately separate. The codes are culture names in both, so one map serves them.</para>
    /// </remarks>
    public static string LanguageLabel(string code) => code switch
    {
        MessageLanguages.English => "English",
        _ => "Polski",
    };

    /// <summary>
    /// How a transport choice is offered to a human.
    ///
    /// <para>⭐⭐ It lives HERE rather than inside <c>SmtpSecurityOption</c> for the reason every word on
    /// this window does: the option record is the VALUE the picker stores, and a value that carries its
    /// own label carries the current language inside its identity. Two options built in two languages
    /// then compare unequal, and the picker silently loses its selection the moment the list is rebuilt.
    /// ⛔ Do not move a label back into an option record.</para>
    /// </summary>
    /// <remarks>⚠ A property-shaped body, like every word above it — in L8 it becomes a lookup.</remarks>
    public static string SecurityLabel(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => "No encryption — internal relay only",
        _ => "STARTTLS (recommended)",
    };
}
