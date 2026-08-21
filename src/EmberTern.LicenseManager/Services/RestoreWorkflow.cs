using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Services;

/// <summary>Why a restore was refused. ⚠ Every value means "nothing was written".</summary>
public enum RestoreRefusal
{
    /// <summary>⛔ The target is the folder the running License Manager keeps its register in.</summary>
    TargetIsTheActiveRegister,

    /// <summary>⛔ The target already holds files. A restore never merges into anything.</summary>
    TargetIsNotEmpty,

    /// <summary>The target path is unusable — blank, or a file rather than a folder.</summary>
    TargetIsNotUsable,

    /// <summary>The restored register failed <see cref="LicenseRegister.CheckIntegrity"/>.</summary>
    RestoredRegisterIsInconsistent,

    /// <summary>The backup decrypted into something that is not a register at all.</summary>
    BackupIsNotARegister,

    /// <summary>
    /// ⛔ The active register file is still held open, so it cannot be replaced.
    ///
    /// <para>⚠ Measured rather than trusted: the workflow opens the file exclusively to find out. Asking
    /// the caller whether it closed the register would be believing a claim about the one fact that must
    /// be true before anything is moved.</para>
    /// </summary>
    ActiveRegisterIsStillOpen,

    /// <summary>
    /// ⛔ The existing register could not be preserved, so it was not replaced. Nothing was moved.
    /// </summary>
    PreservedCopyFailed,

    /// <summary>
    /// ⛔ The register was materialised and then failed its final check, so it was rolled back and the
    /// preserved copy put back in place.
    /// </summary>
    RestoredRegisterFailedFinalCheck,
}

/// <summary>
/// A restore was refused, and the active register was not touched.
///
/// <para>⭐ Refusal is the normal outcome of a bad input here, not an exceptional one — which is why it
/// carries a reason the surface can act on rather than only a sentence.</para>
/// </summary>
/// <remarks>
/// ⚠⚠ <b>It carries a <see cref="MessageKey"/> of its own, and <see cref="Refusal"/> could NOT have served
/// instead — that is measured, not assumed.</b> Three refusal values cover more than one sentence
/// (<c>ActiveRegisterIsStillOpen</c> has two, <c>RestoredRegisterFailedFinalCheck</c> two,
/// <c>TargetIsNotUsable</c> three), so keying off the enum would have silently collapsed distinct
/// sentences into one — a change to what the operator reads, which L8.2 may not make. ⭐ The enum stays
/// exactly as it was: it answers "what should the SURFACE do", the key answers "what does it SAY", and
/// those are different questions.
/// </remarks>
public sealed class RestoreRefusedException : Exception, ILocalizedError
{
    /// <summary>Creates the exception.</summary>
    /// <param name="refusal">Why — for the surface to act on.</param>
    /// <param name="key">The catalog key for the sentence the operator reads.</param>
    /// <param name="message">⚠ The same sentence in English, for diagnostics only. ⛔ Never displayed.</param>
    /// <param name="problems">The integrity problems found, when that is the reason.</param>
    /// <param name="inner">The underlying failure, when there is one.</param>
    public RestoreRefusedException(
        RestoreRefusal refusal,
        MessageKey key,
        string message,
        IReadOnlyList<LocalizedText>? problems = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Refusal = refusal;
        Key = key;
        Problems = problems ?? [];
    }

    /// <summary>Why.</summary>
    public RestoreRefusal Refusal { get; }

    /// <inheritdoc />
    public MessageKey Key { get; }

    /// <inheritdoc />
    /// <remarks>⚠ This exception's sentence takes no arguments; the detail is in <see cref="Problems"/>.</remarks>
    public IReadOnlyList<object?> Arguments => [];

    /// <summary>The integrity problems found, when that is the reason.</summary>
    public IReadOnlyList<LocalizedText> Problems { get; }
}

/// <summary>What a completed restore produced.</summary>
/// <param name="Directory">The folder holding the restored register.</param>
/// <param name="RegisterPath">The restored <c>licenses.db</c>.</param>
/// <param name="BackupCreatedAt">When the backup had been taken.</param>
/// <param name="SchemaVersion">The schema of the restored register.</param>
/// <param name="Counts">What it holds.</param>
/// <param name="PreservedRegisterPath">
/// ⭐ Where the register that used to be here was kept, when this replaced an active one;
/// <see langword="null"/> for a restore into another location, which replaced nothing. ⛔ Never deleted
/// by this application (D‑7).
/// </param>
public sealed record RestoreReport(
    string Directory,
    string RegisterPath,
    DateTimeOffset BackupCreatedAt,
    int SchemaVersion,
    SnapshotCounts Counts,
    string? PreservedRegisterPath = null);

