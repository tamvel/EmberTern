using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Licensing.Issuing;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// The License Manager's own first run: create the signing key, or unlock it.
///
/// <para>⛔ <b>The passphrase is held only for as long as it takes to open the keystore.</b> It is never
/// persisted, never cached, and cleared from this view model the moment the session exists. ⚠ That is
/// hygiene, not a guarantee — a string in managed memory cannot be scrubbed reliably — and the design
/// says so plainly rather than implying more (§24.2).</para>
/// </summary>
public sealed partial class UnlockViewModel : MessageHostViewModel
{
    private readonly ManagerPaths _paths;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Raised once a session exists. The shell takes it from here.</summary>
    public event Action<SigningSession>? Unlocked;

    /// <summary>Creates the view model.</summary>
    public UnlockViewModel(ManagerPaths paths, Func<DateTimeOffset>? clock = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        HasKeyStore = paths.HasKeyStore;
    }

    /// <summary>True when a keystore already exists, so this is an unlock rather than a ceremony.</summary>
    [ObservableProperty]
    private bool _hasKeyStore;

    /// <summary>The key id for a new key. ⭐ <c>R1</c> — "root, first" — matches Appendix A.</summary>
    [ObservableProperty]
    private string _keyId = "R1";

    /// <summary>What the operator typed.</summary>
    [ObservableProperty]
    private string _passphrase = string.Empty;

    /// <summary>Typed again, for a ceremony only.</summary>
    [ObservableProperty]
    private string _passphraseConfirmation = string.Empty;

    /// <summary>
    /// What this window is asking for, as its heading.
    ///
    /// <para>⭐ The heading names the TASK. The product is named by the title bar and its icon, so
    /// spending the window's largest type on the application name said nothing the window had not
    /// already said, and left the operator to work out which of the two modes they were in from the
    /// body text.</para>
    /// </summary>
    public string Headline => HasKeyStore ? "Unlock the keystore" : "Create the signing key";

    // ⛔ There is deliberately no `Location` property and the storage path is NOT shown on this screen
    //    (user review, 2026-08-15). It is infrastructure: it does not help anyone perform the one
    //    action this window exists for, and a path is the kind of detail that reads as something the
    //    operator is supposed to do something about. Where the files live is an administrative
    //    question, and it belongs to an administrative surface — an "Open data folder" action or a
    //    storage section — not to first run. Recorded as an L5 item in the design document.

    /// <summary>Opens an existing keystore.</summary>
    [RelayCommand]
    private void Unlock()
    {
        if (Passphrase.Length == 0)
        {
            Message = StatusMessage.Warning(StatusCatalog.EnterKeystorePassphrase);
            return;
        }

        try
        {
            Complete(SigningSession.Unlock(_paths, Passphrase));
        }
        catch (KeyStoreException e)
        {
            // ⭐ The classified failure is what lets the operator be told to RETYPE rather than sent to
            //    the offline backup. That distinction is the reason the keystore uses an authenticated
            //    cipher at all — throwing it away here would waste it.
            Message = e.Failure switch
            {
                KeyStoreFailure.WrongPassphrase => StatusMessage.Error(StatusCatalog.PassphraseDoesNotOpenKeystore),
                KeyStoreFailure.NotAKeyStore => StatusMessage.Error(StatusCatalog.NotAKeystore, _paths.KeyStore),
                KeyStoreFailure.UnsupportedVersion => StatusMessage.Error(StatusCatalog.KeystoreFromNewerBuild),
                KeyStoreFailure.Corrupt => StatusMessage.Error(StatusCatalog.KeystoreDamaged),
                _ => StatusMessage.Error(StatusCatalog.KeystoreNotOpened, e.Failure),
            };
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Error(StatusCatalog.KeystoreNotRead, e.Message);
        }
    }

    /// <summary>Performs the ceremony and writes the keystore.</summary>
    [RelayCommand]
    private void CreateKey()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
        {
            Message = StatusMessage.Warning(StatusCatalog.KeyIdRequired);
            return;
        }

        if (Passphrase.Length < 12)
        {
            // ⚠ A length floor, not a complexity ruleset. The passphrase is the ONLY thing between an
            //    attacker with the file and the ability to mint licences, and it is meant to be six
            //    generated words rather than something anyone types from memory (§24.1).
            Message = StatusMessage.Warning(StatusCatalog.NewKeyPassphraseHint);
            return;
        }

        if (!string.Equals(Passphrase, PassphraseConfirmation, StringComparison.Ordinal))
        {
            Message = StatusMessage.Warning(StatusCatalog.PassphrasesDoNotMatch);
            return;
        }

        try
        {
            Complete(SigningSession.Create(_paths, KeyId.Trim(), Passphrase, _clock()));
        }
        catch (InvalidOperationException e)
        {
            // ⭐ The refusal is OURS — SigningSession throws it carrying its catalog key — so it RESOLVES
            //   rather than being printed. Handing e.Message to the strip here is exactly how a perfectly
            //   translated sentence stays English forever (see StatusMessage.FromError).
            Message = StatusMessage.FromError(e, MessageSeverity.Error);
        }
        catch (System.IO.IOException e)
        {
            Message = StatusMessage.Error(StatusCatalog.KeystoreNotWritten, e.Message);
        }
    }

    private void Complete(SigningSession session)
    {
        Passphrase = string.Empty;
        PassphraseConfirmation = string.Empty;
        Message = null;
        Unlocked?.Invoke(session);
    }
}
