using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using EmberTern.Core.Workspace;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Shared fixtures for the Settings Center etap 5a export tests. One populated
/// <see cref="ApplicationSettings"/> with a distinctive value in <b>every</b> section, so that "this section
/// travelled" and "this section did not" are always distinguishable rather than both looking like a default.
/// </summary>
internal static class ExportFixtures
{
    /// <summary>A real passphrase, and deliberately not the empty string — an empty one is refused by design.</summary>
    internal const string Passphrase = "correct horse battery staple";

    /// <summary>
    /// A synthetic app version. ⚠ Not <c>AppInfo.Version</c>: the point of the seam is that Core takes the value
    /// as an input, and a test that read the real one would silently start passing for the wrong reason if the
    /// seam were ever replaced by an ambient read.
    /// </summary>
    internal const string AppVersion = "9.9.9-test";

    /// <summary>
    /// A low KDF iteration count for tests.
    /// <para>⭐ This is not a shortcut around the design — it is the design working. The count travels in the
    /// file's own header precisely so that it is a per-file parameter rather than a build-wide assumption, which
    /// is what lets tests run in milliseconds while production uses
    /// <c>PassphraseProtector.DefaultIterations</c>.</para>
    /// </summary>
    internal const int Iterations = 1_000;

    internal const string Secret = "s3cr3t-database-password";

    internal static ApplicationSettings Populated() => new()
    {
        SchemaVersion = ApplicationSettingsStore.CurrentSchemaVersion,
        Connections =
        {
            new ConnectionProfile
            {
                Id = "conn-1",
                Name = "Lab",
                Host = "localhost",
                Port = 3050,
                DatabasePath = @"C:\Lab\EmberTern_Lab.fdb",
                Username = "SYSDBA",
                Password = Secret,
                Charset = "WIN1250",
                Dialect = 3,
                ClientLibraryPath = @"C:\Program Files\Firebird\fbclient.dll",
                DeveloperMode = true,
                DataTransactionProfile = TransactionProfile.ReadCommitted,
                MetadataTransactionProfile = TransactionProfile.ReadCommitted,
            },
        },
        Folders = new FolderState { Folders = { new FolderEntry { Id = "f1", Name = "Production", SortOrder = 7 } } },
        Workspace = new WorkspaceState
        {
            WindowBounds = new WindowBounds { X = 11, Y = 22, Width = 333, Height = 444, WindowState = "Maximized" },
            SidebarWidth = 321,
            LastActiveConnectionId = "conn-1",
            ProcedureEasyMode = true,
            Workspaces =
            {
                ["conn-1"] = new ConnectionWorkspace
                {
                    ActiveTabIndex = 1,
                    Tabs = { new WorkspaceTab { Kind = WorkspaceTabKind.Query, SqlText = "select 1 from rdb$database" } },
                    SavedQueries = { new SavedQuery { Id = "q1", Name = "Query 1", SqlText = "select 2" } },
                },
            },
        },
        UserSettings = new UserSettings
        {
            Preferences = new Preferences
            {
                Theme = PreferenceOptions.ThemeLight,
                FormatterKeywordCase = PreferenceOptions.CaseUpper,
            },
            GridProfiles = { new GridProfile { GridId = "QueryResults", AutoFitColumns = true } },
            ParameterHistory = { new ParameterHistoryEntry { ConnectionId = "conn-1", ObjectName = "SP_X" } },
            DebugWatches = { new DebugWatchEntry { ConnectionId = "conn-1", ObjectName = "SP_X" } },
            ImportProfiles = { new ImportProfile { Id = "p1", Name = "Monthly" } },
        },
    };
}

