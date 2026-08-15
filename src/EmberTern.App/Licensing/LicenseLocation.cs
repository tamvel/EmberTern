using System;
using System.Collections.Generic;
using System.IO;
using EmberTern.Licensing;

namespace EmberTern.App.Licensing;

/// <summary>
/// Where the licence file lives, and in what order the two locations are consulted (design §8).
///
/// <para>
/// <c>1. %APPDATA%\EmberTern\license.etlic</c> — per-user, written by activation<br/>
/// <c>2. %PROGRAMDATA%\EmberTern\license.etlic</c> — per-machine, read-only fallback
/// </para>
///
/// <para>⭐ <b>First match wins, and the per-user file always shadows the machine file.</b> The machine
/// path exists because of decision D8 — *"in some cases we will install the licence at the customer's site
/// ourselves"* — and it also covers shared workstations and terminal servers. Activation only ever writes
/// the per-user file; ⛔ nothing in EmberTern writes to <c>%PROGRAMDATA%</c>, which usually needs elevation
/// and is not the application's to modify.</para>
///
/// <para>⛔ <b>The licence is deliberately NOT stored inside <c>settings.dat</c>.</b> It has to survive a
/// settings reset, be copyable by support, and be readable without EmberTern — none of which is true of an
/// encrypted blob. ⚠ The one licensing value that *does* live in <c>settings.dat</c> is the clock
/// high-water mark, and for the opposite reason: that one is ours, not the customer's.</para>
///
/// <para>⚠ Paths are resolved through <see cref="Environment.SpecialFolder"/> rather than composed from
/// environment variables, so a redirected or roaming profile resolves correctly.</para>
/// </summary>
internal sealed class LicenseLocation
{
    /// <summary>The folder EmberTern already keeps its per-user files in.</summary>
    internal const string FolderName = "EmberTern";

    private readonly string _userDirectory;
    private readonly string _machineDirectory;

    /// <summary>Creates a location over explicit directories. ⚠ For tests — production uses <see cref="Default"/>.</summary>
    internal LicenseLocation(string userDirectory, string machineDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineDirectory);

        _userDirectory = userDirectory;
        _machineDirectory = machineDirectory;
    }

    /// <summary>The real locations: <c>%APPDATA%\EmberTern</c> and <c>%PROGRAMDATA%\EmberTern</c>.</summary>
    internal static LicenseLocation Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FolderName));

    /// <summary>⭐ The per-user file — the only one activation ever writes.</summary>
    internal string UserPath => Path.Combine(_userDirectory, LicenseConstants.StoredFileName);

    /// <summary>⛔ The per-machine file — read-only to EmberTern.</summary>
    internal string MachinePath => Path.Combine(_machineDirectory, LicenseConstants.StoredFileName);

    /// <summary>Both paths, in the order they are consulted.</summary>
    internal IEnumerable<string> SearchOrder
    {
        get
        {
            yield return UserPath;
            yield return MachinePath;
        }
    }

    /// <summary>
    /// The first existing licence file, or <see langword="null"/> when neither is present.
    ///
    /// <para>⚠ Answers with the PATH, not the text: which file was used is something support asks about, and
    /// a reader that returns only the content cannot say.</para>
    /// </summary>
    internal string? ResolveExisting()
    {
        foreach (var path in SearchOrder)
        {
            // ⚠ A path we cannot even test (a disconnected redirected profile, a denied ACL) is treated as
            //    "not here" and the search continues — it must never take the application down.
            try
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (IOException)
            {
                // Continue to the next candidate.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue to the next candidate.
            }
        }

        return null;
    }

    /// <summary>Makes sure the per-user folder exists, so activation can write into it.</summary>
    internal void EnsureUserFolder() => Directory.CreateDirectory(_userDirectory);
}
