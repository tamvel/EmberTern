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
    /// Several <see cref="ApplicationSettingsStore"/> instances stand in for several processes — the cross-process
    /// lock is an OS mutex, which contends between threads of one process exactly as it does between processes, so
    /// this genuinely exercises it.
    ///
    /// <para>⚠⚠ <b>This test used to assert something the architecture never promised, and it was RIGHT to fail.</b>
    /// It read the file after every write and demanded <see cref="SettingsLoadStatus.Loaded"/> each time. But reads
    /// deliberately take no lock, and <c>File.Replace</c> makes the path briefly unopenable and even briefly absent
    /// — so a concurrent reader legitimately sees <c>Unreadable</c>. Measured directly: <b>844 transient
    /// <c>Unreadable</c> reads out of 2 270</b>, every one of them an <c>IOException</c> ("used by another
    /// process" / "could not find file"), and <b>not one</b> <c>Corrupt</c>. Demanding otherwise made the suite fail
    /// in roughly 1 run in 9 while the data was never in danger.</para>
    ///
    /// <para>⭐ <b>So it now asserts the guarantee that IS real and IS worth protecting:</b> when the concurrent
    /// writing stops, the file is complete and readable, the backup generation is a valid settings file, no temp
    /// file is orphaned, and — the part that matters most — <b>no write was lost</b>. The transient-read half of the
    /// story has its own, deterministic guard in
    /// <see cref="ATransientReadFailure_NeverWritesDefaults"/>, which pins the thing that genuinely could destroy
    /// data. ⛔ Do not restore the per-read <c>Loaded</c> assertion: it is a race used as an instrument for a race,
    /// and it measures the scheduler rather than the product.</para>
    /// </summary>
    [Fact]
    public void ConcurrentSaves_LeaveTheFileIntact()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "SEED" } },
            });

            System.Threading.Tasks.Parallel.For(0, 4, worker =>
            {
                var store = new ApplicationSettingsStore(dir);
                for (int i = 0; i < 15; i++)
                {
                    store.Update(s => s.Connections.Add(new ConnectionProfile { Name = $"W{worker}-{i}" }));
                }
            });

            // ⭐ THE GUARANTEE, asserted where it actually holds: once the writers are done, the file is a
            // complete, readable settings.dat — and every profile written through the locked read-modify-write
            // is still in it. Nothing was lost to interleaving.
            var final = new ApplicationSettingsStore(dir).LoadWithStatus();
            Assert.Equal(SettingsLoadStatus.Loaded, final.Status);
            Assert.Equal(1 + (4 * 15), final.Settings!.Connections.Count);
            Assert.Contains(final.Settings.Connections, c => c.Name == "SEED");

            // The single backup generation must also be a real settings file, not a half-written one.
            var backup = new ApplicationSettingsStore(dir).FilePath + ".bak";
            if (File.Exists(backup))
            {
                var bakDir = Path.Combine(dir, "bak");
                Directory.CreateDirectory(bakDir);
                File.Copy(backup, new ApplicationSettingsStore(bakDir).FilePath, overwrite: true);
                Assert.Equal(SettingsLoadStatus.Loaded, new ApplicationSettingsStore(bakDir).LoadWithStatus().Status);
            }

            // No orphaned temp files: a per-write name is only an improvement if the writes clean up after
            // themselves, otherwise the settings folder fills with files nobody can identify.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        });
    }

    // ─── Update: the locked read-modify-write (E) ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>No section facade may turn a failed read into defaults and then write them.</b>
    ///
    /// <para>The banned shape is <c>Load() ?? new ApplicationSettings()</c> in a type that also writes. It reads
    /// as harmless — it is the obvious way to say "or start fresh" — and it was the mechanism of a measured
    /// data-loss defect: <c>Load()</c> returns <c>null</c> for a MISSING file and for a file that merely could
    /// not be read at that instant, and the caller cannot tell which. <see cref="ApplicationSettingsStore.Update"/>
    /// is the replacement, and it is the ONE place allowed to create a default aggregate.</para>
    ///
    /// <para>⚠ It keys on the SOURCE, not on behaviour, for the reason gotcha #284 gives: a correctly-written
    /// facade and a dangerous one behave identically until the moment a read fails, which is exactly when no
    /// test is watching. ⭐ It fires only when the same file also WRITES — a read-only consumer that degrades to
    /// an empty aggregate is doing the right thing and needs no exemption entry, which keeps this guard free of
    /// an allowlist to maintain.</para>
    /// </summary>
    [Fact]
    public void NoSectionFacade_TurnsAFailedReadIntoDefaults()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EmberTern.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);

        var banned = new System.Text.RegularExpressions.Regex(@"Load\(\)\s*\?\?\s*new ApplicationSettings\(\)");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            // The store itself owns the one legal creation point, inside Update.
            if (Path.GetFileName(file) == "ApplicationSettingsStore.cs") continue;

            var source = File.ReadAllText(file);

            // ⚠ COMMENTS ARE STRIPPED FIRST, and that is not tidiness — it is the guard's own correctness.
            // Measured while writing this: the migration left explanatory comments QUOTING the banned shape
            // ("`Load() ?? new ApplicationSettings()` turned a transient read failure into DEFAULTS"), and the
            // guard reported the two files that document the fix as the two files that still had the bug.
            // A rule that a comment can violate teaches the next author to stop writing comments.
            var code = string.Join(
                '\n',
                source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            if (!banned.IsMatch(code)) continue;

            // Only a file that also WRITES can lose data this way.
            if (code.Contains(".Save(", StringComparison.Ordinal)
                || code.Contains(".Update(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(offenders.Count == 0,
            "These types substitute defaults for a settings read that may merely have FAILED, then write them — "
            + "the shape that silently replaced connection profiles and passwords: " + string.Join(", ", offenders)
            + ". Use ApplicationSettingsStore.Update instead, which creates defaults only for a genuinely "
            + "missing file and refuses for every other status.");
    }

    /// <summary>
    /// <see cref="SettingsLoadStatus.Missing"/> is the ONE status that may produce a default aggregate, and it
    /// still does — a fresh install must not be refused.
    /// </summary>
    [Fact]
    public void Update_OnMissingFile_CreatesDefaults()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            Assert.Equal(SettingsLoadStatus.Missing, store.LoadWithStatus().Status);

            var ok = store.Update(s => s.Connections.Add(new ConnectionProfile { Name = "FIRST" }));

            Assert.True(ok);
            Assert.Null(store.LastSaveDiagnostic);
            Assert.Equal("FIRST", Assert.Single(store.Load()!.Connections).Name);
        });
    }

    /// <summary>
    /// ⭐⭐ The heart of E: a file that cannot be read is never turned into defaults and written back. This is the
    /// A-03 refusal moved one step earlier — to the READ, where the damage is actually decided.
    /// </summary>
    [Fact]
    public void Update_OnUnreadableFile_RefusesAndLeavesTheFileIntact()
    {
        InTempDir(dir =>
        {
            // A real user's settings, written where they can be read…
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
            });
            var original = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            // …and now opened where decryption fails, exactly as on a copied Windows profile.
            var store = new ApplicationSettingsStore(dir, UndecryptableProtector());
            Assert.Equal(SettingsLoadStatus.Unreadable, store.LoadWithStatus().Status);

            var ok = store.Update(s => s.Connections.Add(new ConnectionProfile { Name = "WOULD-DESTROY" }));

            Assert.False(ok);
            Assert.NotNull(store.LastSaveDiagnostic);
            Assert.Equal(original, File.ReadAllText(Path.Combine(dir, "settings.dat")));

            // And the data really is still there for whoever can decrypt it.
            var recovered = new ApplicationSettingsStore(dir, FakeProtector()).Load();
            Assert.Equal("PROD", Assert.Single(recovered!.Connections).Name);
            Assert.Equal("secret", recovered.Connections[0].Password);
        });
    }

    /// <summary>
    /// Rule #11 in the small: a facade changes its own section and every other section survives byte-for-byte.
    /// This is what the whole read-modify-write shape exists for.
    /// </summary>
    [Fact]
    public void Update_PreservesEverySectionItDidNotTouch()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
                Folders = { Folders = { new FolderEntry { Id = "f1", Name = "Klienci" } } },
            });

            // A different facade's write — the grid-column-resize kind nobody thinks of as a write.
            new GridProfileStore(dir).Save(new GridProfile { GridId = "results" });

            var after = new ApplicationSettingsStore(dir).Load()!;
            Assert.Equal("PROD", Assert.Single(after.Connections).Name);
            Assert.Equal("secret", after.Connections[0].Password);
            Assert.Equal("Klienci", Assert.Single(after.Folders.Folders).Name);
            Assert.Equal("results", Assert.Single(after.UserSettings.GridProfiles).GridId);
        });
    }

    /// <summary>
    /// ⭐⭐ <b>The regression guard for the measured data-loss defect, and it is deterministic — no race is used
    /// to pin a race.</b>
    ///
    /// <para>The defect: <c>Load()</c> collapses every failure to <c>null</c>, so a facade doing
    /// <c>Load() ?? new ApplicationSettings()</c> could not tell "no file yet" from "could not read it just
    /// now", substituted DEFAULTS, and saved them over intact settings. <c>Save</c>'s A-03 guard did not stop
    /// it, because that guard judges the file at WRITE time and by then the transient condition has cleared.
    /// Measured against a concurrent publisher before the fix: <b>182 failed reads, 89 of which wrote defaults,
    /// ending with 0 of 5 profiles surviving.</b></para>
    ///
    /// <para>⭐ Determinism comes from making the read fail for a REASON THAT PERSISTS — a protector that cannot
    /// decrypt — instead of trying to hit a microsecond window. The class of failure is the same one the race
    /// produces (<c>Unreadable</c>, file intact); only the trigger is reliable. ⛔ Do not rewrite this as a
    /// timing loop.</para>
    /// </summary>
    [Fact]
    public void ATransientReadFailure_NeverWritesDefaults()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections =
                {
                    new ConnectionProfile { Name = "PROD", Password = "secret" },
                    new ConnectionProfile { Name = "TEST" },
                },
            });
            var original = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            // Every section facade, pointed at a file it cannot decrypt. Not one of them may write.
            new ConnectionProfileStore(dir, UndecryptableProtector()).Upsert(new ConnectionProfile { Name = "NEW" });
            new ConnectionProfileStore(dir, UndecryptableProtector()).Delete("whatever");
            new FolderStore(dir, UndecryptableProtector()).Save(new FolderState());
            new GridProfileStore(dir, UndecryptableProtector()).Save(new GridProfile { GridId = "g" });
            new EmberTern.Core.Workspace.WorkspaceStore(dir, UndecryptableProtector())
                .Save(new EmberTern.Core.Workspace.WorkspaceState());
            new WatchStore(dir, UndecryptableProtector()).Save("c", "o", new[] { "x" });
            new PreferencesStore(dir, UndecryptableProtector()).Save(new Preferences());
            new EmberTern.Core.Import.ImportProfileStore(dir, UndecryptableProtector())
                .SaveLastUsed("c", new EmberTern.Core.Import.ImportConfiguration());

            // The file is untouched, and the user's data is still recoverable by whoever can decrypt it.
            Assert.Equal(original, File.ReadAllText(Path.Combine(dir, "settings.dat")));
            var recovered = new ApplicationSettingsStore(dir, FakeProtector()).Load();
            Assert.Equal(2, recovered!.Connections.Count);
            Assert.Equal("secret", recovered.Connections.Single(c => c.Name == "PROD").Password);
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