/// <summary>
/// Restoring a register from an encrypted backup, in two explicit modes.
///
/// <para>⭐⭐ <b>ONE core, two endings.</b> <see cref="Stage"/> does everything both modes share — read the
/// header, decrypt, build the register in a private temporary folder, and run the register's own
/// <see cref="LicenseRegister.CheckIntegrity"/> over it. ⛔ Nothing on the operator's disk is touched
/// until that has passed. A second restore path would be a second place for that gate to be forgotten,
/// which is why the branch comes as late as it possibly can.</para>
///
/// <list type="bullet">
/// <item><see cref="Restore"/> — <b>into another location.</b> A new or empty folder of the operator's
/// choosing. ⛔ It REFUSES the active register's folder by identity, refuses a folder that is not empty,
/// and takes a directory rather than a file path, so no argument it accepts can mean <i>"write over that
/// database"</i>. For recovering or inspecting a backup while the working register carries on.</item>
/// <item><see cref="RestoreOverActiveRegister"/> — <b>replacing the active register.</b> The one
/// operation that writes into that folder, and the previous register is MOVED ASIDE and kept before it
/// does (D‑7). It refuses outright while the file is still held open, and it verifies again AFTER
/// materialising: if what is on disk is not what was approved, the previous register is put back.</item>
/// </list>
///
/// <para>⭐ <b>What the safety rests on is preservation and verification, not a warning.</b> The operator
/// cannot end a failed replace without a working register: either the new one passed both checks, or the
/// one they started with is back in place, or — if even the roll-back failed — it is on disk under the
/// name the refusal message gives them. ⛔ Nothing here deletes a preserved copy, ever.</para>
///
/// <para>⛔ <b>It holds no <see cref="LicenseRegister"/>, in either mode.</b> It is constructed with a
/// path, so there is no object here through which the RUNNING application's register could be written to
/// or audited — which is how D‑5 stays a structural fact rather than a remembered rule.
/// <c>RestoreWorkflowTests</c> asserts it by reflection, with a positive control, because it is exactly
/// the kind of property a future convenience constructor removes quietly.</para>
///
/// <para>⚠ <b>Replacing does not switch the running application over</b> (D‑6). The caller closes the
/// register before calling, and shuts the application down after; the next start reads the restored file.
/// ⛔ Reopening and re-pointing the view models is a separate stage and is deliberately not attempted
/// here.</para>
/// </summary>
public sealed class RestoreWorkflow
{
    private readonly string _activeRegisterFolder;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Creates the workflow.
    /// </summary>
    /// <param name="activeRegisterFolder">
    /// ⭐ The folder the running application keeps its register in. <see cref="Restore"/> REFUSES it as a
    /// target; <see cref="RestoreOverActiveRegister"/> is the one operation that writes there, and only
    /// after preserving what is already in it. ⛔ Neither opens the running application's register — this
    /// class holds no <see cref="LicenseRegister"/> at all, which is what keeps a restore from being able
    /// to write to one (D‑5).
    /// </param>
    /// <param name="clock">Stamps the preserved copy's name.</param>
    public RestoreWorkflow(string activeRegisterFolder, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeRegisterFolder);
        _activeRegisterFolder = activeRegisterFolder;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Reads what a backup file claims about itself, without a passphrase.</summary>
    /// <exception cref="BackupException">It is not a readable backup.</exception>
    public static BackupHeader Inspect(byte[] backup) => RegisterBackup.ReadHeader(backup);

    /// <summary>
    /// Restores <paramref name="backup"/> into <paramref name="targetDirectory"/>, which must be new or
    /// empty, and which may never be the active register's folder.
    /// </summary>
    /// <param name="backup">The encrypted backup file's bytes.</param>
    /// <param name="passphrase">⭐ The BACKUP's own passphrase (D‑1).</param>
    /// <param name="targetDirectory">A new or empty folder. ⛔ Never a file path.</param>
    /// <exception cref="BackupException">Wrong passphrase, damaged file, or an unknown format.</exception>
    /// <exception cref="RestoreRefusedException">See <see cref="RestoreRefusal"/>.</exception>
    public RestoreReport Restore(byte[] backup, string passphrase, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(backup);

        // ⭐ The target is resolved BEFORE anything is decrypted, so a mistyped folder costs nothing and
        //    says why. ⛔ This is also where the active register's folder is refused — the mode below
        //    does not come through here, which is what keeps this guard at full strength.
        var target = ResolveTarget(targetDirectory);

        using var staged = Stage(backup, passphrase);
        var registerPath = Materialise(staged.Staging.FilePath, target);

        return new RestoreReport(
            target, registerPath, staged.Header.CreatedAt, staged.SchemaVersion, staged.Counts);
    }

