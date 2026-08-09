using System;
using System.IO;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Settings load health (audit A-03): a <c>settings.dat</c> this build cannot read must never be overwritten.
/// <para>
/// The bug these pin was a CONFLATION, not a missing check. <c>Load()</c> returned null for "fresh install"
/// and for "exists but undecryptable" alike; all eight section facades answer null with
/// <c>?? new ApplicationSettings()</c> and then save; and the save-side guard deliberately allowed replacing a
/// file it could not decrypt. Net effect on a machine where DPAPI fails — a copied Windows profile, a restored
/// account — the next grid-column resize replaced the user's connection profiles and passwords with defaults.
/// </para>
/// <para><b>The load-bearing test is <see cref="Save_RefusesToOverwrite_AnUndecryptableFile"/>.</b> It fails on
/// the pre-fix code. Everything else here describes the states around it.</para>
/// </summary>
public class SettingsLoadHealthTests
{
    // Reversible stand-in for DPAPI, matching ApplicationSettingsStoreTests: "x" -> "ENC:x".
    private static SecretProtector FakeProtector() =>
        new(s => "ENC:" + s, s => s.StartsWith("ENC:", StringComparison.Ordinal)
            ? s.Substring(4)
            : throw new FormatException("not an ENC: blob"));

    // A protector that encrypts fine but can never decrypt — precisely DPAPI on the wrong account.
    private static SecretProtector UndecryptableProtector() =>
        new(s => "ENC:" + s, _ => throw new InvalidOperationException("Key not valid for use in specified state."));

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static void InTempDir(Action<string> body)
    {
        var dir = NewTempDir();
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // ─── THE REGRESSION ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_RefusesToOverwrite_AnUndecryptableFile()
    {
        InTempDir(dir =>
        {
            // A real user's settings, written on "their" machine.
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
            });
            var original = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            // Now open it where decryption fails. This is the whole scenario: the file is INTACT and would
            // decrypt on the right machine — it is simply unreadable here.
            var store = new ApplicationSettingsStore(dir, UndecryptableProtector());
            var health = store.LoadWithStatus();
            Assert.Equal(SettingsLoadStatus.Unreadable, health.Status);
            Assert.False(health.CanSave);

            // …and the app does what it always does with a null load: defaults, then a trivial write.
            // Before the fix, THIS line destroyed the profile above.
            store.Save(store.Load() ?? new ApplicationSettings());

            Assert.Equal(original, File.ReadAllText(Path.Combine(dir, "settings.dat")));
            Assert.NotNull(store.LastSaveDiagnostic);
            Assert.Contains("Refusing to overwrite", store.LastSaveDiagnostic!, StringComparison.Ordinal);

            // And the data really is still there for whoever can decrypt it.
            var recovered = new ApplicationSettingsStore(dir, FakeProtector()).Load();
            Assert.NotNull(recovered);
            Assert.Equal("PROD", Assert.Single(recovered!.Connections).Name);
            Assert.Equal("secret", recovered.Connections[0].Password);
        });
    }

    [Fact]
    public void Save_RefusesToOverwrite_ACorruptFile()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, "{ this is not valid JSON ::: ");
            var original = File.ReadAllText(store.FilePath);

            Assert.Equal(SettingsLoadStatus.Corrupt, store.LoadWithStatus().Status);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "X" } } });

            Assert.Equal(original, File.ReadAllText(store.FilePath));
            Assert.NotNull(store.LastSaveDiagnostic);
        });
    }

    // ─── The states that DO permit a write ──────────────────────────────────────────────────

    [Fact]
    public void Missing_IsNotAnError_AndSavingIsSafe()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            var health = store.LoadWithStatus();

            Assert.Equal(SettingsLoadStatus.Missing, health.Status);
            Assert.True(health.CanSave);      // nothing on disk to destroy
            Assert.False(health.NeedsAttention);
            Assert.Null(health.Diagnostic);   // a fresh install needs no explanation

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "A" } } });
            Assert.Null(store.LastSaveDiagnostic);
            Assert.Single(store.Load()!.Connections);
        });
    }

    [Fact]
    public void EmptyFile_CountsAsMissing_BecauseItHoldsNoUserData()
    {
        // A zero-length settings.dat is what a disk-full or killed-mid-write leaves behind. Refusing to write
        // over it would strand the user for no benefit: there is nothing in it to lose.
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, "   ");

            Assert.Equal(SettingsLoadStatus.Missing, store.LoadWithStatus().Status);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "A" } } });
            Assert.Null(store.LastSaveDiagnostic);
            Assert.Single(store.Load()!.Connections);
        });
    }

    [Fact]
    public void Loaded_ReportsHealthy_AndKeepsSaving()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "A" } } });

            var health = store.LoadWithStatus();

            Assert.Equal(SettingsLoadStatus.Loaded, health.Status);
            Assert.True(health.CanSave);
            Assert.False(health.NeedsAttention);
            Assert.NotNull(health.Settings);
        });
    }

    // ─── Downgrade protection still behaves as it did ───────────────────────────────────────

    [Fact]
    public void FutureSchemaVersion_IsReportedAsFuture_AndStillRefused()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings());
            // Forge a newer data schema version.
            var raw = File.ReadAllText(store.FilePath);
            File.WriteAllText(store.FilePath, raw.Replace(
                "\"SchemaVersion\": 2", "\"SchemaVersion\": 999", StringComparison.Ordinal));
            var original = File.ReadAllText(store.FilePath);

            var health = store.LoadWithStatus();

            Assert.Equal(SettingsLoadStatus.FutureVersion, health.Status);
            Assert.True(health.NeedsAttention);
            Assert.False(health.CanSave);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "X" } } });
            Assert.Equal(original, File.ReadAllText(store.FilePath));
        });
    }

    // ─── The escape hatch ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveOverUnreadableFile_PreservesTheOldBytes_ThenWrites()
    {
        // Being stranded must be a stop, not a dead end — but recovery keeps the old file, because "cannot read
        // it" is not "it is worthless".
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
            });
            var original = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            var store = new ApplicationSettingsStore(dir, UndecryptableProtector());
            var preservedAt = store.SaveOverUnreadableFile(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "FRESH" } },
            });

            Assert.NotNull(preservedAt);
            Assert.True(File.Exists(preservedAt!));
            Assert.Equal(original, File.ReadAllText(preservedAt!));       // the old data survives, verbatim
            Assert.NotEqual(original, File.ReadAllText(store.FilePath));  // and the new settings were written
        });
    }

    [Fact]
    public void SaveOverUnreadableFile_OnAFreshInstall_PreservesNothing()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            Assert.Null(store.SaveOverUnreadableFile(new ApplicationSettings()));
            Assert.True(File.Exists(store.FilePath));
        });
    }

    // ─── The secondary net ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AtomicWrite_KeepsThePreviousGeneration_AsBak()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "FIRST" } } });
            var first = File.ReadAllText(store.FilePath);

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "SECOND" } } });

            var bak = store.FilePath + ".bak";
            Assert.True(File.Exists(bak));
            Assert.Equal(first, File.ReadAllText(bak));
            Assert.Contains("SECOND", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        });
    }

    // ─── Two instances of EmberTern, one settings.dat (2026-08-03) ──────────────────────────

    /// <summary>
    /// ⭐⭐ <b>THE LOAD-BEARING TEST OF THE SECOND SETTINGS DATA-LOSS BUG. It fails on the pre-fix code.</b>
    ///
    /// <para>The user ran a second EmberTern from the same exe (a smoke test beside a working instance) and their
    /// settings vanished. The mechanism was one shared object: <c>AtomicWrite</c> used the FIXED temp path
    /// <c>settings.dat.tmp</c>, identical in every process. <c>File.WriteAllText</c> truncates before it writes, so
    /// the shared temp is momentarily zero-length — and a second instance reaching its <c>File.Replace</c> inside
    /// that window publishes an EMPTY settings.dat. From there the loss is automatic and silent: an empty file
    /// loads as <c>Missing</c>, every facade answers that with <c>?? new()</c>, the save guard lets a blank file
    /// through, and the next write makes defaults permanent.</para>
    ///
    /// <para>⚠ This test does not race anything — a race is the wrong instrument for pinning a race. It plants a
    /// file at the OLD shared temp path and requires the save to leave it completely alone, which is the property
    /// "the temp path is not shared" stated directly. Pre-fix, the save overwrites that file and
    /// <c>File.Replace</c> then consumes it, so both assertions fail.</para>
    /// </summary>
    [Fact]
    public void Save_DoesNotWriteThroughAShared_TempFileName()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "FIRST" } } });

            // Stand-in for the other instance's in-flight temp file, at the path both processes used to share.
            var sharedTemp = store.FilePath + ".tmp";
            File.WriteAllText(sharedTemp, "THE OTHER INSTANCE IS MID-WRITE");

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "SECOND" } } });

            Assert.True(File.Exists(sharedTemp), "the other instance's temp file was consumed by this save");
            Assert.Equal("THE OTHER INSTANCE IS MID-WRITE", File.ReadAllText(sharedTemp));
            Assert.Contains("SECOND", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The invariant the fix exists to protect, exercised the way it actually breaks: many writers, one file.
    /// Two <see cref="ApplicationSettingsStore"/> instances stand in for two processes — the cross-process lock is
    /// an OS mutex, which contends between threads of one process exactly as it does between processes, so this
    /// genuinely exercises it.
    /// <para>⚠ Asserted after every write, not once at the end: the failure being guarded is a file that is briefly
    /// wrong, and a single check at the end would pass straight through it.</para>
    /// <para>⚠⚠ <b>And this test does NOT catch the bug it describes — measured, not assumed.</b> Re-run against the
    /// pre-fix shared temp filename it still passes, while
    /// <see cref="Save_DoesNotWriteThroughAShared_TempFileName"/> fails on the exact assertion. Keep it anyway (it
    /// is the invariant, and it would catch a future lock defect that the deterministic test cannot), but do not
    /// mistake it for the guard: <b>a race is the wrong instrument for pinning a race</b> — the window is
    /// microseconds wide and no amount of iterations makes hitting it a fact rather than a hope.</para>
    /// </summary>
    [Fact]
    public void ConcurrentSaves_NeverLeaveSettingsUnreadable()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "SEED" } },
            });

            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            System.Threading.Tasks.Parallel.For(0, 4, worker =>
            {
                var store = new ApplicationSettingsStore(dir);
                var reader = new ApplicationSettingsStore(dir);
                for (int i = 0; i < 15; i++)
                {
                    store.Save(new ApplicationSettings
                    {
                        Connections = { new ConnectionProfile { Name = $"W{worker}-{i}" } },
                    });

                    var result = reader.LoadWithStatus();
                    if (result.Status != SettingsLoadStatus.Loaded)
                    {
                        failures.Add($"worker {worker}, write {i}: status was {result.Status}");
                    }
                    else if (result.Settings!.Connections.Count == 0)
                    {
                        failures.Add($"worker {worker}, write {i}: loaded but held no connections");
                    }
                }
            });

            Assert.Empty(failures);

            // No orphaned temp files: a per-write name is only an improvement if the writes clean up after
            // themselves, otherwise the settings folder fills with files nobody can identify.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        });
    }

    /// <summary>
    /// A write that cannot complete must REPORT, not throw. The realistic callers are ones nobody thinks of as
    /// writes — a grid column resized, the window closing — and an escaping <c>IOException</c> from
    /// <c>MainWindow</c>'s Closing handler would abandon the rest of the shutdown sequence.
    /// <para>The swap is blocked here by occupying the backup filename with a DIRECTORY, which
    /// <c>File.Replace</c> cannot overwrite. Any I/O failure would do; this one is deterministic.</para>
    /// </summary>
    [Fact]
    public void Save_WhenTheSwapFails_ReportsAndLeavesThePreviousFileIntact()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "KEEP" } } });
            var before = File.ReadAllText(store.FilePath);

            Directory.CreateDirectory(store.FilePath + ".bak");

            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "LOST" } } });

            Assert.NotNull(store.LastSaveDiagnostic);
            Assert.Equal(before, File.ReadAllText(store.FilePath));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        });
    }

    // ─── Every facade inherits the protection ───────────────────────────────────────────────

    [Fact]
    public void TheFrequent_SilentWriters_CannotDestroyTheFileEither()
    {
        // The realistic trigger is never "the user saved their settings" — it is a grid column resized, a
        // procedure run recording its parameters, the app closing. Each of those reaches the same Save, so
        // asserting one facade end-to-end is asserting the class of bug.
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
            });
            var original = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            var grids = new GridProfileStore(dir, UndecryptableProtector());

            grids.Save(new GridProfile { GridId = "sql-results" });

            Assert.Equal(original, File.ReadAllText(Path.Combine(dir, "settings.dat")));
        });
    }

    // ─── The user is TOLD ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAppWarns_WhenSettingsCannotBeRead()
    {
        // Refusing to write loses nothing, but it is invisible — so the one obligation the App layer carries is
        // to say so. Without this the user goes on arranging grids and adding connections, and none of it
        // persists. The message must carry the PATH (so they can back the file up) and the REASON (so a
        // wrong-machine DPAPI file, which is intact, is distinguishable from a damaged one).
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD" } },
            });

            var store = new ConnectionProfileStore(dir, UndecryptableProtector());
            using var service = new EmberTern.Firebird.FirebirdConnectionService();
            var vm = new EmberTern.App.ViewModels.MainWindowViewModel(store, service);

            Assert.True(vm.ShowSettingsHealthWarning);
            Assert.Contains(store.FilePath, vm.SettingsHealthMessage, StringComparison.Ordinal);
            Assert.Contains("will not save settings", vm.SettingsHealthMessage, StringComparison.Ordinal);

            // Dismiss silences the notice without pretending anything was resolved.
            vm.DismissSettingsHealthWarningCommand.Execute(null);
            Assert.False(vm.ShowSettingsHealthWarning);
        });
    }

    [Fact]
    public void TheAppStaysQuiet_OnAHealthyOrFreshInstall()
    {
        InTempDir(dir =>
        {
            var store = new ConnectionProfileStore(dir, FakeProtector());
            using var service = new EmberTern.Firebird.FirebirdConnectionService();
            var vm = new EmberTern.App.ViewModels.MainWindowViewModel(store, service);

            Assert.False(vm.ShowSettingsHealthWarning);
        });
    }

    // ─── A-09: the transaction-profile chips report what is ENFORCED ────────────────────────

    [Fact]
    public void TransactionProfileChips_CannotClaimAProfileTheEngineDoesNotUse()
    {
        // A legacy v1 settings file carries its single TransactionProfile forward into DataTransactionProfile
        // (Migrate_1_2), while TransactionService hard-enforces ReadCommitted. Reading the persisted value made
        // the chip claim "Table Stability" for transactions that were plainly Read Committed. A status chip whose
        // entire job is to say how the user's transactions behave must not be able to be wrong about it.
        InTempDir(dir =>
        {
            var store = new ConnectionProfileStore(dir, FakeProtector());
            store.Upsert(new ConnectionProfile
            {
                Name = "legacy",
                DataTransactionProfile = TransactionProfile.ReadWriteTableStability,
                MetadataTransactionProfile = TransactionProfile.Snapshot,
            });

            using var service = new EmberTern.Firebird.FirebirdConnectionService();
            var vm = new EmberTern.App.ViewModels.MainWindowViewModel(store, service);

            var enforced = EmberTern.App.ViewModels.TransactionProfileCatalog.LabelFor(
                EmberTern.Firebird.TransactionService.EnforcedProfile);
            Assert.Equal(enforced, vm.DataProfileName);
            Assert.Equal(enforced, vm.MetadataProfileName);
        });
    }
}
