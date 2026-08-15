using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Controls;
using EmberTern.App.Licensing;
using EmberTern.Licensing;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Settings ▸ Licence (design §17.2) — what EmberTern makes of the licence file it found, who it is for,
/// how long it runs, and the two things a customer ever needs to do with it.
///
/// <para>⭐ <b>Every text is COMPUTED, never captured.</b> The Settings window is where the language is
/// changed, so it is the one surface guaranteed to be on screen when that happens — a value assigned in the
/// constructor would show the previous language until the window was reopened, which is exactly the defect
/// the Polish QA round found on the category list (gotcha #353).</para>
///
/// <para>⭐ <b>This page stays reachable in every state, including the blocked ones.</b> It is the way out of
/// <c>Expired</c> and <c>Unlicensed</c>: a licensing gate that also hid the screen for fixing the licence
/// would be a trap, so nothing here is conditioned on the verdict being usable.</para>
///
/// <para>⚠ The class is public because <c>SettingsCenterViewModel</c> is, but its constructor is internal —
/// <see cref="LicenseService"/> is internal on purpose and nothing outside this assembly may hand one in.</para>
/// </summary>
public sealed partial class LicenseSettingsViewModel : ObservableObject
{
    private readonly LicenseService? _license;

    /// <param name="license">
    /// ⚠ <see langword="null"/> means licensing is not wired up — a designer, or a unit test that is not about
    /// licensing. The page then shows the <c>Unlicensed</c> wording, which is the honest answer to
    /// "what licence does this copy have".
    /// </param>
    internal LicenseSettingsViewModel(LicenseService? license) => _license = license;

    /// <summary>The verdict being described. Never null — an absent service reads as <c>Unlicensed</c>.</summary>
    private LicenseVerdict Verdict => _license?.Verdict ?? LicenseVerdict.Unlicensed;

    private LicensePayload? Payload => Verdict.Payload;

    /// <summary>The state, in one phrase: <i>Licensed</i>, <i>Licence expired</i>, <i>No licence</i>, …</summary>
    public string StatusHeadline => LicenseText.Headline(Verdict);

    /// <summary>The whole sentence: what happened, why, and what to do now.</summary>
    public string StatusExplanation => LicenseText.Explain(Verdict);

    /// <summary>⭐ The shared §7 tone mapping, so this page and the main window never disagree about a state.</summary>
    public MessageSeverity StatusSeverity
        => LicenseText.SeverityOf(Verdict, _license?.IsExpiringSoon ?? false);

    /// <summary>
    /// Whether there is a licence to describe. ⚠ True for an EXPIRED licence too: an expired licence still has
    /// a licensee and dates, and hiding them is precisely when a customer needs to read them to us.
    /// </summary>
    public bool HasDetails => Payload is not null;

    public string Licensee => Payload?.Licensee ?? string.Empty;

    /// <summary>
    /// ⚠ Displayed and enforced by nothing (decision D2). The note beside it says so in the user's own
    /// language rather than letting the number imply a control that does not exist.
    /// </summary>
    public string Seats => Payload is { } payload
        ? payload.Seats.ToString(CultureInfo.CurrentCulture)
        : string.Empty;

    public string ValidFrom => Payload is { } payload ? LicenseText.Day(payload.NotBefore) : string.Empty;

    public string ValidUntil => Payload is { } payload ? LicenseText.Day(payload.ExpiresAt) : string.Empty;

    public string LicenseId => Payload?.LicenseId ?? string.Empty;

    /// <summary>Which of the two locations (§8) the verdict was read from. Support asks this first.</summary>
    public string FilePath => _license?.SourcePath ?? string.Empty;

    public bool HasFilePath => !string.IsNullOrEmpty(FilePath);

    /// <summary>Opens the activation window. The view supplies it, because a window needs an owner.</summary>
    public Func<Task>? RequestUpdate { get; set; }

    /// <summary>Puts text on the clipboard. The view supplies it, for the same reason.</summary>
    public Func<string, Task>? RequestCopy { get; set; }

    [RelayCommand]
    private async Task UpdateLicenseAsync()
    {
        if (RequestUpdate is { } request) await request();

        // ⭐ Everything on this page is computed, so re-reading is the whole of the refresh — and it has to
        //   happen, because activation may just have replaced the verdict underneath us.
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task CopyLicenseIdAsync()
    {
        if (RequestCopy is { } copy && !string.IsNullOrEmpty(LicenseId)) await copy(LicenseId);
    }

    /// <summary>
    /// ⛔ Deliberately NOT translated: this is a token for us, not a sentence for the customer. It exists so a
    /// support e-mail can carry something exact (design §9.1).
    /// </summary>
    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        if (RequestCopy is { } copy) await copy(LicenseText.Details(Verdict, _license?.SourcePath));
    }

    /// <summary>Tells every binding on this page to re-read. Called on a language change by the page's host.</summary>
    internal void RefreshLocalizedText() => OnPropertyChanged(string.Empty);
}