    /// <summary>
    /// Replaces the ACTIVE register with the one in <paramref name="backup"/>, keeping the current one.
    ///
    /// <para>⭐⭐ <b>The existing register is never deleted and never overwritten — it is MOVED ASIDE</b>
    /// to <c>licenses.db.replaced-&lt;stamp&gt;</c> in the same folder, and every such copy is kept
    /// (D‑7). Cleaning them up is the operator's decision, taken with the files in front of them.</para>
    ///
    /// <para>⭐ <b>The order is what makes this safe</b>, and it is the same contract the other mode has,
    /// with two steps added at the end:</para>
    ///
    /// <list type="number">
    /// <item>the register file is proved to be RELEASED — by opening it exclusively, not by believing a
    /// caller who says it closed it;</item>
    /// <item>decrypt, stage in a private folder, and run the register's own
    /// <see cref="LicenseRegister.CheckIntegrity"/> — ⛔ nothing on disk has been touched yet;</item>
    /// <item><b>preserve</b>: the current register is moved aside. A move rather than a copy, because it
    /// is atomic within a volume and leaves no window holding half a file;</item>
    /// <item><b>materialise</b>: the staged register is moved into place;</item>
    /// <item><b>verify again</b>: the register now on disk is opened and must both pass the integrity
    /// check and reproduce the staged content row for row. ⛔ If it does not, it is removed and the
    /// preserved copy is moved back — the operator ends the failed restore with the register they
    /// started with.</item>
    /// </list>
    ///
    /// <para>⚠ <b>This does not switch the running application over.</b> The register it holds is closed
    /// by the caller before this runs and is not reopened here (D‑6): the caller shuts the application
    /// down, and the next start reads the restored file. ⛔ Reopening and re-pointing the view models is
    /// a separate stage and is deliberately not attempted.</para>
    /// </summary>
    /// <param name="backup">The encrypted backup file's bytes.</param>
    /// <param name="passphrase">⭐ The BACKUP's own passphrase (D‑1).</param>
    /// <exception cref="BackupException">Wrong passphrase, damaged file, or an unknown format.</exception>
    /// <exception cref="RestoreRefusedException">See <see cref="RestoreRefusal"/>.</exception>
    public RestoreReport RestoreOverActiveRegister(byte[] backup, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(backup);

        var registerPath = Path.Combine(_activeRegisterFolder, ManagerPaths.RegisterFileName);
        EnsureReleased(registerPath);

        using var staged = Stage(backup, passphrase);

        // ⛔ Preserve FIRST. Until this succeeds the register on disk is the operator's own, untouched,
        //    and every failure above this line has left it that way.
        var preserved = Preserve(registerPath);

        try
        {
            Directory.CreateDirectory(_activeRegisterFolder);
            File.Move(staged.Staging.FilePath, registerPath);
            VerifyMaterialised(registerPath, staged);
        }
        catch
        {
            RollBack(registerPath, preserved);
            throw;
        }

        return new RestoreReport(
            _activeRegisterFolder, registerPath, staged.Header.CreatedAt,
            staged.SchemaVersion, staged.Counts, preserved);
    }