/// <summary>
/// Settings Center etap 5a — <b>what travels</b> (design §6.3.4) and the export/import round trip.
///
/// <para>⭐ This is a rule #11 surface: the file carries the user's connection profiles and, on request, their
/// database credentials. So the classification is tested three ways rather than one — the policy directly
/// (<see cref="SettingsExporter.BuildContent"/>), the round trip through real encryption, and a
/// <b>reflection guard per persisted type</b> that fails the build when someone adds a field without deciding
/// whether it travels. The last one is the important one: the first two only ever test the fields somebody
/// thought about.</para>
/// </summary>
public class SettingsExportContentTests
{
    // ─── ROUND TRIP, EVERY COMBINATION OF SECTIONS ──────────────────────────────────────────

    public static IEnumerable<object[]> SectionCombinations()
    {
        // Six independent sections × the password opt-in. The all-off case is excluded because an export with
        // nothing in it is refused rather than written (see RefusesAnEmptySelection).
        //
        // ⚠ The mask travels as an int rather than a ready-made SettingsExportOptions so every case is
        // xunit-serializable — otherwise the runner cannot pre-enumerate them and a 128-case theory reports as one
        // opaque test.
        for (var mask = 1; mask < 64; mask++)
        {
            yield return new object[] { mask, false };
            yield return new object[] { mask, true };
        }
    }

    [Theory]
    [MemberData(nameof(SectionCombinations))]
    public void ExportThenImport_RoundTripsExactlyTheSelectedSections(int mask, bool passwords)
    {
        var options = new SettingsExportOptions
        {
            Preferences = (mask & 1) != 0,
            GridProfiles = (mask & 2) != 0,
            Folders = (mask & 4) != 0,
            Connections = (mask & 8) != 0,
            Workspaces = (mask & 16) != 0,
            ImportProfiles = (mask & 32) != 0,
            Passwords = passwords,
        };

        var source = ExportFixtures.Populated();

        var file = SettingsExporter.Export(
            source, options, ExportFixtures.Passphrase, ExportFixtures.AppVersion, ExportFixtures.Iterations);
        var imported = Open(file);

        var settings = imported.Settings;

        // Each section is present exactly when it was asked for — and its DATA agrees with that claim, so a
        // section list that lies would fail here rather than being believed.
        Assert.Equal(options.Preferences, imported.Contains(SettingsExportSections.Preferences));
        Assert.Equal(options.Preferences ? PreferenceOptions.ThemeLight : PreferenceOptions.ThemeDark,
            settings.UserSettings.Preferences.Theme);

        Assert.Equal(options.GridProfiles, imported.Contains(SettingsExportSections.GridProfiles));
        Assert.Equal(options.GridProfiles ? 1 : 0, settings.UserSettings.GridProfiles.Count);

        Assert.Equal(options.Folders, imported.Contains(SettingsExportSections.Folders));
        Assert.Equal(options.Folders ? 1 : 0, settings.Folders.Folders.Count);

        Assert.Equal(options.Connections, imported.Contains(SettingsExportSections.Connections));
        Assert.Equal(options.Connections ? 1 : 0, settings.Connections.Count);

        Assert.Equal(options.Workspaces, imported.Contains(SettingsExportSections.Workspaces));
        Assert.Equal(options.Workspaces ? 1 : 0, settings.Workspace.Workspaces.Count);

        Assert.Equal(options.ImportProfiles, imported.Contains(SettingsExportSections.ImportProfiles));
        Assert.Equal(options.ImportProfiles ? 1 : 0, settings.UserSettings.ImportProfiles.Count);

        // ⚠ Passwords are a statement ABOUT the connections, so the section is recorded only when there are
        // connections to attach it to.
        Assert.Equal(options.Connections && options.Passwords,
            imported.Contains(SettingsExportSections.Passwords));
        Assert.Equal(options.Connections && options.Passwords, imported.CarriesPasswords);

        if (options.Connections)
        {
            var connection = settings.Connections[0];

            // The substance of a profile is what makes an export worth having on a second machine.
            Assert.Equal("Lab", connection.Name);
            Assert.Equal(@"C:\Lab\EmberTern_Lab.fdb", connection.DatabasePath);
            Assert.Equal("WIN1250", connection.Charset);
            Assert.True(connection.DeveloperMode);

            Assert.Equal(options.Passwords ? ExportFixtures.Secret : string.Empty, connection.Password);

            // ❌ Never travels, whatever was selected: a local path, meaningful only in Embedded mode.
            Assert.Equal(string.Empty, connection.ClientLibraryPath);
            // ❌ The v1→v2 shim never travels either.
            Assert.Null(connection.LegacyTransactionProfile);
        }

        // ❌ Monitor geometry never travels — importing it can place the window off-screen.
        Assert.Null(settings.Workspace.WindowBounds);
        // ❌ Execution history is not settings, and there is no option that could include it.
        Assert.Empty(settings.UserSettings.ParameterHistory);
        Assert.Empty(settings.UserSettings.DebugWatches);
    }

