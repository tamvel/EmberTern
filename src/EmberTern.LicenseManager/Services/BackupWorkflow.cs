using System;
using System.IO;
using System.Text;
using EmberTern.LicenseManager.Data;

namespace EmberTern.LicenseManager.Services;

/// <summary>What a backup turned out to contain, once it is written and therefore true.</summary>
/// <param name="Path">Where it was written.</param>
/// <param name="CreatedAt">The instant stamped into the file.</param>
/// <param name="SchemaVersion">The register schema inside it.</param>
/// <param name="Customers">How many customers it carries.</param>
/// <param name="Licenses">How many licences.</param>
/// <param name="Artifacts">How many issued artifacts — ⭐ the whole history, not the current ones.</param>
/// <param name="CurrentPointers">How many current-artifact pointers.</param>
/// <param name="AuditEntries">How many history lines.</param>
/// <param name="Bytes">The size of the encrypted file.</param>
public sealed record BackupReport(
    string Path,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    int Customers,
    int Licenses,
    int Artifacts,
    int CurrentPointers,
    int AuditEntries,
    long Bytes);

/// <summary>What a JSONL export turned out to contain.</summary>
/// <param name="Path">Where it was written.</param>
/// <param name="Lines">How many lines — one per row across all five record types.</param>
/// <param name="Bytes">The size of the file.</param>
public sealed record JsonlExportReport(string Path, int Lines, long Bytes);

/// <summary>
/// Taking a backup of the register, and exporting it as JSONL.
///
/// <para>⭐⭐ <b>A backup is VERIFIED before it is written, not after</b> (D‑3). The snapshot is opened as
/// a register and compared with the live one row by row — every table, every column, the
/// current-artifact pointers and the whole append-only history included. Only then is it encrypted. §24.1
/// already says it of the keystore: <i>a backup that has never been restored is a hypothesis</i>; this is
/// the same rule applied to the register, at the only moment it costs nothing.</para>
///
/// <para>⚠ <b>The verification compares CONTENT, never bytes.</b> <c>VACUUM INTO</c> defragments, so the
/// snapshot is a different file from <c>licenses.db</c> carrying the same register — see
/// <see cref="LicenseRegister.CreateSnapshot"/>. A guard written against a file hash would fail on every
/// correct backup.</para>
///
/// <para>⭐ Both operations write a line to the register's own history (D‑5): they are things that were
/// done TO the active register, and an administrator asking <i>"when was this last backed up?"</i> should
/// not have to look outside it. ⛔ Restore does not, and lives in <see cref="RestoreWorkflow"/> precisely
/// so that it has no way to.</para>
/// </summary>
public sealed class BackupWorkflow
{
    /// <summary>The audit action a backup writes.</summary>
    public const string BackupAction = "register.backed-up";

    /// <summary>The audit action a JSONL export writes.</summary>
    public const string ExportAction = "register.exported";

    /// <summary>The audit target type both actions use.</summary>
    public const string TargetType = "register";

    private readonly LicenseRegister _register;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the workflow over the active register.</summary>
    public BackupWorkflow(LicenseRegister register, Func<DateTimeOffset>? clock = null)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Takes a verified, encrypted backup of the whole register and writes it to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Where to write. ⚠ Chosen by the operator through a save dialog.</param>
    /// <param name="passphrase">⭐ The BACKUP's own passphrase (D‑1) — never the keystore's.</param>
    /// <exception cref="ArgumentException">The passphrase is empty.</exception>
    /// <exception cref="RegisterIntegrityException">
    /// The snapshot does not reproduce the register, or the register is inconsistent. ⛔ Nothing is
    /// written in either case: a backup of a register that disagrees with itself is a backup of a defect.
    /// </exception>
    public BackupReport CreateBackup(string path, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException(
                "A passphrase is required — a register backup is always encrypted.", nameof(passphrase));
        }

        // ⛔ A register that disagrees with itself is not backed up. Refusing here is what stops a
        //    corrupted state from being carried forward under a name that promises safety — and it is
        //    the same CheckIntegrity a restore refuses on, deliberately, rather than a second checker.
        var problems = _register.CheckIntegrity();
        if (problems.Count > 0)
        {
            throw new RegisterIntegrityException(
                "The register has integrity problems, so it was not backed up: " +
                string.Join(" ", problems));
        }

        var expected = _register.DumpContent();
        var snapshot = _register.CreateSnapshot();

        var counts = VerifySnapshot(snapshot, expected);

        var createdAt = _clock();
        var file = RegisterBackup.Create(
            snapshot, passphrase, createdAt, _register.SchemaVersion);

        WriteAtomically(path, file);

        _register.Record(
            BackupAction,
            TargetType,
            Path.GetFileName(path),
            $"Encrypted backup written to {path} — {counts.Customers} customer(s), " +
            $"{counts.Licenses} licence(s), {counts.Artifacts} artifact(s), " +
            $"{counts.AuditEntries} history line(s).");

