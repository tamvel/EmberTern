using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The About window's content — a <b>projection of <see cref="ManagerInfo"/></b>, which is itself a
/// projection of the assembly. It holds no state and nothing is settable.
///
/// <para>⭐ It exists rather than binding straight to <see cref="ManagerInfo"/> for one reason: each value
/// has to be composed with its label ("Version …", "Released …", "Created by …"), and labels are words that
/// belong in a catalog while the values belong to the build. This is the one place those two meet, and doing
/// it here keeps the view free of formatting.</para>
///
/// <para>⚠⚠ <b>Every property below composes its words in C#, so it follows the language perfectly on READ
/// and is never re-read unless something says so.</b> Hence the language subscription: without it the window
/// renders one language while everything around it renders another, with no binding error and no exception —
/// the exact shape L8.4 found four times over. ⚠ The subscription is WEAK with a <c>static</c> handler,
/// because <c>Loc.LanguageChanged</c> is a static event and would otherwise root this view model forever;
/// one of these is built on every open of the window.</para>
///
/// <para>⛔ <b>No version literal, here or anywhere else</b> — see <see cref="ManagerInfo"/>. ⛔ It is
/// deliberately NOT a <see cref="MessageHostViewModel"/>: this window reports nothing and asks nothing, so
/// a message strip would be a surface with no producer (the dead-surface trap, gotcha #233).</para>
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>Creates the view model.</summary>
    public AboutViewModel() =>
        LanguageChange.SubscribeWeak(this, static about => about.RefreshLocalizedText());

    /// <summary>
    /// The product's own name — <c>EmberTern License Manager</c>, as the build declares it.
    /// </summary>
    /// <remarks>
    /// ⭐ It names THIS application, not the product it administers, which is why
    /// <c>EmberTern.LicenseManager.csproj</c> overrides <c>&lt;Product&gt;</c>. Before that override the
    /// executable claimed its product was "EmberTern" — in its Windows file properties as well as here.
    /// </remarks>
    public string Product => ManagerInfo.Product;

    /// <summary>The window's title, "About EmberTern License Manager".</summary>
    public string Title => AboutCatalog.WindowTitle(Product);

    /// <summary>"Version 0.…" — the number comes from the build, never from a literal.</summary>
    public string VersionText => AboutCatalog.Version(ManagerInfo.Version);

    /// <summary>
    /// "Released 2026-…", or empty when the build declared no date — in which case the view hides the line
    /// rather than showing a label with nothing after it.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>ISO, invariant, and that is the License Manager's RATIFIED date form</b> — not an oversight and
    /// not a copy of the product's choice. The product renders this one date in the reader's long-date
    /// pattern; here every date is ISO, because the register stores RFC 3339 and an operator correlates what
    /// they read on screen with what they read out of <c>licenses.db</c> (§36.2, `terminology.md` §4.4).
    /// ⚠ Recorded in <c>DatePresentationTests.DeliberateIsoDisplayPaths</c> with that reason, which is what
    /// that guard asks of an author: say which side of the line the date is on.
    /// </remarks>
    public string ReleasedText => ManagerInfo.ReleaseDate is { } date
        ? AboutCatalog.Released(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        : string.Empty;

    /// <summary>Whether the build declared a release date at all.</summary>
    public bool HasReleaseDate => ManagerInfo.ReleaseDate is not null;

    /// <summary>"Created by …".</summary>
    public string AuthorText => AboutCatalog.Author(ManagerInfo.Author);

    /// <summary>The copyright notice, exactly as the build declares it. ⛔ Not a sentence we own.</summary>
    public string Copyright => ManagerInfo.Copyright;

    /// <summary>The way out.</summary>
    public string CloseText => AboutCatalog.Close;

    // ⭐ Every word this view model composes, re-read on a real language change. ⛔ Not "everything": the
    //   values that come from the build (Product, Copyright) do not move with the language, and notifying
    //   them would say that they might.
    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ReleasedText));
        OnPropertyChanged(nameof(AuthorText));
        OnPropertyChanged(nameof(CloseText));
    }
}
