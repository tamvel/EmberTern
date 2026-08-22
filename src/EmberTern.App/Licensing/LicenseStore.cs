using System;
using System.IO;
using System.Text;
using EmberTern.Licensing;

namespace EmberTern.App.Licensing;

/// <summary>
/// Reading and writing the licence file on disk.
///
/// <para>⭐⭐ <b><see cref="Install"/> takes the verifier as a parameter and returns the verdict it read
/// BACK FROM DISK — the re-read is not something a caller can forget, because there is no way to write
/// without it.</b> Design §5 calls this Architecture rule 11 rather than paranoia: if the write half
/// succeeded, the user has to find out *now*, with the file still on their desktop, and not at the next
/// launch with the e-mail already deleted.</para>
///
/// <para>⭐ The write is atomic — temporary file, then replace — so an interrupted write cannot leave a
/// half-written licence where a working one used to be. Same shape as the keystore write in the License
/// Manager, and for the same reason.</para>
///
/// <para>⚠ UTF-8 with <b>no BOM</b> (gotcha #178). The armoured token is pure ASCII, so this is about the
/// bytes we do not add: a BOM would be carried into the signed-input reconstruction by anything that reads
/// the file as text.</para>
/// </summary>
internal static class LicenseStore
{
    /// <summary>
    /// The licence text at <paramref name="path"/>, or <see langword="null"/> when it cannot be read.
    ///
    /// <para>⚠ An unreadable file answers <see langword="null"/> — the same as an absent one — and the
    /// caller turns that into <c>Unlicensed</c>. ⛔ It must never throw out of startup: a licence file
    /// locked by a backup agent is not a reason to refuse to launch.</para>
    /// </summary>
    internal static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/>, then reads it back and verifies the file
    /// that is actually on disk.
    /// </summary>
    /// <param name="path">Where to write. Its folder must already exist.</param>
    /// <param name="text">The licence text as the user supplied it.</param>
    /// <param name="verify">The verification to apply to what comes back off the disk.</param>
    /// <returns>The verdict for the STORED file — never for the in-memory text.</returns>
    /// <exception cref="IOException">The write failed, or the file could not be read back.</exception>
    internal static LicenseVerdict Install(string path, string text, Func<string, LicenseVerdict> verify)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(verify);

        WriteAtomic(path, text);

        // ⭐ Deliberately NOT `TryRead`: here a failure to read back is a real failure of the install and
        //    must be reported, whereas at startup an unreadable file is simply "no licence".
        var stored = File.ReadAllText(path, Encoding.UTF8);
        return verify(stored);
    }

    /// <summary>
    /// ⭐ Temporary file, then replace — so an interrupted write cannot destroy the licence that was there.
    /// </summary>
    private static void WriteAtomic(string path, string text)
    {
        var temporary = path + ".tmp";

        // ⚠ UTF8Encoding(false), never Encoding.UTF8 — the latter emits a BOM (#178).
        File.WriteAllText(temporary, text, new UTF8Encoding(false));

        if (File.Exists(path))
        {
            // ⚠ Null destination-backup: we do not want a `.bak` accumulating beside the licence. The
            //    replace is still atomic; only the rollback copy is declined.
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
