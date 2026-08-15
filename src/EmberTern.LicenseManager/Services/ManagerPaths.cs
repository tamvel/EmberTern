using System;
using System.IO;

namespace EmberTern.LicenseManager.Services;

/// <summary>
/// Where the License Manager keeps its two files.
///
/// <para>⭐ The register and the keystore are SEPARATE files with separate protection, so that "back up
/// the register" and "back up the key" stay two decisions with two different risk profiles — and so that
/// handing someone the <c>.db</c> for inspection leaks nothing that can sign (§18.3).</para>
/// </summary>
public sealed class ManagerPaths
{
    /// <summary>The folder holding both files.</summary>
    public const string FolderName = "EmberTern License Manager";

    /// <summary>The register file name.</summary>
    public const string RegisterFileName = "licenses.db";

    /// <summary>The keystore file name.</summary>
    public const string KeyStoreFileName = "keystore.etkeys";

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

    /// <summary>True when a keystore already exists — i.e. the ceremony has been performed.</summary>
    public bool HasKeyStore => File.Exists(KeyStore);

    /// <summary>Makes sure the folder exists.</summary>
    public void EnsureFolder() => Directory.CreateDirectory(Root);
}
