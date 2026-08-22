using System;
using System.Security.Cryptography;
using System.Text;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// Windows DPAPI, CurrentUser scope — this application's own, deliberately local implementation.
///
/// <para>⭐⭐ <b>Why this exists rather than reusing EmberTern's.</b> The design (§14.3) says the SMTP
/// password is protected "exactly as EmberTern protects connection passwords", naming
/// <c>DpapiSecretProtector</c>. That class lives in <c>EmberTern.App</c> and reaches
/// <c>EmberTern.Core.Security.SecretProtector</c> and <c>UiStrings</c> — so "the same mechanism" here
/// means <b>the same construction</b>, not the same code. ⛔ The License Manager references neither
/// project and must not start to: it is the application that can sign, and dragging the product's world
/// into it would be a new coupling bought for forty lines.</para>
///
/// <para>⭐ <b>The entropy is this application's own, and that is load-bearing.</b> DPAPI entropy is not a
/// secret — it ships in the binary — but it namespaces the blobs, so a value protected here cannot be
/// unprotected by EmberTern and vice versa. ⛔ Do not reuse <c>EmberTern.App</c>'s
/// <c>"EmberTern.v1.secret"</c>: two applications sharing one namespace would make their at-rest
/// secrets interchangeable, which is the opposite of what separate files with separate protection
/// (see <see cref="Services.ManagerPaths"/>) exist to achieve.</para>
///
/// <para>⚠⚠ <b>The <see cref="OperatingSystem.IsWindows"/> guards are required by the compiler, not by
/// taste.</b> Measured 2026-08-19 against this repository's own build settings: without them the platform
/// analyzer raises <b>CA1416</b> on every <see cref="ProtectedData"/> member, and with
/// <c>TreatWarningsAsErrors=true</c> that is four build ERRORS, not four warnings. The negative control
/// was run — removing the guards fails the build, adding them back gives 0/0 — so this is a measured
/// requirement rather than a pattern copied from <c>EmberTern.App</c> on faith.</para>
///
/// <para>⚠ DPAPI CurrentUser is deliberately <b>not</b> portable across machines or Windows accounts.
/// That is correct for a credential and wrong to hide: the settings window says so in words, exactly as
/// EmberTern does for connection profiles.</para>
/// </summary>
public static class LocalDpapiProtector
{
    /// <summary>
    /// ⭐ This application's DPAPI namespace. NOT a secret — it ships in the binary — but it must differ
    /// from every other application's, so that a blob protected here is meaningless to them.
    ///
    /// <para>⛔ Changing this string makes every previously stored secret unreadable. That is survivable
    /// for an SMTP password (the operator retypes it) and is why <see cref="TryUnprotect"/> reports
    /// failure rather than throwing.</para>
    /// </summary>
    public const string EntropyLabel = "EmberTern.LicenseManager.v1.smtp";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(EntropyLabel);

    /// <summary>Plaintext → base64 ciphertext. An empty input stays empty (there is no secret to hide).</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        // ⚠⚠ INLINE, and it must stay inline. CA1416 recognises `OperatingSystem.IsWindows()` written
        //    here; it does NOT follow a call into a `RequireWindows()` helper, so extracting these four
        //    lines turns the next build red. ⛔ And the alternative — annotating the method
        //    `[SupportedOSPlatform("windows")]` — propagates to every caller and then to every test,
        //    which is precisely why EmberTern.App's DpapiSecretProtector writes it exactly like this.
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnly);
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Base64 ciphertext → plaintext, reporting failure rather than throwing.
    ///
    /// <para>⭐ <b>Failure here is an ORDINARY state, not an error.</b> A settings file carried to another
    /// machine or another Windows account decrypts to nothing at all, and the honest response is to show
    /// an empty password box and say why — not to tear down the window the operator opened to fix it.
    /// ⚠ So this returns a verdict; ⛔ callers must not turn it back into an exception.</para>
    /// </summary>
    public static bool TryUnprotect(string stored, out string plaintext)
    {
        plaintext = string.Empty;

        if (string.IsNullOrEmpty(stored))
        {
            return true;
        }

        // ⚠⚠ Inline for the reason spelled out in Protect — CA1416 does not follow a helper call.
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnly);
        }

        try
        {
            var encrypted = Convert.FromBase64String(stored);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // CryptographicException: another user, another machine, or a corrupted blob.
            // FormatException: the stored text is not base64 at all — a hand-edited file.
            return false;
        }
    }

    private const string WindowsOnly =
        "The License Manager stores its secrets with Windows DPAPI and runs on Windows only.";
}
