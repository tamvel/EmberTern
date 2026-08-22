using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// Every word the About window says.
///
/// <para>⭐ Its own catalog rather than a region of another, per L8 decision D‑5's thematic split — and
/// because the prefix is what makes the split safe: this one wants a <c>WindowTitle</c> and so does
/// <see cref="ManagerSettingsCatalog"/>.</para>
///
/// <para>⭐⭐ <b>The Polish is INHERITED from the product, not invented</b> (`terminology.md` §4.1): the
/// product already ships ratified Polish for exactly these lines — <c>AboutVersionFormat</c>,
/// <c>AboutReleasedFormat</c>, <c>AboutAuthorFormat</c>, <c>AboutClose</c> — and this window states the same
/// facts about the same release. ⛔ A different word here would mean the same fact is called two things on
/// the issuer's side and the customer's side.</para>
///
/// <para>⚠⚠ <b>Properties and methods, never <c>const</c> and never <c>static readonly</c>.</b> A
/// <c>const</c> is inlined, so nothing is left to resolve; a <c>static readonly</c> resolves once and then
/// freezes in whichever language happened to be in force. This is the lesson the product's own frozen
/// <c>SettingsCatalog</c> table taught, at the cost of a Polish QA round.</para>
///
/// <para>⭐ <b>The product NAME is an argument, never part of a value.</b> Branding is exempt from
/// localization (`terminology.md` §4.4) — but baking it into the sentence would also hand the translator a
/// fixed word order, so it travels as <c>{0}</c> from <see cref="ManagerInfo.Product"/>. ⚠ The product does
/// it the other way ("About EmberTern" is one value); this is the better half of the two, and the reason it
/// differs is worth having written down rather than looking like drift.</para>
///
/// <para>⛔ There is no version, date, author or copyright literal here either: every one of those is a
/// value the build declares — see <see cref="ManagerInfo"/>.</para>
/// </summary>
[StringCatalog(KeyPrefix)]
internal static class AboutCatalog
{
    /// <summary>The prefix every key in this catalog carries.</summary>
    internal const string KeyPrefix = "About.";

    /// <summary>Resolves one of this catalog's own wordings.</summary>
    /// <remarks>
    /// ⚠ The argument is always <c>nameof(TheMember)</c>, never a typed-out string: the member name IS the
    /// key, so there is one owner and nothing to keep in step.
    /// </remarks>
    private static string Word(string member) => Loc.Text(KeyPrefix + member);

    /// <summary>The window's title — "About EmberTern License Manager".</summary>
    public static string WindowTitle(string product) =>
        Loc.Format(KeyPrefix + nameof(WindowTitle), product);

    /// <summary>"Version 0.…". ⛔ The number is the build's, never a literal.</summary>
    public static string Version(string version) =>
        Loc.Format(KeyPrefix + nameof(Version), version);

    /// <summary>"Released 2026-…". ⚠ The line is hidden entirely when the build declared no date.</summary>
    public static string Released(string date) =>
        Loc.Format(KeyPrefix + nameof(Released), date);

    /// <summary>"Created by …".</summary>
    /// <remarks>
    /// ⚠ The label is deliberate, and the product records why: unlabelled, the bare name reads as an
    /// unsigned line of text, and it already appears in the copyright below — the label is what makes the
    /// repetition read as authorship rather than as an accident.
    /// </remarks>
    public static string Author(string author) =>
        Loc.Format(KeyPrefix + nameof(Author), author);

    /// <summary>The way out.</summary>
    public static string Close => Word(nameof(Close));
}