    // ── The shared core ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Everything both modes do before anything on disk is touched: read the header, decrypt, build the
    /// register in a private temporary folder, and refuse it if it disagrees with itself.
    ///
    /// <para>⭐ ONE core, branching only where the two modes genuinely differ — at the target and at the
    /// materialisation. A second restore path would be a second place for the integrity gate to be
    /// forgotten.</para>
    ///
    /// <para>⚠ The staged register is CLOSED on return, with its file left in place: SQLite pools
    /// connections, so a register that has only been disposed still holds its file and cannot be moved
    /// (see <see cref="LicenseRegister.Dispose"/>). The returned object still owns the folder and removes
    /// it on disposal, including when the file has already been moved out of it.</para>
    /// </summary>
    private static StagedRestore Stage(byte[] backup, string passphrase)
    {
        // ⭐ The header is read before the passphrase is used, so an unreadable format is reported as
        //    such rather than as a failed decryption the operator would blame on their typing.
        var header = RegisterBackup.ReadHeader(backup);
        var snapshot = RegisterBackup.Open(backup, passphrase);

        var staging = OpenStaging(snapshot);

        try
        {
            var problems = staging.Register.CheckIntegrity();
            if (problems.Count > 0)
            {
                // ⛔ Reported, never repaired. A restore that quietly fixed what it found would hand the
                //    operator a register whose history nobody can trust — and would do it at the exact
                //    moment they are least able to notice.
                throw new RestoreRefusedException(
                    RestoreRefusal.RestoredRegisterIsInconsistent,
                    StatusCatalog.RestoreBackupInconsistent,
                    "The backup restored into a register that disagrees with itself, so nothing was written.",
                    problems);
            }

            var staged = new StagedRestore(
                staging,
                header,
                SnapshotCounts.Read(staging.Register),
                staging.Register.SchemaVersion,
                staging.Register.DumpContent());

            staging.Close();
            return staged;
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    private sealed record StagedRestore(
        StagedRegister Staging,
        BackupHeader Header,
        SnapshotCounts Counts,
        int SchemaVersion,
        IReadOnlyList<string> Content) : IDisposable
    {
        public void Dispose() => Staging.Dispose();
    }

    // ── Replacing the active register ───────────────────────────────────────────────────────────────

    // ⭐ MEASURED, not asked. A caller that has forgotten to close the register would otherwise be
    //   believed right up to the moment File.Move fails — half way through, with the previous register
    //   already moved aside.
    private static void EnsureReleased(string registerPath)
    {
        if (!File.Exists(registerPath))
        {
            return;   // Nothing to replace; the restore simply creates it.
        }

        try
        {
            using var probe = new FileStream(
                registerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.ActiveRegisterIsStillOpen,
                StatusCatalog.RestoreRegisterStillOpen,
                "The register is still open, so it was not replaced. Close the License Manager's " +
                "register first — nothing has been changed.",
                inner: e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.ActiveRegisterIsStillOpen,
                StatusCatalog.RestoreRegisterNotOpenable,
                "The register file cannot be opened for replacement, so nothing was changed.",
                inner: e);
        }
    }

    /// <summary>
    /// Moves the current register aside and returns where it went, or <see langword="null"/> when there
    /// was none.
    /// </summary>
    private string? Preserve(string registerPath)
    {
        if (!File.Exists(registerPath))
        {
            return null;
        }

        var stamp = _clock().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = registerPath + ".replaced-" + stamp;

        // ⚠ Two restores in the same second must not make the second one overwrite the first one's
        //    preserved copy. ⛔ Nothing here may ever land on an existing file (D‑7).
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = registerPath + ".replaced-" + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        try
        {
            File.Move(registerPath, candidate);
            return candidate;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.PreservedCopyFailed,
                StatusCatalog.RestorePreservedCopyFailed,
                "The existing register could not be kept safe, so it was not replaced. " +
                "Nothing has been changed.",
                inner: e);
        }
    }

    // ⭐ The FINAL verification: what is on disk now must pass the integrity check AND reproduce the
    //   staged register row for row. The second half is the one that matters — an integrity check
    //   answers "is this self-consistent", not "is this the register we just approved".
    private static void VerifyMaterialised(string registerPath, StagedRestore staged)
    {
        IReadOnlyList<string> content;
        IReadOnlyList<LocalizedText> problems;

        using (var restored = LicenseRegister.Open(registerPath))
        {
            problems = restored.CheckIntegrity();
            content = restored.DumpContent();
        }

        if (problems.Count > 0)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.RestoredRegisterFailedFinalCheck,
                StatusCatalog.RestoreFinalCheckFailed,
                "The restored register failed its final check, so the previous one was put back.",
                problems);
        }

