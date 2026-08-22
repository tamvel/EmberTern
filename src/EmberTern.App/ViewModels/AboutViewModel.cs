using System;
using System.Globalization;
using EmberTern.App.Licensing;
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
    private readonly LicenseService? _license;

    /// <summary>⚠ For the designer and for tests that are not about licensing: no licence line is shown.</summary>
    public AboutViewModel()
    {
    }

    /// <param name="license">The application's one licence service, so About names the same licensee Settings does.</param>
    internal AboutViewModel(LicenseService? license) => _license = license;

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

    /// <summary>
    /// "Licensed to ACME Sp. z o.o." — the licensee, beside the version (design §17.2, decision D6).
    ///
    /// <para>⚠ Empty when there is no payload, and the view hides the line rather than showing a label with
    /// nothing after it. ⭐ It is UX and a deterrent against careless sharing — ⛔ never a technical control.</para>
    /// </summary>
    public string LicensedToText => _license?.Verdict.Payload is { } payload
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.AboutLicensedToFormat, payload.Licensee)
        : string.Empty;

    public bool HasLicensee => !string.IsNullOrEmpty(LicensedToText);

    /// <summary>
    /// ⭐ The one Debug-only marker (design §16.5), and the reason it exists: without it a developer seeing
    /// EmberTern start with no licence cannot tell whether the gate is off BY DESIGN or broken.
    ///
    /// <para>⛔ It is deliberately NOT localized, and Architecture rule 12 is not engaged: users receive
    /// <c>Release</c> builds, where <c>GateEnabled</c> is a <c>const true</c> and the compiler folds this to
    /// <see langword="false"/> — the text can never reach a customer. Same class as
    /// <c>%TEMP%\EmberTern-debug.log</c>. Ratified in §16.5, which flagged it so it could be overruled.</para>
    /// </summary>
    public bool ShowDebugGateMarker => !LicensingPolicy.GateEnabled;

    /// <summary>⛔ Developer-facing text; see <see cref="ShowDebugGateMarker"/> for why it is not a resource.</summary>
    public string DebugGateMarker => "Debug build — licensing gate off";
}
