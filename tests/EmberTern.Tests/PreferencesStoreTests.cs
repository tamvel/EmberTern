using System;
using System.IO;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Settings Center etap 2 — <c>PreferencesStore</c>, the 8th section facade over the shared
/// <c>settings.dat</c> (mirrors <c>WatchStoreTests</c> / <c>GridProfileStoreTests</c>).
/// <para>
/// Three of these are about safety rather than round-tripping, and they are the reason the class exists in
/// this shape: <see cref="Load_NormalizesWhatItReads_ButNeverWritesTheFile"/> (a writing <c>Load</c> is audit
/// A-03's shape), <see cref="Save_ReportsRefusal_OverAFileItCannotRead"/> (a settings surface that appears to
/// accept a change and persists nothing), and
/// <see cref="Load_OfAFileWrittenBeforePreferencesExisted_YieldsDefaults"/> (additive — no schema bump).
/// </para>
/// </summary>
public class PreferencesStoreTests
{
    // Reversible stand-in for DPAPI, matching ApplicationSettingsStoreTests: "x" -> "ENC:x".
    private static SecretProtector FakeProtector() =>
        new(s => "ENC:" + s, s => s.StartsWith("ENC:", StringComparison.Ordinal)
            ? s.Substring(4)
            : throw new FormatException("not an ENC: blob"));

    // Encrypts fine but can never decrypt — precisely DPAPI on the wrong Windows account.
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