    [Fact]
    public void WorkspacesOptIn_CarriesTabsSqlAndSavedQueries_ButNeverWindowBounds()
    {
        var imported = Open(Write(new SettingsExportOptions { Workspaces = true }));

        Assert.True(imported.Settings.Workspace.Workspaces.TryGetValue("conn-1", out var workspace));
        Assert.Equal("select 1 from rdb$database", workspace!.Tabs[0].SqlText);
        Assert.Equal("Query 1", workspace.SavedQueries[0].Name);

        // Layout preference rides along with the opt-in; monitor geometry does not.
        Assert.Equal(321, imported.Settings.Workspace.SidebarWidth);
        Assert.True(imported.Settings.Workspace.ProcedureEasyMode);
        Assert.Null(imported.Settings.Workspace.WindowBounds);
    }

    // ─── THE POLICY, DIRECTLY ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuildContent_NeverMutatesTheLiveSettings()
    {
        // ⚠ The failure this prevents is the worst kind available here: exporting without passwords would strip
        // the password out of the RUNNING app, and the user would discover it at the next connect.
        var source = ExportFixtures.Populated();

        SettingsExporter.BuildContent(source, new SettingsExportOptions { Connections = true });

        Assert.Equal(ExportFixtures.Secret, source.Connections[0].Password);
        Assert.NotEqual(string.Empty, source.Connections[0].ClientLibraryPath);
        Assert.NotNull(source.Workspace.WindowBounds);
    }

