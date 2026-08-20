using System;
using System.IO;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// Where the License Manager keeps its files.
///
/// <para>⭐ The register and the keystore are SEPARATE files with separate protection, so that "back up
/// the register" and "back up the key" stay two decisions with two different risk profiles — and so that
/// handing someone the <c>.db</c> for inspection leaks nothing that can sign (§18.3).</para>
///
/// <para>⭐ <b>L6 adds a third on the same principle.</b> The SMTP settings hold a credential protected
/// with Windows DPAPI for one account on one machine, so they belong to neither of the other two: they
/// are not part of the register (⛔ a backup deliberately does not carry them — they could not be read
/// after a restore elsewhere anyway) and they are not part of the keystore. Three files, three
/// protections, three decisions.</para>
/// </summary>
public sealed class ManagerPaths
{
    /// <summary>The folder holding the files.</summary>
    public const string FolderName = "EmberTern License Manager";

    /// <summary>The register file name.</summary>
    public const string RegisterFileName = "licenses.db";

    /// <summary>The keystore file name.</summary>
    public const string KeyStoreFileName = "keystore.etkeys";

    /// <summary>The e-mail settings file name.</summary>
    public const string SmtpSettingsFileName = "smtp.dat";

    /// <summary>The interface preferences file name.</summary>
    public const string PreferencesFileName = "ui.json";

    /// <summary>Creates a set of paths rooted at <paramref name="root"/>.</summary>
    public ManagerPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>The default location: <c>%APPDATA%\EmberTern License Manager</c>.</summary>
    public static ManagerPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName));

    /// <summary>The folder.</summary>
    public string Root { get; }

    /// <summary>The register of record.</summary>
    public string Register => Path.Combine(Root, RegisterFileName);

    /// <summary>⛔ The encrypted signing key.</summary>
    public string KeyStore => Path.Combine(Root, KeyStoreFileName);

    /// <summary>
    /// The e-mail settings, holding a DPAPI-protected SMTP password.
    ///
    /// <para>⚠ Bound to one Windows account on one machine by design — see
    /// <see cref="Email.LocalDpapiProtector"/>. That is correct for a credential and is stated in the
    /// settings window rather than left for the operator to discover.</para>
    /// </summary>
    public string SmtpSettings => Path.Combine(Root, SmtpSettingsFileName);

    /// <summary>
    /// The interface preferences — today, the application language and nothing else (L8 decision D‑4).
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>A FOURTH file, on the same principle as the other three, and it belongs to none of
    /// them.</b> It is not the register (a preference must not travel in a backup, nor follow a restore
    /// onto another machine), it is not the keystore, and ⛔ it is deliberately not <c>smtp.dat</c>: that
    /// file has ONE Save covering a whole coherent configuration, so applying a language on selection
    /// through it would mean a read-modify-write on every pick — persisting half-typed SMTP edits the
    /// operator had not committed, or re-reading the file underneath them and losing those edits (§49.3,
    /// and the audit follow-up's item E is this repository's own scar from that shape).</para>
    ///
    /// <para>⚠ Four files, four protections, four decisions. This one carries no secret, is plain text on
    /// purpose, and losing it costs one click.</para>
    /// </remarks>
    public string Preferences => Path.Combine(Root, PreferencesFileName);

    /// <summary>True when a keystore already exists — i.e. the ceremony has been performed.</summary>
    public bool HasKeyStore => File.Exists(KeyStore);

    /// <summary>Makes sure the folder exists.</summary>
    public void EnsureFolder() => Directory.CreateDirectory(Root);
}