    // ─── ROUND TRIP ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsDefaults_WhenNothingIsSaved()
    {
        InTempDir(dir => Assert.Equal(new Preferences(), new PreferencesStore(dir).Load()));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAcrossInstances()
    {
        InTempDir(dir =>
        {
            Assert.True(new PreferencesStore(dir).Save(new Preferences
            {
                Theme = PreferenceOptions.ThemeLight,
                FormatterKeywordCase = PreferenceOptions.CaseUpper,
            }));

            var loaded = new PreferencesStore(dir).Load();
            Assert.Equal(PreferenceOptions.ThemeLight, loaded.Theme);
            Assert.Equal(PreferenceOptions.CaseUpper, loaded.FormatterKeywordCase);
            // Untouched properties keep their defaults rather than blanking.
            Assert.Equal(PreferenceOptions.LanguageEnglish, loaded.Language);
            Assert.Equal(PreferenceOptions.CaseLower, loaded.FormatterIdentifierCase);
        });
    }

    [Fact]
    public void Save_DoesNotMutateTheCallersObject()
    {
        InTempDir(dir =>
        {
            var mine = new Preferences { Theme = "chartreuse" };
            new PreferencesStore(dir).Save(mine);
            Assert.Equal("chartreuse", mine.Theme);
        });
    }

    /// <summary>A value we would only have to correct on the next read has no business reaching the file, so
    /// normalization runs in both directions across the boundary.</summary>
    [Fact]
    public void Save_NormalizesBeforeWriting()
    {
        InTempDir(dir =>
        {
            new PreferencesStore(dir).Save(new Preferences { Theme = "chartreuse", Language = "kl" });

            // Read the raw section, bypassing the facade, so this proves what is ON DISK rather than what
            // Load would have corrected anyway.
            var stored = new ApplicationSettingsStore(dir).Load()!.UserSettings.Preferences;
            Assert.Equal(PreferenceOptions.ThemeDark, stored.Theme);
            Assert.Equal(PreferenceOptions.LanguageEnglish, stored.Language);
        });
    }

    // ─── THE SECTION FACADE CONTRACT ────────────────────────────────────────────────────────

    /// <summary>Read-modify-write on one section: saving a preference must not disturb Connections, Folders,
    /// Workspace or any of the four <c>UserSettings</c> lists in the shared file.</summary>
    [Fact]
    public void Save_PreservesEveryOtherSection()
    {
        InTempDir(dir =>
        {
            var raw = new ApplicationSettingsStore(dir);
            raw.Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
                UserSettings = { GridProfiles = { new GridProfile { GridId = "g1" } } },
            });

            Assert.True(new PreferencesStore(dir).Save(new Preferences { Theme = PreferenceOptions.ThemeLight }));

            var after = new ApplicationSettingsStore(dir).Load()!;
            Assert.Equal("PROD", Assert.Single(after.Connections).Name);
            Assert.Equal("secret", after.Connections[0].Password);
            Assert.Equal("g1", Assert.Single(after.UserSettings.GridProfiles).GridId);
            Assert.Equal(PreferenceOptions.ThemeLight, after.UserSettings.Preferences.Theme);
        });
    }

    /// <summary>The other facades must not clobber preferences either — the same read-modify-write rule seen
    /// from the outside.</summary>
    [Fact]
    public void AnotherFacadesWrite_PreservesPreferences()
    {
        InTempDir(dir =>
        {
            new PreferencesStore(dir).Save(new Preferences { Theme = PreferenceOptions.ThemeLight });
            new WatchStore(dir).Save("c1", "SP", new[] { "a" });

            Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);
        });
    }

    // ─── NORMALIZATION AT THE READ BOUNDARY, WITHOUT WRITING ────────────────────────────────

    /// <summary>
    /// ⚠ The correction lives in memory and reaches disk only if something later saves for its own reasons.
    /// A "repair the file on load" write is precisely the shape audit A-03 was about.
    /// </summary>
    [Fact]
    public void Load_NormalizesWhatItReads_ButNeverWritesTheFile()
    {
        InTempDir(dir =>
        {
            // Plant a value no build would write, through the raw store (which does not normalize).
            var raw = new ApplicationSettingsStore(dir);
            raw.Save(new ApplicationSettings
            {
                UserSettings = { Preferences = new Preferences { Theme = "chartreuse" } },
            });
            var before = File.ReadAllText(raw.FilePath);
            var beforeStamp = File.GetLastWriteTimeUtc(raw.FilePath);

            Assert.Equal(PreferenceOptions.ThemeDark, new PreferencesStore(dir).Load().Theme);

            Assert.Equal(before, File.ReadAllText(raw.FilePath));
            Assert.Equal(beforeStamp, File.GetLastWriteTimeUtc(raw.FilePath));
        });
    }

    /// <summary>An undecryptable or corrupt file yields defaults rather than throwing — but that is a
    /// degradation, not permission to write (see <see cref="Save_ReportsRefusal_OverAFileItCannotRead"/>).</summary>
    [Fact]
    public void Load_YieldsDefaults_WhenTheFileCannotBeRead()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                UserSettings = { Preferences = new Preferences { Theme = PreferenceOptions.ThemeLight } },
            });

            Assert.Equal(new Preferences(), new PreferencesStore(dir, UndecryptableProtector()).Load());
        });
    }

    // ─── ADDITIVE: NO SCHEMA BUMP ───────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>settings.dat</c> written before <c>Preferences</c> existed has no such key. It must load, yield
    /// defaults, and keep every other section — which is what "additive" has to mean in practice.
    /// </summary>
    [Fact]
    public void Load_OfAFileWrittenBeforePreferencesExisted_YieldsDefaults()
    {
        InTempDir(dir =>
        {
            // Hand-built because the current model always serializes a Preferences node. Identity protector
            // (the default when none is injected), so the payload is readable JSON.
            const string json = """
                {
                  "SchemaVersion": 2,
                  "Connections": [ { "Name": "PROD" } ],
                  "Folders": {},
                  "Workspace": {},
                  "UserSettings": { "GridProfiles": [], "ParameterHistory": [], "DebugWatches": [] }
                }
                """;
            var path = Path.Combine(dir, "settings.dat");
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, SettingsFileContainer.Wrap(
                SettingsFileContainer.CurrentContainerVersion, EncryptionSchemes.None, json));

            Assert.Equal(new Preferences(), new PreferencesStore(dir).Load());
            Assert.Equal("PROD", Assert.Single(new ApplicationSettingsStore(dir).Load()!.Connections).Name);
        });
    }

    /// <summary>
    /// ⭐ The settings schema version stays 2 — adding a property is additive, and a bump would trip the
    /// store's downgrade protection and make an OLDER build refuse the whole file. Pinned so the next person
    /// adding a preference does not reach for it out of tidiness.
    /// </summary>
    [Fact]
    public void AddingPreferences_DidNotBumpTheSchemaVersion()
    {
        Assert.Equal(2, ApplicationSettingsStore.CurrentSchemaVersion);
    }

    // ─── SAVE MUST REPORT A REFUSAL ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>Save</c> refuses silently over a settings.dat this build could not read — correct, because the
    /// values being written would be defaults standing in for data still sitting in that file. Silence is
    /// right for the app's incidental writers; a surface whose whole purpose is "change this setting" is the
    /// one place it is wrong, so the facade has to hand that fact back.
    /// </summary>
    [Fact]
    public void Save_ReportsRefusal_OverAFileItCannotRead()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "PROD", Password = "secret" } },
            });
            var path = Path.Combine(dir, "settings.dat");
            var original = File.ReadAllText(path);

            var store = new PreferencesStore(dir, UndecryptableProtector());
            Assert.False(store.Save(new Preferences { Theme = PreferenceOptions.ThemeLight }));
            Assert.NotNull(store.LastSaveDiagnostic);
            Assert.Contains("Refusing to overwrite", store.LastSaveDiagnostic!, StringComparison.Ordinal);

            // And the user's data is untouched, which is what the refusal is for.
            Assert.Equal(original, File.ReadAllText(path));
        });
    }

    [Fact]
    public void Save_ReportsSuccess_AndClearsTheDiagnostic_OnANormalWrite()
    {
        InTempDir(dir =>
        {
            var store = new PreferencesStore(dir);
            Assert.True(store.Save(new Preferences { Theme = PreferenceOptions.ThemeLight }));
            Assert.Null(store.LastSaveDiagnostic);
        });
    }

    [Fact]
    public void FilePath_PointsAtTheSharedSettingsFile()
    {
        InTempDir(dir =>
            Assert.Equal(Path.Combine(dir, "settings.dat"), new PreferencesStore(dir).FilePath));
    }
}
