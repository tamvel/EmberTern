using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Controls;
using EmberTern.App.Licensing;

namespace EmberTern.App.ViewModels;

/// <summary>
/// ⭐ The activation window's content (design §5): one buffer fed by three gestures — drop, Browse, paste —
/// and one <c>[Activate]</c> that acts on it.
///
/// <para>⭐ <b>The three gestures share ONE buffer on purpose.</b> A dropped file and a browsed file are read
/// into <see cref="PasteText"/> rather than kept aside, so there is exactly one thing <c>Activate</c> can act
/// on and the user can SEE what they are about to install. Three sources feeding three code paths is how a
/// paste ends up verified by different code from a drop.</para>
///
/// <para>⛔ Nothing here writes the licence file: <see cref="LicenseService.Install"/> owns the write, and it
/// re-reads and re-verifies from disk before answering — a half-succeeded write has to be found now, with the
/// file still on the user's desktop (§5, Architecture rule 11).</para>
///
/// <para>⚠⚠ Every message is resolved HERE, at the moment of display, through <see cref="LicenseText"/> or
/// <see cref="UiStrings"/>. ⛔ Never from an exception's <c>Message</c> — design §17.3.</para>
/// </summary>
internal sealed partial class LicenseActivationViewModel : ObservableObject
{
    private readonly LicenseService _license;

    internal LicenseActivationViewModel(LicenseService license)
        => _license = license ?? throw new ArgumentNullException(nameof(license));

    /// <summary>
    /// The artifact on offer, whatever gesture supplied it. Two-way for the paste box; a drop and a Browse
    /// write into it as well.
    /// </summary>
    [ObservableProperty]
    private string _pasteText = string.Empty;

    /// <summary>What the banner is saying. Empty until the user does something.</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private MessageSeverity _severity = MessageSeverity.Info;

    [ObservableProperty]
    private bool _hasMessage;

    /// <summary>
    /// ⭐ True when the offered licence carries a DIFFERENT licence id from the installed one (§16.4). The
    /// window then offers <c>[Replace]</c> — moving a machine onto another licence is legitimate, but it is a
    /// decision, not a default.
    /// </summary>
    [ObservableProperty]
    private bool _needsReplaceConfirmation;

    /// <summary>True once a licence has been installed and re-verified from disk. The window's cue to finish.</summary>
    [ObservableProperty]
    private bool _isActivated;

    /// <summary>The verdict now in force — what About and Settings ▸ Licence will show.</summary>
    internal LicenseService License => _license;

    /// <summary>
    /// Reads a file the user dropped or picked into the one buffer.
    ///
    /// <para>⚠ A file that cannot be read is a MESSAGE, never an exception out of this method: the user
    /// dropped something and deserves to be told what happened to it.</para>
    /// </summary>
    internal void OfferFile(string path)
    {
        NeedsReplaceConfirmation = false;

        try
        {
            PasteText = File.ReadAllText(path);
            Say(MessageSeverity.Info, UiStrings.LicenseActivationIntro);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Say(MessageSeverity.Error,
                string.Format(CultureInfo.CurrentCulture, UiStrings.LicenseActivationUnreadableFormat, path));
        }
    }

    [RelayCommand]
    private void Activate() => Install(confirmedDifferentLicense: false);

    /// <summary>⭐ The explicit branch of the freshness rule — the user has agreed to replace a different licence.</summary>
    [RelayCommand]
    private void Replace() => Install(confirmedDifferentLicense: true);

    private void Install(bool confirmedDifferentLicense)
    {
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            NeedsReplaceConfirmation = false;
            Say(MessageSeverity.Warning, UiStrings.LicenseActivationNothing);
            return;
        }

        var result = _license.Install(PasteText, confirmedDifferentLicense);
        NeedsReplaceConfirmation = result.Outcome == LicenseInstallOutcome.DifferentLicenseNeedsConfirmation;

        switch (result.Outcome)
        {
            case LicenseInstallOutcome.Installed:
                IsActivated = true;
                // ⭐ The verdict READ BACK FROM DISK — so what the user is congratulated with is what the next
                //    launch will find, not what was in memory a moment ago.
                Say(MessageSeverity.Success, LicenseText.Explain(result.Verdict));
                break;

            case LicenseInstallOutcome.NotNewer:
                Say(MessageSeverity.Warning, UiStrings.LicenseActivationNotNewer);
                break;

            case LicenseInstallOutcome.DifferentLicenseNeedsConfirmation:
                Say(MessageSeverity.Warning, UiStrings.LicenseActivationDifferentLicense);
                break;

            case LicenseInstallOutcome.NotStored:
                Say(MessageSeverity.Error,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        UiStrings.LicenseActivationNotStoredFormat,
                        _license.InstallPath));
                break;

            default:
                // ⭐ The verdict's own sentence: what happened, why, and what to do now.
                Say(MessageSeverity.Error, LicenseText.Explain(result.Verdict));
                break;
        }
    }

    private void Say(MessageSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
        HasMessage = !string.IsNullOrWhiteSpace(message);
    }
}
