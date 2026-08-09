using System;
using System.Globalization;
using EmberTern.Core.Formatting;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The About window's content — a <b>projection of <see cref="AppInfo"/></b>, which is itself a projection of
/// the assembly. It holds no state, nothing is settable, and there is nothing to refresh.
///
/// <para>It exists rather than binding <c>{x:Static}</c> straight to <see cref="AppInfo"/> for one reason: each
/// value has to be composed with its label (<c>"Version …"</c>, <c>"Released …"</c>, <c>"Created by …"</c>), and
/// labels are UI strings that belong in <see cref="UiStrings"/> (architecture rule #6) while the values belong
/// to the assembly. This is the one place those two meet, and doing it here keeps the view free of formatting.</para>
///
/// <para>⛔ No version literal, here or anywhere else in the app — see <see cref="AppInfo"/>.</para>
/// </summary>
public sealed class AboutViewModel
{
    public string Product => AppInfo.Product;

    public string VersionText =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.AboutVersionFormat, AppInfo.Version);

    /// <summary>
    /// "Released 29 July 2026", or empty when the build declared no date — in which case the view hides the
    /// line rather than showing a label with nothing after it.
    /// <para>
    /// ⚠ CORRECTED 2026-08-07 (P5): this used the INVARIANT culture's <c>d MMMM yyyy</c>, justified here as
    /// "matching the rest of the window's English text". That reasoning does not survive the Language row
    /// shipping in Settings Center — and it was wrong on its own terms anyway, because it spelled an English
    /// month name on a machine whose every other date the application already rendered in the user's own
    /// format. A single prominent date is exactly the case for the reader's long-date pattern.
    /// </para>
    /// <para>⚠ The value STORED in <c>Directory.Build.props</c> stays ISO — that half is a build contract and
    /// <see cref="AppInfo"/> still parses it invariantly.</para>
    /// </summary>
    public string ReleasedText => AppInfo.ReleaseDate is { } date
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.AboutReleasedFormat,
            DateTimeDisplay.LongDate(date.ToDateTime(TimeOnly.MinValue)))
        : string.Empty;

    public bool HasReleaseDate => AppInfo.ReleaseDate is not null;

    /// <summary>
    /// "Created by Grzegorz Groński". ⚠ The label is deliberate: unlabelled, the bare name read as an unsigned
    /// line of text, and it already appears in the copyright below — the label is what makes the repetition
    /// read as authorship rather than as an accident.
    /// </summary>
    public string AuthorText =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.AboutAuthorFormat, AppInfo.Author);

    public string Copyright => AppInfo.Copyright;
}