    [Fact]
    public void WithoutThePasswordOptIn_ThePasswordIsNotInThePayloadAtAll()
    {
        var content = SettingsExporter.BuildContent(
            ExportFixtures.Populated(), new SettingsExportOptions { Connections = true });

        var json = JsonSerializer.Serialize(content);

        Assert.DoesNotContain(ExportFixtures.Secret, json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWrittenFileNeverShowsAPassword_EvenWhenPasswordsAreIncluded()
    {
        // Proves the encryption actually happened rather than being assumed — the one assertion that would fail
        // if a future change ever wrote the payload in the clear.
        var file = Write(new SettingsExportOptions { Connections = true, Passwords = true });
        var payload = file[(file.IndexOf('\n') + 1)..];

        // ⚠ The secret is chosen so this assertion cannot pass by coincidence: it contains a '-', which is not in
        // the Base64 alphabet, so it can never appear in an encrypted payload — whereas a plaintext one would
        // carry it verbatim. (A short value like the profile NAME would be useless here: "Lab" is three Base64
        // characters and turns up in ciphertext by chance, which is how this test first failed.)
        Assert.DoesNotContain(ExportFixtures.Secret, file, StringComparison.Ordinal);

        // And the payload is not JSON at all: '{' is likewise outside the Base64 alphabet.
        Assert.DoesNotContain("{", payload, StringComparison.Ordinal);

        // …while the header stays readable, which is what makes the ordered checks possible.
        Assert.StartsWith(SettingsExportFormat.Magic, file, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnEmptySelection()
    {
        var nothing = new SettingsExportOptions
        {
            Preferences = false, GridProfiles = false, Folders = false, Connections = false,
        };

        Assert.True(nothing.IsEmpty);
        Assert.Throws<ArgumentException>(() => SettingsExporter.Export(
            ExportFixtures.Populated(), nothing, ExportFixtures.Passphrase, ExportFixtures.AppVersion,
            ExportFixtures.Iterations));
    }

    [Fact]
    public void RefusesAnEmptyPassphrase_BecauseEveryExportIsEncrypted()
    {
        // Ratified Q3 made "an unencrypted export" unrepresentable rather than merely discouraged.
        Assert.Throws<ArgumentException>(() => SettingsExporter.Export(
            ExportFixtures.Populated(), new SettingsExportOptions(), string.Empty, ExportFixtures.AppVersion,
            ExportFixtures.Iterations));
    }

    [Fact]
    public void TheDefaultOptions_AreTheRatifiedClassification()
    {
        var defaults = new SettingsExportOptions();

        // ✅ portable by default
        Assert.True(defaults.Preferences);
        Assert.True(defaults.GridProfiles);
        Assert.True(defaults.Folders);
        Assert.True(defaults.Connections);
        // ⚠ opt-in only
        Assert.False(defaults.Passwords);
        Assert.False(defaults.Workspaces);
        Assert.False(defaults.ImportProfiles);
    }

    [Fact]
    public void EverySectionName_IsReachableFromSomeOptions()
    {
        // A section constant nobody can produce is a mechanism with no consumer (gotcha #233) — and it would be
        // invisible, because every round-trip test would simply never mention it.
        var everything = new SettingsExportOptions
        {
            Workspaces = true, ImportProfiles = true, Passwords = true,
        };

        Assert.Equal(SettingsExportSections.All.OrderBy(s => s, StringComparer.Ordinal),
            everything.Sections().OrderBy(s => s, StringComparer.Ordinal));
    }

    // ─── REFLECTION GUARDS — adding a field must FORCE a decision ────────────────────────────

    [Fact]
    public void EveryConnectionProfileField_IsAccountedForInTheExport()
    {
        // ⚠ The one that matters most. A new field on ConnectionProfile is exactly where a local path or a second
        // credential would arrive, and neither the round-trip nor the policy test would notice it.
        AssertAccountedFor<ConnectionProfile>(new Dictionary<string, string>
        {
            [nameof(ConnectionProfile.Id)] = "✅ exported — identity, and what makes a re-import update rather than duplicate",
            [nameof(ConnectionProfile.Name)] = "✅ exported",
            [nameof(ConnectionProfile.Host)] = "✅ exported",
            [nameof(ConnectionProfile.Port)] = "✅ exported",
            [nameof(ConnectionProfile.DatabasePath)] = "✅ exported — usually identical on a second machine",
            [nameof(ConnectionProfile.Username)] = "✅ exported",
            [nameof(ConnectionProfile.Charset)] = "✅ exported",
            [nameof(ConnectionProfile.Dialect)] = "✅ exported",
            [nameof(ConnectionProfile.DeveloperMode)] = "✅ exported — a per-connection preference",
            [nameof(ConnectionProfile.DataTransactionProfile)] = "✅ exported",
            [nameof(ConnectionProfile.MetadataTransactionProfile)] = "✅ exported",
            [nameof(ConnectionProfile.Password)] = "⚠ OPT-IN only (Q2) — credentials in a file that travels",
            [nameof(ConnectionProfile.ClientLibraryPath)] = "❌ never — a local filesystem path, Embedded mode only",
            [nameof(ConnectionProfile.LegacyTransactionProfile)] = "❌ never — the v1→v2 migration shim, cleared on export",
        });
    }

    [Fact]
    public void EveryApplicationSettingsSection_IsAccountedForInTheExport()
    {
        AssertAccountedFor<ApplicationSettings>(new Dictionary<string, string>
        {
            [nameof(ApplicationSettings.SchemaVersion)] = "✅ exported — the settings-shape axis, migrated by the existing ladder",
            [nameof(ApplicationSettings.Connections)] = "✅ section: Connections",
            [nameof(ApplicationSettings.Folders)] = "✅ section: Folders",
            [nameof(ApplicationSettings.Workspace)] = "⚠ section: Workspaces (opt-in, Q6), minus WindowBounds",
            [nameof(ApplicationSettings.UserSettings)] = "✅ split across the Preferences / GridProfiles / ImportProfiles sections",
        });
    }

    [Fact]
    public void EveryUserSettingsMember_IsAccountedForInTheExport()
    {
        AssertAccountedFor<UserSettings>(new Dictionary<string, string>
        {
            [nameof(UserSettings.Preferences)] = "✅ section: Preferences",
            [nameof(UserSettings.GridProfiles)] = "✅ section: GridProfiles",
            [nameof(UserSettings.ImportProfiles)] = "⚠ section: ImportProfiles (opt-in — they embed source file paths)",
            [nameof(UserSettings.ParameterHistory)] = "❌ never — execution history keyed to connection ids, not settings",
            [nameof(UserSettings.DebugWatches)] = "❌ never — same",
        });
    }

    [Fact]
    public void EveryWorkspaceStateMember_IsAccountedForInTheExport()
    {
        // Everything here travels with the Workspaces opt-in except the one ❌ row, so this guard exists to make
        // a future addition state which of the two it is — a second piece of monitor geometry, say.
        AssertAccountedFor<WorkspaceState>(new Dictionary<string, string>
        {
            [nameof(WorkspaceState.WindowBounds)] = "❌ never — monitor geometry; importing it can place the window off-screen",
            [nameof(WorkspaceState.Workspaces)] = "⚠ with the Workspaces opt-in — tabs, SQL text, saved queries",
            [nameof(WorkspaceState.LastActiveConnectionId)] = "⚠ with the Workspaces opt-in",
            [nameof(WorkspaceState.QueryPanelVisible)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.SidebarWidth)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.SidebarCollapsed)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.ResultsPanelHeight)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.ResultsMaximized)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.BottomPanelTabIndex)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.ProcedureEasyMode)] = "⚠ with the Workspaces opt-in — a Source/Easy seed",
            [nameof(WorkspaceState.ViewEasyMode)] = "⚠ with the Workspaces opt-in — a Source/Easy seed",
            [nameof(WorkspaceState.TriggerEasyMode)] = "⚠ with the Workspaces opt-in — a Source/Easy seed",
            [nameof(WorkspaceState.FunctionEasyMode)] = "⚠ with the Workspaces opt-in — a Source/Easy seed",
            [nameof(WorkspaceState.ImportPreviewPanelHeight)] = "⚠ with the Workspaces opt-in — layout preference",
            [nameof(WorkspaceState.ImportPreviewPanelCollapsed)] = "⚠ with the Workspaces opt-in — layout preference",
        });
    }

    private static void AssertAccountedFor<T>(IReadOnlyDictionary<string, string> declared)
    {
        var actual = typeof(T).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var recorded = declared.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        var undecided = actual.Except(recorded, StringComparer.Ordinal).ToArray();
        var stale = recorded.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.True(undecided.Length == 0,
            $"{typeof(T).Name} gained {string.Join(", ", undecided)} and the settings export has no decision "
            + "recorded for it. Decide whether it travels (§6.3.4), implement it in SettingsExporter.BuildContent, "
            + "and record it in this table — an unrecorded field silently rides along with its section, which for "
            + "a credential or a local path is a defect.");

        Assert.True(stale.Length == 0,
            $"this table records {string.Join(", ", stale)} for {typeof(T).Name}, which no longer exists.");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────

    private static string Write(SettingsExportOptions options) => SettingsExporter.Export(
        ExportFixtures.Populated(), options, ExportFixtures.Passphrase, ExportFixtures.AppVersion,
        ExportFixtures.Iterations);

    private static SettingsExportContent Open(string file)
    {
        var result = SettingsImportReader.Open(
            SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(file)), ExportFixtures.Passphrase);
        Assert.Equal(SettingsImportStatus.Ok, result.Status);
        Assert.True(result.IsUsable);
        return result.Content!;
    }
}
