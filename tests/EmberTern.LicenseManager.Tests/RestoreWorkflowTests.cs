using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Restoring a register, and — far more importantly — every way in which it refuses to.
///
/// <para>⭐⭐ The governing rule of this stage is that a restore never writes over the active register.
/// These tests are written so that the rule fails LOUDLY: after every one of them, successful or refused,
/// the active register must be byte-for-row identical to what it was before.</para>
/// </summary>
public sealed class RestoreWorkflowTests : IDisposable
{
    private const string BackupPassphrase = "a different secret from the keystore";

    private readonly ManagerFixture _fixture = new();
    private readonly string _workspace;

    public RestoreWorkflowTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "etlm-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        RegisterSnapshotTests.Seed(_fixture);
    }

    private RestoreWorkflow Restorer => new(_fixture.Paths.Root);

    private string Fresh(string name) => Path.Combine(_workspace, name + Guid.NewGuid().ToString("N")[..6]);

    private byte[] GoodBackup()
    {
        var path = Path.Combine(_workspace, "source" + RegisterBackup.FileExtension);
        new BackupWorkflow(_fixture.Register, () => _fixture.Now).CreateBackup(path, BackupPassphrase);
        return File.ReadAllBytes(path);
    }

    // ── The happy path ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARestoreCreatesANewRegisterInANewFolder()
    {
        var backup = GoodBackup();
        var target = Fresh("restored-");

        var report = Restorer.Restore(backup, BackupPassphrase, target);

        Assert.True(Directory.Exists(target));
        Assert.Equal(Path.Combine(target, ManagerPaths.RegisterFileName), report.RegisterPath);
        Assert.True(File.Exists(report.RegisterPath));
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, report.SchemaVersion);
        Assert.Equal(_fixture.Now, report.BackupCreatedAt);
    }

    /// <summary>⭐⭐ The whole register comes back: every row, every column, every table.</summary>
    [Fact]
    public void TheRestoredRegisterIsTheOneThatWasBackedUp()
    {
        var expected = _fixture.Register.DumpContent();
        var backup = GoodBackup();
        var target = Fresh("same-");

        var report = Restorer.Restore(backup, BackupPassphrase, target);

        using var restored = LicenseRegister.Open(report.RegisterPath);
        Assert.Equal(expected, restored.DumpContent());
    }

    [Fact]
    public void TheHistoryTheCurrentPointerAndTheAuditAllComeBack()
    {
        var license = _fixture.Register.GetAllLicenses().Single();
        var artifacts = _fixture.Register.GetArtifacts(license.LicenseId);
        var current = _fixture.Register.GetCurrentArtifact(license.LicenseId)!;
        var audit = _fixture.Register.GetAudit(new AuditQuery { Limit = int.MaxValue });

        var report = Restorer.Restore(GoodBackup(), BackupPassphrase, Fresh("history-"));

        using var restored = LicenseRegister.Open(report.RegisterPath);

        // ⭐ Not flattened to the current artifact: the superseded one is still there, still marked.
        Assert.Equal(
            artifacts.Select(a => (a.ArtifactId, a.Token, a.Reason, a.Status)),
            restored.GetArtifacts(license.LicenseId).Select(a => (a.ArtifactId, a.Token, a.Reason, a.Status)));
        Assert.Contains(
            restored.GetArtifacts(license.LicenseId), a => a.Status == ArtifactStatuses.Superseded);

        Assert.Equal(current.ArtifactId, restored.GetCurrentArtifact(license.LicenseId)!.ArtifactId);

        Assert.Equal(
            audit.Select(e => (e.AuditId, e.Action, e.TargetId, e.Note)),
            restored.GetAudit(new AuditQuery { Limit = int.MaxValue })
                .Select(e => (e.AuditId, e.Action, e.TargetId, e.Note)));
    }

    [Fact]
    public void TheRestoredRegisterPassesTheIntegrityCheck()
    {
        var report = Restorer.Restore(GoodBackup(), BackupPassphrase, Fresh("sound-"));

        using var restored = LicenseRegister.Open(report.RegisterPath);
        Assert.Empty(restored.CheckIntegrity());
    }

    [Fact]
    public void TheRestoredRegisterCanStillIssueBecauseItsIdentityCounterCameBack()
    {
        // ⭐ The AUTOINCREMENT high-water mark is data. Without it a restored register would re-spend an
        //    artifact_id the history already used, and every pointer into that history would become
        //    ambiguous. Proved by APPENDING, not by reading a table.
        var report = Restorer.Restore(GoodBackup(), BackupPassphrase, Fresh("counter-"));
        var highest = _fixture.Register.GetAllArtifacts().Max(a => a.ArtifactId);

        using var restored = LicenseRegister.Open(report.RegisterPath, () => _fixture.Now.AddDays(30));
        var license = restored.GetAllLicenses().Single();
        var source = _fixture.Register.GetAllArtifacts().Last();

        var appended = restored.AppendArtifact(new IssuedArtifactRecord
        {
            LicenseId = license.LicenseId,
            KeyId = source.KeyId,
            IssuedAt = _fixture.Now.AddDays(30),
            PayloadJson = source.PayloadJson,
            Token = source.Token,
            Reason = IssueReasons.ReissueLost,
        });

        Assert.True(
            appended.ArtifactId > highest,
            $"a restored register re-used artifact_id {appended.ArtifactId}; history already reached {highest}");
    }

    // ── The rule ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>THE RULE.</b> The active register is untouched by a successful restore — not even a history
    /// line (D‑5). Compared through the schema-driven dump, so an audit row would show up as a difference.
    /// </summary>
    [Fact]
    public void ASUCCESSFULRestoreLeavesTheActiveRegisterCompletelyUntouched()
    {
        var backup = GoodBackup();
        var before = _fixture.Register.DumpContent();

        Restorer.Restore(backup, BackupPassphrase, Fresh("elsewhere-"));

        Assert.Equal(before, _fixture.Register.DumpContent());
    }

    [Fact]
    public void ARestoreWritesNothingToTheActiveAuditLog()
    {
        var backup = GoodBackup();
        var before = _fixture.Register.GetAudit(new AuditQuery { Limit = int.MaxValue }).Count;

        Restorer.Restore(backup, BackupPassphrase, Fresh("silent-"));

        Assert.Equal(
            before, _fixture.Register.GetAudit(new AuditQuery { Limit = int.MaxValue }).Count);
    }

    /// <summary>
    /// ⛔⛔ <b>The rule made structural.</b> A restore cannot write to the active register because it holds
    /// no register at all — only a folder path, and only so it can refuse it. ⚠ This is the guard against
    /// a future convenience constructor quietly reintroducing the capability; a comment would not survive
    /// that, and neither would an intention.
    /// </summary>
    [Fact]
    public void TheRestoreWorkflowHoldsNoRegisterAtAll()
    {
        // ⚠⚠ A test that asserts an ABSENCE passes just as happily when the query is wrong as when the
        //    code is right — gotcha #378's shape. So the same query is first pointed at BackupWorkflow,
        //    which certainly does hold a register: if THAT comes back empty, the query is broken and this
        //    guard would have been decorative. ⭐ The positive control is what makes the negative one
        //    mean something, and it costs nothing.
        Assert.Contains(typeof(LicenseRegister), TypesReachableFrom(typeof(BackupWorkflow)));

        var reachable = TypesReachableFrom(typeof(RestoreWorkflow));

        Assert.DoesNotContain(typeof(LicenseRegister), reachable);
        Assert.DoesNotContain(typeof(BackupWorkflow), reachable);
    }

    private static System.Collections.Generic.List<Type> TypesReachableFrom(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(member => member switch
            {
                FieldInfo f => new[] { f.FieldType },
                PropertyInfo p => [p.PropertyType],
                ConstructorInfo c => c.GetParameters().Select(x => x.ParameterType).ToArray(),
                MethodInfo m => m.GetParameters().Select(x => x.ParameterType)
                    .Append(m.ReturnType).ToArray(),
                _ => [],
            })
            .ToList();

    [Fact]
    public void RestoringIntoTheActiveRegistersOwnFolderIsRefused()
    {
        var backup = GoodBackup();
        var before = _fixture.Register.DumpContent();

        var error = Assert.Throws<RestoreRefusedException>(
            () => Restorer.Restore(backup, BackupPassphrase, _fixture.Paths.Root));

        Assert.Equal(RestoreRefusal.TargetIsTheActiveRegister, error.Refusal);
        Assert.Equal(before, _fixture.Register.DumpContent());
        Assert.True(File.Exists(_fixture.Paths.Register));
    }

    /// <summary>⚠ A trailing separator, a different casing and a relative walk are the same folder.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TheActiveFolderCannotBeReachedBySpellingItDifferently(bool trailingSeparator, bool upperCase)
    {
        var spelling = _fixture.Paths.Root;
        if (upperCase)
        {
            spelling = spelling.ToUpperInvariant();
        }

        if (trailingSeparator)
        {
            spelling += Path.DirectorySeparatorChar;
        }

        Assert.Equal(
            RestoreRefusal.TargetIsTheActiveRegister,
            Assert.Throws<RestoreRefusedException>(
                () => Restorer.Restore(GoodBackup(), BackupPassphrase, spelling)).Refusal);
    }

    [Fact]
    public void ARelativePathThatResolvesToTheActiveFolderIsAlsoRefused()
    {
        var sneaky = Path.Combine(_fixture.Paths.Root, "sub", "..");

        Assert.Equal(
            RestoreRefusal.TargetIsTheActiveRegister,
            Assert.Throws<RestoreRefusedException>(
                () => Restorer.Restore(GoodBackup(), BackupPassphrase, sneaky)).Refusal);
    }

    [Fact]
    public void ANonEmptyFolderIsRefusedAndNothingInItIsTouched()
    {
        var target = Fresh("occupied-");
        Directory.CreateDirectory(target);
        var bystander = Path.Combine(target, "important.txt");
        File.WriteAllText(bystander, "do not lose me");

        var error = Assert.Throws<RestoreRefusedException>(
            () => Restorer.Restore(GoodBackup(), BackupPassphrase, target));

        Assert.Equal(RestoreRefusal.TargetIsNotEmpty, error.Refusal);
        Assert.Equal("do not lose me", File.ReadAllText(bystander));
        Assert.False(File.Exists(Path.Combine(target, ManagerPaths.RegisterFileName)));
    }

    [Fact]
    public void AnExistingEmptyFolderIsAcceptable()
    {
        var target = Fresh("empty-");
        Directory.CreateDirectory(target);

        var report = Restorer.Restore(GoodBackup(), BackupPassphrase, target);

        Assert.True(File.Exists(report.RegisterPath));
    }

    [Fact]
    public void AFilePathIsNotAFolderAndIsRefused()
    {
        var target = Fresh("afile-") + ".db";
        File.WriteAllText(target, "not a folder");

        Assert.Equal(
            RestoreRefusal.TargetIsNotUsable,
            Assert.Throws<RestoreRefusedException>(
                () => Restorer.Restore(GoodBackup(), BackupPassphrase, target)).Refusal);
        Assert.Equal("not a folder", File.ReadAllText(target));
    }

    // ── Bad backups ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>⛔ A wrong passphrase is never "close enough". Nothing is created.</summary>
    [Fact]
    public void AWrongPassphraseRestoresNothingAndCreatesNoFolder()
    {
        var backup = GoodBackup();
        var target = Fresh("wrongpass-");
        var before = _fixture.Register.DumpContent();

        Assert.Equal(
            BackupFailure.WrongPassphrase,
            Assert.Throws<BackupException>(
                () => Restorer.Restore(backup, "not the passphrase", target)).Failure);

        Assert.False(Directory.Exists(target));
        Assert.Equal(before, _fixture.Register.DumpContent());
    }

    [Fact]
    public void AModifiedBackupIsRefusedAndCreatesNoFolder()
    {
        var backup = GoodBackup();
        backup[^8] = backup[^8] == (byte)'A' ? (byte)'B' : (byte)'A';
        var target = Fresh("tampered-");

        Assert.Throws<BackupException>(() => Restorer.Restore(backup, BackupPassphrase, target));

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void AFileThatIsNotABackupIsRefusedAndCreatesNoFolder()
    {
        var target = Fresh("notabackup-");

        Assert.Equal(
            BackupFailure.NotABackup,
            Assert.Throws<BackupException>(() => Restorer.Restore(
                Encoding.UTF8.GetBytes("hello, I am a text file"), BackupPassphrase, target)).Failure);

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void AnEmptyFileIsRefusedAndCreatesNoFolder()
    {
        var target = Fresh("emptyfile-");

        Assert.Throws<BackupException>(() => Restorer.Restore([], BackupPassphrase, target));

        Assert.False(Directory.Exists(target));
    }

    /// <summary>
    /// ⛔⛔ <b>A backup whose contents are inconsistent is refused, and the refusal leaves NO folder.</b>
    /// This is the case a warning dialog could not have covered: the file decrypts, the passphrase was
    /// right, and everything looks like success right up to the point where the register contradicts
    /// itself. ⭐ The gate is <c>CheckIntegrity</c> — the existing checker, reporting, never repairing.
    /// </summary>
    [Fact]
    public void ABackupOfAnInconsistentRegisterIsRefusedAfterDecryptingAndLeavesNothingBehind()
    {
        var corrupted = BackupOfADamagedRegister();
        var target = Fresh("inconsistent-");

        var error = Assert.Throws<RestoreRefusedException>(
            () => Restorer.Restore(corrupted, BackupPassphrase, target));

        Assert.Equal(RestoreRefusal.RestoredRegisterIsInconsistent, error.Refusal);
        Assert.NotEmpty(error.Problems);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void ARefusedRestoreLeavesNoStagedRegisterUnderTheTempFolder()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "etlm-staging-*").Length;

        Assert.Throws<RestoreRefusedException>(
            () => Restorer.Restore(BackupOfADamagedRegister(), BackupPassphrase, Fresh("leak-")));

        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "etlm-staging-*").Length);
    }

    /// <summary>
    /// ⭐ The header is readable before a passphrase is typed — which is what lets the restore surface say
    /// what it is about to restore rather than asking the operator to trust a file name.
    /// </summary>
    [Fact]
    public void ABackupCanBeInspectedBeforeItIsOpened()
    {
        var header = RestoreWorkflow.Inspect(GoodBackup());

        Assert.Equal(_fixture.Now, header.CreatedAt);
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, header.SchemaVersion);
    }

    // ── Replacing the active register (D‑6 / D‑7) ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The mode's whole promise in one test: the restored register is in place, AND the one that used
    /// to be there is still on disk, openable, with its own content.
    /// </summary>
    [Fact]
    public void ReplacingTheActiveRegisterKeepsThePreviousOneAndPutsTheRestoredOneInPlace()
    {
        var expected = _fixture.Register.DumpContent();
        var backup = GoodBackup();
        var active = AnActiveRegisterHolding("THE OLD REGISTER");

        var report = new RestoreWorkflow(active, () => _fixture.Now)
            .RestoreOverActiveRegister(backup, BackupPassphrase);

        // The restored register is where the application will look for it.
        Assert.Equal(Path.Combine(active, ManagerPaths.RegisterFileName), report.RegisterPath);
        using (var restored = LicenseRegister.Open(report.RegisterPath))
        {
            Assert.Equal(expected, restored.DumpContent());
            Assert.Empty(restored.CheckIntegrity());
        }

        // ⛔ …and the previous one was kept, not deleted. It still opens, and it is still ITS OWN
        //    register — this is what stops a restore from being a one-way door.
        Assert.NotNull(report.PreservedRegisterPath);
        Assert.True(File.Exists(report.PreservedRegisterPath));
        Assert.StartsWith(
            Path.Combine(active, ManagerPaths.RegisterFileName) + ".replaced-",
            report.PreservedRegisterPath,
            StringComparison.Ordinal);

        using var preserved = LicenseRegister.Open(report.PreservedRegisterPath!);
        Assert.Contains(preserved.GetCustomers(), c => c.Name == "THE OLD REGISTER");
    }

    /// <summary>
    /// ⛔⛔ The refusal that protects the whole mode. While the register is open the file cannot be moved,
    /// and a workflow that believed its caller had closed it would find out half way through — with the
    /// previous register already moved aside.
    /// </summary>
    [Fact]
    public void ReplacingIsRefusedWhileTheRegisterIsStillOpenAndNothingIsMoved()
    {
        var backup = GoodBackup();
        var active = AnActiveRegisterHolding("STILL IN USE");

        // Re-open it, and leave it open — the state a caller that forgot to close would be in.
        using (var held = LicenseRegister.Open(Path.Combine(active, ManagerPaths.RegisterFileName)))
        {
            var error = Assert.Throws<RestoreRefusedException>(
                () => new RestoreWorkflow(active, () => _fixture.Now)
                    .RestoreOverActiveRegister(backup, BackupPassphrase));

            Assert.Equal(RestoreRefusal.ActiveRegisterIsStillOpen, error.Refusal);
            Assert.Contains("STILL IN USE", held.GetCustomers().Single().Name, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(active, "*.replaced-*"));
        Assert.Single(Directory.GetFiles(active));
    }

    /// <summary>⛔ An inconsistent backup never reaches the point of moving anything.</summary>
    [Fact]
    public void ReplacingWithAnInconsistentBackupLeavesTheActiveRegisterExactlyWhereItWas()
    {
        var corrupted = BackupOfADamagedRegister();
        var active = AnActiveRegisterHolding("UNTOUCHED");
        var registerPath = Path.Combine(active, ManagerPaths.RegisterFileName);
        var before = File.ReadAllBytes(registerPath);

        var error = Assert.Throws<RestoreRefusedException>(
            () => new RestoreWorkflow(active, () => _fixture.Now)
                .RestoreOverActiveRegister(corrupted, BackupPassphrase));

        Assert.Equal(RestoreRefusal.RestoredRegisterIsInconsistent, error.Refusal);
        Assert.NotEmpty(error.Problems);
        Assert.Equal(before, File.ReadAllBytes(registerPath));
        Assert.Empty(Directory.GetFiles(active, "*.replaced-*"));
    }

    [Fact]
    public void ReplacingWithAWrongPassphraseLeavesTheActiveRegisterExactlyWhereItWas()
    {
        var backup = GoodBackup();
        var active = AnActiveRegisterHolding("UNTOUCHED");
        var registerPath = Path.Combine(active, ManagerPaths.RegisterFileName);
        var before = File.ReadAllBytes(registerPath);

        Assert.Throws<BackupException>(
            () => new RestoreWorkflow(active, () => _fixture.Now)
                .RestoreOverActiveRegister(backup, "not the passphrase"));

        Assert.Equal(before, File.ReadAllBytes(registerPath));
        Assert.Empty(Directory.GetFiles(active, "*.replaced-*"));
    }

    /// <summary>
    /// ⛔⛔ <b>D‑7: no preserved copy is ever overwritten.</b> Two replaces stamped at the same instant
    /// must produce TWO files — otherwise the second restore silently destroys the register the first one
    /// saved, which is the exact failure the preservation exists to prevent.
    /// </summary>
    [Fact]
    public void TwoReplacesInTheSameSecondKeepBOTHPreviousRegisters()
    {
        var backup = GoodBackup();
        var active = AnActiveRegisterHolding("FIRST");

        // ⚠ A frozen clock, deliberately: this is the collision the naming scheme has to survive.
        var restorer = new RestoreWorkflow(active, () => _fixture.Now);

        var first = restorer.RestoreOverActiveRegister(backup, BackupPassphrase);
        var second = restorer.RestoreOverActiveRegister(backup, BackupPassphrase);

        Assert.NotEqual(first.PreservedRegisterPath, second.PreservedRegisterPath);
        Assert.True(File.Exists(first.PreservedRegisterPath));
        Assert.True(File.Exists(second.PreservedRegisterPath));
        Assert.Equal(2, Directory.GetFiles(active, "*.replaced-*").Length);
    }

    /// <summary>⭐ A folder with no register yet is simply populated; there is nothing to preserve.</summary>
    [Fact]
    public void ReplacingWhereThereIsNoRegisterYetPreservesNothingAndStillRestores()
    {
        var backup = GoodBackup();
        var empty = Fresh("no-register-");
        Directory.CreateDirectory(empty);

        var report = new RestoreWorkflow(empty, () => _fixture.Now)
            .RestoreOverActiveRegister(backup, BackupPassphrase);

        Assert.Null(report.PreservedRegisterPath);
        Assert.True(File.Exists(report.RegisterPath));
        Assert.Empty(Directory.GetFiles(empty, "*.replaced-*"));
    }

    /// <summary>
    /// ⭐ The two modes stay apart: replacing writes into the active folder, and restoring elsewhere still
    /// refuses it. ⚠ Worth pinning together — the risk in adding the first mode was that it would soften
    /// the guard on the second.
    /// </summary>
    [Fact]
    public void AddingTheReplaceModeDidNotSoftenTheOtherModesRefusal()
    {
        var backup = GoodBackup();
        var active = AnActiveRegisterHolding("UNTOUCHED");
        var restorer = new RestoreWorkflow(active, () => _fixture.Now);

        Assert.Equal(
            RestoreRefusal.TargetIsTheActiveRegister,
            Assert.Throws<RestoreRefusedException>(
                () => restorer.Restore(backup, BackupPassphrase, active)).Refusal);

        Assert.Empty(Directory.GetFiles(active, "*.replaced-*"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A folder holding a CLOSED register with one recognisable customer — the state a License Manager
    /// leaves behind once it has released its register.
    /// </summary>
    private string AnActiveRegisterHolding(string customerName)
    {
        var folder = Fresh("active-");
        Directory.CreateDirectory(folder);

        using (var register = LicenseRegister.Open(
                   Path.Combine(folder, ManagerPaths.RegisterFileName), () => _fixture.Now))
        {
            register.SaveCustomer(new CustomerRecord { CustomerId = "c-0001", Name = customerName });
        }

        return folder;
    }

    /// <summary>
    /// A structurally valid, correctly encrypted backup whose CONTENTS are inconsistent.
    ///
    /// <para>⚠ It cannot be produced through <see cref="BackupWorkflow"/> — that refuses an inconsistent
    /// register, which is the point. So the damage is injected past the API into a snapshot, and the
    /// envelope is built directly. ⭐ Without this, "a restore refuses a broken register" would be a claim
    /// no test could reach.</para>
    /// </summary>
    private byte[] BackupOfADamagedRegister()
    {
        var snapshot = _fixture.Register.CreateSnapshot();

        var folder = Path.Combine(Path.GetTempPath(), "etlm-damage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ManagerPaths.RegisterFileName);

        try
        {
            File.WriteAllBytes(path, snapshot);

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE license_current_artifact SET artifact_id = " +
                    "(SELECT MIN(artifact_id) FROM issued_artifacts WHERE lid = license_current_artifact.lid);";
                command.ExecuteNonQuery();
                connection.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            }

            return RegisterBackup.Create(
                File.ReadAllBytes(path), BackupPassphrase, _fixture.Now, LicenseRegister.CurrentSchemaVersion);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public void Dispose()
    {
        _fixture.Dispose();

        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