        return new BackupReport(
            path, createdAt, _register.SchemaVersion,
            counts.Customers, counts.Licenses, counts.Artifacts,
            counts.CurrentPointers, counts.AuditEntries, file.LongLength);
    }

    /// <summary>
    /// Writes the whole register as JSON Lines — the escape hatch of §29.1.
    ///
    /// <para>⛔ Plain text and unencrypted, by design. It exists to be readable when nothing else is, so
    /// the surface offering it must say plainly that it is not a backup. ⚠ It carries every issued token
    /// verbatim, which is the point and also the reason it is not something to leave in a shared folder.
    /// </para>
    /// </summary>
    public JsonlExportReport ExportJsonl(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = RegisterJsonl.Export(_register);

        // ⚠ UTF-8 with NO BOM. Every other text artifact this repository writes for outside consumption
        //    is written the same way (gotcha #178), and a BOM on the first line of a JSONL file breaks
        //    the very `cat | jq` route the format exists for.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(string.Join("\n", lines) + "\n");

        WriteAtomically(path, bytes);

        _register.Record(
            ExportAction,
            TargetType,
            Path.GetFileName(path),
            $"Plain JSONL export written to {path} — {lines.Count} line(s). Not encrypted.");

        return new JsonlExportReport(path, lines.Count, bytes.LongLength);
    }

    /// <summary>
    /// ⭐ The verification that makes this a backup rather than a copy: it opens the snapshot as a real
    /// register and compares the schema-driven dump, so a column added tomorrow is covered without
    /// anybody remembering to extend this.
    ///
    /// <para>⚠⚠ <b><c>internal</c> so it can be tested, and that is not tidiness.</b> This guard only
    /// fires when a snapshot fails to reproduce the register — a state the public API cannot be talked
    /// into. Measured in the L5.5 injection campaign: deleting the comparison entirely left every test in
    /// the suite green. A safety mechanism with no reachable failing case is a comment.</para>
    /// </summary>
    internal static SnapshotCounts VerifySnapshot(byte[] snapshot, System.Collections.Generic.IReadOnlyList<string> expected)
    {
        var staging = new StagedRegister(snapshot);

        try
        {
            var actual = staging.Register.DumpContent();

            if (actual.Count != expected.Count)
            {
                throw new RegisterIntegrityException(
                    $"The snapshot holds {actual.Count} row(s) where the register holds {expected.Count}. " +
                    "Nothing was written.");
            }

            for (var i = 0; i < actual.Count; i++)
            {
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                {
                    throw new RegisterIntegrityException(
                        "The snapshot does not reproduce the register row for row. Nothing was written.");
                }
            }

            return SnapshotCounts.Read(staging.Register);
        }
        finally
        {
            staging.Dispose();
        }
    }

    // ⚠ Write-then-rename, so an interrupted write cannot leave a half-file wearing the name of a good
    //   backup. ⭐ Overwriting the DESTINATION is the operator's own decision — they picked it in a save
    //   dialog that already asked. That is a different question from a restore's target, which may never
    //   be overwritten at all; see RestoreWorkflow.
    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".partial";

        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // The original failure is the one worth reporting.
            }

            throw;
        }
    }
}

/// <summary>How much of each thing a register holds.</summary>
/// <param name="Customers">Customers.</param>
/// <param name="Licenses">Licences.</param>
/// <param name="Artifacts">Issued artifacts — the whole history.</param>
/// <param name="CurrentPointers">Current-artifact pointers.</param>
/// <param name="AuditEntries">History lines.</param>
public sealed record SnapshotCounts(
    int Customers, int Licenses, int Artifacts, int CurrentPointers, int AuditEntries)
{
    /// <summary>Counts what a register holds. ⚠ Every read is unlimited — a capped count is a wrong one.</summary>
    public static SnapshotCounts Read(LicenseRegister register)
    {
        ArgumentNullException.ThrowIfNull(register);
        return new SnapshotCounts(
            register.GetCustomers().Count,
            register.GetAllLicenses().Count,
            register.GetAllArtifacts().Count,
            register.GetCurrentArtifactPointers().Count,
            register.GetAudit(new AuditQuery { Limit = int.MaxValue }).Count);
    }
}

/// <summary>
/// A register snapshot materialised in a private temporary folder, and cleaned up afterwards.
///
/// <para>⭐ SQLite opens files, not byte arrays, so anything that wants to READ a snapshot has to put it
/// on a disk somewhere. Keeping that in one type means the temporary folder is removed on every path,
/// including the ones that throw.</para>
/// </summary>
internal sealed class StagedRegister : IDisposable
{
    private readonly string _folder;
    private bool _closed;
    private bool _disposed;

    internal StagedRegister(byte[] snapshot)
    {
        _folder = Path.Combine(
            Path.GetTempPath(), "etlm-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        FilePath = Path.Combine(_folder, ManagerPaths.RegisterFileName);
        File.WriteAllBytes(FilePath, snapshot);

        Register = LicenseRegister.Open(FilePath);
    }

    /// <summary>The staged register, open until <see cref="Close"/>.</summary>
    internal LicenseRegister Register { get; }

    /// <summary>Where the staged file sits.</summary>
    internal string FilePath { get; }

    /// <summary>
    /// Closes the staged register, releasing its file, and leaves the file in place.
    ///
    /// <para>⚠ For the restore path, which has to MOVE this file once it is satisfied with it — and which
    /// cannot move a file SQLite still holds. <see cref="Dispose"/> still removes whatever is left of the
    /// folder afterwards, so a failure between closing and moving cleans up like any other.</para>
    /// </summary>
    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Register.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover folder under %TEMP% must not turn a completed operation into a failed one.
        }
    }
}