        if (content.Count != staged.Content.Count)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.RestoredRegisterFailedFinalCheck,
                StatusCatalog.RestoreDoesNotMatchVerified,
                "The restored register does not match what was verified, so the previous one was put back.");
        }

        for (var i = 0; i < content.Count; i++)
        {
            if (!string.Equals(content[i], staged.Content[i], StringComparison.Ordinal))
            {
                throw new RestoreRefusedException(
                    RestoreRefusal.RestoredRegisterFailedFinalCheck,
                    StatusCatalog.RestoreDoesNotMatchVerified,
                    "The restored register does not match what was verified, so the previous one was " +
                    "put back.");
            }
        }
    }

    // ⛔ The operator must never be left without the register they started with. This undoes exactly the
    //   two file operations above, and deletes only the file this method itself put there.
    private static void RollBack(string registerPath, string? preserved)
    {
        try
        {
            if (File.Exists(registerPath))
            {
                File.Delete(registerPath);
            }

            if (preserved is not null && File.Exists(preserved))
            {
                File.Move(preserved, registerPath);
            }
        }
        catch (IOException)
        {
            // ⚠ The original failure is the one worth reporting, and the preserved copy is still on disk
            //   under a name the report names — so the operator can put it back by hand.
        }
    }

    // ⭐ Every refusal happens here, before a single byte is decrypted — so a mistyped target costs the
    //   operator nothing and tells them why.
    private string ResolveTarget(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new RestoreRefusedException(
                RestoreRefusal.TargetIsNotUsable,
                StatusCatalog.RestoreChooseFolder,
                "Choose a folder to restore into.");
        }

        string target;
        try
        {
            target = Path.GetFullPath(targetDirectory);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RestoreRefusedException(
                RestoreRefusal.TargetIsNotUsable,
                StatusCatalog.RestoreNotAUsableFolderPath,
                "That is not a usable folder path.",
                inner: e);
        }

        if (File.Exists(target))
        {
            throw new RestoreRefusedException(
                RestoreRefusal.TargetIsNotUsable,
                StatusCatalog.RestorePathIsAFile,
                "That path is a file. A restore needs a folder of its own.");
        }

        // ⛔⛔ THE RULE. Compared as resolved full paths so that a relative path, a trailing separator or
        //     a different casing cannot walk around it.
        if (IsSameFolder(target, _activeRegisterFolder))
        {
            throw new RestoreRefusedException(
                RestoreRefusal.TargetIsTheActiveRegister,
                StatusCatalog.RestoreNeverIntoActiveFolder,
                "A restore never writes into the active register's folder. Choose a new, empty folder; " +
                "you can swap the restored register in yourself once you have looked at it.");
        }

        if (Directory.Exists(target) &&
            (Directory.EnumerateFileSystemEntries(target).GetEnumerator().MoveNext()))
        {
            throw new RestoreRefusedException(
                RestoreRefusal.TargetIsNotEmpty,
                StatusCatalog.RestoreFolderNotEmpty,
                "That folder is not empty. A restore always creates its register in a folder of its own, " +
                "so nothing that is already there can be written over.");
        }

        return target;
    }

    private static bool IsSameFolder(string left, string right)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));

        // ⚠ Windows paths are case-insensitive, and this comparison is a safety gate — an ordinal
        //   comparison here would let "c:\data" through while "C:\data" is refused.
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static StagedRegister OpenStaging(byte[] snapshot)
    {
        try
        {
            return new StagedRegister(snapshot);
        }
        catch (Exception e) when (e is not RestoreRefusedException)
        {
            // ⚠ The bytes decrypted — the passphrase was right — but they are not a register this build
            //   can open. Saying so is the difference between "you mistyped" and "this file is not what
            //   it claims", which are different problems with different next steps.
            throw new RestoreRefusedException(
                RestoreRefusal.BackupIsNotARegister,
                StatusCatalog.RestoreBackupIsNotARegister,
                "The backup decrypted, but what came out is not a register this build can open.",
                inner: e);
        }
    }

    // ⭐ The target folder is created HERE — after the integrity gate — so that a refused restore leaves
    //   no folder behind that looks like a completed one.
    private static string Materialise(string stagedFile, string target)
    {
        var created = !Directory.Exists(target);
        Directory.CreateDirectory(target);

        var destination = Path.Combine(target, ManagerPaths.RegisterFileName);

        try
        {
            File.Move(stagedFile, destination);
            return destination;
        }
        catch
        {
            // ⛔ A half-materialised folder must never be handed back as a restore. Undo exactly what
            //   this method did, and no more: a folder the operator had already created stays.
            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                if (created && Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
            }
            catch (IOException)
            {
                // The original failure is the one worth reporting.
            }

            throw;
        }
    }
}
