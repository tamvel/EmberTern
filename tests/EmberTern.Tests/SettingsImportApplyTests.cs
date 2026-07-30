using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.App.Settings;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using EmberTern.Core.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// Etap 5b — <b>writing an imported configuration into <c>settings.dat</c></b>.
///
/// <para>This is the sharpest rule #11 surface in the sprint: it touches connection profiles, credentials and
/// saved queries, and unlike the export it does not merely read. So the assertions here are mostly about what an
/// import must NOT do — leave an unselected section altered, duplicate a profile, erase a stored password, or
/// proceed without a recovery copy.</para>
/// </summary>
public sealed class SettingsImportApplyTests
{
    private readonly ITestOutputHelper _out;

    public SettingsImportApplyTests(ITestOutputHelper output) => _out = output;

    private const string Passphrase = "correct-horse-battery-staple";

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A representative settings file: two connections with passwords, a folder, a grid layout, a
    /// workspace and a preference away from its default.</summary>
    private static ApplicationSettings Populated(string tag) => new()
    {
        Connections =
        {
            new ConnectionProfile
            {
                Id = "conn-alpha", Name = tag + "-alpha", Host = "alpha.example",
                DatabasePath = "/db/alpha.fdb", Password = tag + "-secret-alpha",
                ClientLibraryPath = @"C:\local\" + tag + @"\fbclient.dll",
            },
            new ConnectionProfile
            {
                Id = "conn-beta", Name = tag + "-beta", Host = "beta.example",
                DatabasePath = "/db/beta.fdb", Password = tag + "-secret-beta",
            },
        },
        Folders = new FolderState
        {
            Folders = { new FolderEntry { Id = "folder-" + tag, Name = tag + " folder" } },
            ConnectionFolderMap = { ["conn-alpha"] = "folder-" + tag },
            ConnectionSortOrders = { ["conn-alpha"] = 7 },
            ExpandedNodeIds = { "folder-" + tag },
        },
        Workspace = new WorkspaceState
        {
            SidebarWidth = tag.Length * 10,
            WindowBounds = new WindowBounds { X = 1, Y = 2, Width = 300, Height = 400 },
        },
        UserSettings = new UserSettings
        {
            GridProfiles = { new GridProfile { GridId = "grid-" + tag, AutoFitColumns = false } },
            Preferences = new Preferences
            {
                Theme = PreferenceOptions.ThemeLight,
                FormatterKeywordCase = PreferenceOptions.CaseUpper,
            },
            ParameterHistory = { new ParameterHistoryEntry { ConnectionId = "conn-alpha", ObjectName = "SP_X" } },
        },
    };

    private static SettingsExportContent Exported(ApplicationSettings source, SettingsExportOptions options)
        => SettingsExportContent_FromRoundTrip(source, options);

    /// <summary>
    /// Goes through the real encryption rather than calling <c>BuildContent</c>: an import in production always
    /// arrives from a file, and a merge test that skipped the envelope could pass while the file half was broken.
    /// </summary>
    private static SettingsExportContent SettingsExportContent_FromRoundTrip(
        ApplicationSettings source, SettingsExportOptions options)
    {
        var text = SettingsExporter.Export(source, options, Passphrase, "9.9.9-test", iterations: 1000);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        var inspection = SettingsImportReader.Inspect(stream);
        Assert.Equal(SettingsImportStatus.Ok, inspection.Status);
        var opened = SettingsImportReader.Open(inspection, Passphrase);
        Assert.True(opened.IsUsable);
        return opened.Content!;
    }

    // ─── The headline guarantee ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Export → import → write → reload, and every section NOT taken is byte-identical to what it was.</b>
    ///
    /// <para>Asserted by serializing the whole aggregate before and after and comparing the untouched sections'
    /// JSON — not by spot-checking a field, because the failure this guards against is a merge that rebuilt a
    /// section it was never asked to touch, and a spot check would miss the part nobody thought of.</para>
    /// </summary>
    [Fact]
    public void OnlyTheSelectedSectionsChange_EverythingElseIsUntouched()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            var content = Exported(Populated("remote"), new SettingsExportOptions { Workspaces = true });
            var before = store.Load()!;
            var foldersBefore = Json(before.Folders);
            var gridsBefore = Json(before.UserSettings.GridProfiles);
            var workspaceBefore = Json(before.Workspace);
            var connectionsBefore = Json(before.Connections);

            // Take the preferences and nothing else.
            var result = SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Preferences = true });

            Assert.True(result.Applied);
            Assert.Equal(new[] { SettingsExportSections.Preferences }, result.AppliedSections);

            var after = store.Load()!;
            Assert.Equal(PreferenceOptions.ThemeLight, after.UserSettings.Preferences.Theme);
            Assert.Equal(foldersBefore, Json(after.Folders));
            Assert.Equal(gridsBefore, Json(after.UserSettings.GridProfiles));
            Assert.Equal(workspaceBefore, Json(after.Workspace));
            Assert.Equal(connectionsBefore, Json(after.Connections));
        });
    }

    /// <summary>
    /// ⭐ Merging by <c>Id</c>: re-importing the same file updates the same profiles and does NOT duplicate them,
    /// and a profile the file never mentions is left alone (§6.3.4).
    /// </summary>
    [Fact]
    public void ConnectionsMergeById_ARepeatedImportUpdatesRatherThanDuplicating()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            var local = Populated("local");
            local.Connections.Add(new ConnectionProfile { Id = "conn-local-only", Name = "only here" });
            store.Save(local);

            var content = Exported(Populated("remote"), new SettingsExportOptions());
            var selection = new SettingsImportSelection { Connections = true };

            Assert.True(SettingsImportApplier.Apply(store, content, selection).Applied);
            Assert.True(SettingsImportApplier.Apply(store, content, selection).Applied);

            var after = store.Load()!;
            _out.WriteLine("after: " + string.Join(", ", after.Connections.Select(c => c.Id + "=" + c.Name)));

            Assert.Equal(3, after.Connections.Count);
            Assert.Equal("remote-alpha", after.Connections.Single(c => c.Id == "conn-alpha").Name);
            Assert.Equal("only here", after.Connections.Single(c => c.Id == "conn-local-only").Name);
        });
    }

    /// <summary>
    /// ⚠⚠ <b>The subtlest way this feature could have destroyed data: an import that did not ask for passwords
    /// must not blank the ones already stored.</b>
    ///
    /// <para>An export without passwords carries every connection with an <i>empty</i> password — that is how the
    /// exporter omits them — so a merge that copied the incoming profile wholesale would erase a working
    /// credential as a side effect of importing a host name. The same applies to <c>ClientLibraryPath</c>, which
    /// never travels at all.</para>
    /// </summary>
    [Fact]
    public void ImportingConnectionsWithoutPasswords_KeepsTheLocalPasswordAndClientLibrary()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            // No Passwords opt-in on either side.
            var content = Exported(Populated("remote"), new SettingsExportOptions());
            Assert.False(content.CarriesPasswords);

            Assert.True(SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Connections = true, Passwords = true }).Applied);

            var alpha = store.Load()!.Connections.Single(c => c.Id == "conn-alpha");
            Assert.Equal("remote-alpha", alpha.Name);
            Assert.Equal("local-secret-alpha", alpha.Password);
            Assert.Equal(@"C:\local\local\fbclient.dll", alpha.ClientLibraryPath);
        });
    }

    /// <summary>And with the opt-in on both sides, the file's password does replace the local one — otherwise the
    /// checkbox would be decoration.</summary>
    [Fact]
    public void ImportingWithPasswords_ReplacesTheStoredPassword()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            var content = Exported(Populated("remote"), new SettingsExportOptions { Passwords = true });
            Assert.True(content.CarriesPasswords);

            Assert.True(SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Connections = true, Passwords = true }).Applied);

            Assert.Equal(
                "remote-secret-alpha",
                store.Load()!.Connections.Single(c => c.Id == "conn-alpha").Password);

            // ⚠ And taking the connections while declining the passwords keeps the local one even when the file
            // has one to offer — the decision is the user's, not the file's.
            store.Save(Populated("local"));
            Assert.True(SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Connections = true, Passwords = false }).Applied);
            Assert.Equal(
                "local-secret-alpha",
                store.Load()!.Connections.Single(c => c.Id == "conn-alpha").Password);
        });
    }

    /// <summary>Window geometry is the local machine's, always: it never travels, so importing a workspace must
    /// not replace it with nothing.</summary>
    [Fact]
    public void ImportingAWorkspace_KeepsTheLocalWindowBounds()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            var content = Exported(Populated("remote"), new SettingsExportOptions { Workspaces = true });
            Assert.True(SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Workspaces = true }).Applied);

            var after = store.Load()!;
            Assert.Equal(60, after.Workspace.SidebarWidth);   // "remote".Length * 10 — the file's value
            Assert.NotNull(after.Workspace.WindowBounds);      // the machine's own geometry, kept
            Assert.Equal(300, after.Workspace.WindowBounds!.Width);
        });
    }

    /// <summary>Folders merge rather than replace, and per-connection assignments survive an import that does not
    /// bring the connections with it.</summary>
    [Fact]
    public void FoldersMergeByIdAndKeepLocalEntries()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            var content = Exported(Populated("remote"), new SettingsExportOptions());
            Assert.True(SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Folders = true }).Applied);

            var folders = store.Load()!.Folders;
            Assert.Equal(2, folders.Folders.Count);
            Assert.Contains(folders.Folders, f => f.Id == "folder-local");
            Assert.Contains(folders.Folders, f => f.Id == "folder-remote");
            Assert.Equal("folder-remote", folders.ConnectionFolderMap["conn-alpha"]);
            Assert.Contains("folder-local", folders.ExpandedNodeIds);
            Assert.Contains("folder-remote", folders.ExpandedNodeIds);
        });
    }

    // ─── Refusals and the recovery copy ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The <c>.pre-import-&lt;stamp&gt;</c> copy exists after an import and is <b>still loadable</b> — which is
    /// what makes the operation undoable by hand, and what proves it was a COPY rather than a move (a move would
    /// have taken the merge base away and every unselected section would have come back as a default).
    /// </summary>
    [Fact]
    public void ThePreImportCopyIsKeptAndIsStillReadable()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));

            var content = Exported(Populated("remote"), new SettingsExportOptions());
            var result = SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Preferences = true, Connections = true });

            Assert.True(result.Applied);
            Assert.NotNull(result.PreservedAt);
            Assert.Contains(".pre-import-", result.PreservedAt!, StringComparison.Ordinal);
            Assert.True(File.Exists(result.PreservedAt));
            _out.WriteLine("preserved: " + Path.GetFileName(result.PreservedAt!));

            // Readable as settings, and holding the PRE-import values: a hand recovery is a file copy away.
            var rescueDir = Path.Combine(dir, "rescue");
            Directory.CreateDirectory(rescueDir);
            File.Copy(result.PreservedAt!, Path.Combine(rescueDir, "settings.dat"));
            var rescued = new ApplicationSettingsStore(rescueDir).LoadWithStatus();

            Assert.Equal(SettingsLoadStatus.Loaded, rescued.Status);
            Assert.Equal("local-alpha", rescued.Settings!.Connections.Single(c => c.Id == "conn-alpha").Name);
        });
    }

    /// <summary>
    /// ⚠ The store's refusal (§2.5 / audit A-03) reaches the caller, and <b>nothing is written or copied</b>.
    /// Verified by asking before the copy, which is why <c>CanSave</c> exists: a refusal must not leave a
    /// pre-import file behind for an import that never happened.
    /// </summary>
    [Fact]
    public void ARefusingStoreIsReported_AndNothingIsWrittenOrCopied()
    {
        InTempDir(dir =>
        {
            // A settings.dat this build cannot decrypt — DPAPI on the wrong Windows account.
            var writable = new SecretProtector(s => "ENC:" + s, s => s.Substring(4));
            new ApplicationSettingsStore(dir, writable).Save(Populated("local"));
            var undecryptable = new SecretProtector(
                s => "ENC:" + s,
                _ => throw new InvalidOperationException("Key not valid for use in specified state."));

            var store = new ApplicationSettingsStore(dir, undecryptable);
            var content = Exported(Populated("remote"), new SettingsExportOptions());

            var before = File.ReadAllText(Path.Combine(dir, "settings.dat"));
            var result = SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Preferences = true });

            Assert.Equal(SettingsImportApplyStatus.Refused, result.Status);
            Assert.Contains("Refusing to overwrite", result.Message, StringComparison.Ordinal);
            Assert.Null(result.PreservedAt);
            Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "settings.dat")));
            Assert.Empty(Directory.GetFiles(dir, "*.pre-import-*"));
            _out.WriteLine(result.Message);
        });
    }

    /// <summary>An import with nothing selected changes nothing, and says so rather than reporting a success that
    /// did nothing.</summary>
    [Fact]
    public void AnEmptySelectionChangesNothing()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));
            var before = File.ReadAllText(Path.Combine(dir, "settings.dat"));

            var content = Exported(Populated("remote"), new SettingsExportOptions());

            foreach (var selection in new[]
                     {
                         SettingsImportSelection.Nothing,
                         // Selected, but the file does not carry it — the same outcome, reached the other way.
                         new SettingsImportSelection { Workspaces = true },
                     })
            {
                var result = SettingsImportApplier.Apply(store, content, selection);
                Assert.Equal(SettingsImportApplyStatus.NothingSelected, result.Status);
                Assert.Empty(result.AppliedSections);
                Assert.Null(result.PreservedAt);
                Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "settings.dat")));
            }
        });
    }

    /// <summary>A section that never travels cannot arrive by any selection — proved from the applier's side, not
    /// only the exporter's.</summary>
    [Fact]
    public void ExecutionHistoryNeverArrives()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            var local = Populated("local");
            local.UserSettings.ParameterHistory.Clear();
            local.UserSettings.DebugWatches.Clear();
            store.Save(local);

            var remote = Populated("remote");
            remote.UserSettings.DebugWatches.Add(new DebugWatchEntry { ConnectionId = "c", ObjectName = "o" });

            var content = Exported(remote, new SettingsExportOptions
            {
                Preferences = true, GridProfiles = true, Folders = true, Connections = true,
                Passwords = true, Workspaces = true, ImportProfiles = true,
            });

            Assert.True(SettingsImportApplier.Apply(
                store, content, SettingsImportSelection.EverythingIn(content)).Applied);

            var after = store.Load()!;
            Assert.Empty(after.UserSettings.ParameterHistory);
            Assert.Empty(after.UserSettings.DebugWatches);
        });
    }

    // ─── The App seam: no facade may keep a pre-import snapshot ─────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>THE TRAP THIS ETAP WAS WARNED ABOUT.</b> An import writes <c>settings.dat</c> directly, so any
    /// in-memory holder loaded from it is stale afterwards — and the damage is not the stale read, it is the next
    /// write: <c>PreferencesStore.Save</c> persists a <b>whole</b> <c>Preferences</c>, so the next preference the
    /// user changes would carry the pre-import copy of every other field back to disk. Silent, unlogged, green
    /// build.
    ///
    /// <para>The test therefore does not stop at "the service sees the imported theme". It changes a
    /// <i>different</i> preference afterwards and requires the imported one to survive that write — which is the
    /// step that fails when nothing reloaded.</para>
    ///
    /// <para>⚠ It also requires <c>Changed</c> to have been raised, because that event is the app's ONE theme
    /// apply point (§13.2): without it the preference is correct and the window is still painted the old colour.</para>
    /// </summary>
    [Fact]
    public void AfterAnImport_NoFacadeKeepsItsPreImportSnapshot()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings());

            var service = new PreferencesService(new PreferencesStore(dir));
            Assert.Equal(PreferenceOptions.ThemeDark, service.Current.Theme);

            var refreshed = 0;
            var changed = 0;
            service.Changed += (_, _) => changed++;
            var portability = new SettingsPortability(store, service, "9.9.9-test", () => refreshed++);

            var content = Exported(
                new ApplicationSettings
                {
                    UserSettings = new UserSettings
                    {
                        Preferences = new Preferences { Theme = PreferenceOptions.ThemeLight },
                    },
                },
                new SettingsExportOptions());

            Assert.True(portability.Apply(content, new SettingsImportSelection { Preferences = true }).Applied);

            // The live snapshot moved, the app was told, and the one apply point fired.
            Assert.Equal(PreferenceOptions.ThemeLight, service.Current.Theme);
            Assert.Equal(1, refreshed);
            Assert.True(changed >= 1);

            // ⭐ The assertion that actually catches a missing reload: a later write of a DIFFERENT preference must
            // not carry the pre-import theme back.
            service.Apply(service.Current with { Language = PreferenceOptions.LanguageEnglish });
            Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);
        });
    }

    /// <summary>
    /// ⚠ Importing the <c>Workspaces</c> section arms the one-shot suppression of the app-close capture — without
    /// it the session would write its own tabs over the imported ones on exit and the import would silently undo
    /// itself. An import that does not take workspaces must NOT arm it, because suppressing a capture nobody asked
    /// to replace would lose the session's real work.
    /// </summary>
    [Fact]
    public void OnlyImportingWorkspaces_ArmsTheCloseCaptureSuppression()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(Populated("local"));
            var service = new PreferencesService(new PreferencesStore(dir));

            var withWorkspaces = Exported(Populated("remote"), new SettingsExportOptions { Workspaces = true });

            var quiet = new SettingsPortability(store, service, "9.9.9-test");
            Assert.True(quiet.Apply(withWorkspaces, new SettingsImportSelection { Preferences = true }).Applied);
            Assert.False(quiet.ImportedWorkspaces);

            var loud = new SettingsPortability(store, service, "9.9.9-test");
            Assert.True(loud.Apply(withWorkspaces, new SettingsImportSelection { Workspaces = true }).Applied);
            Assert.True(loud.ImportedWorkspaces);
        });
    }

    /// <summary>The seam exposes the settings folder — the <i>Open settings folder</i> button's target, and the
    /// only place in the UI this path is visible.</summary>
    [Fact]
    public void TheSeamExposesTheSettingsFolder()
    {
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            var portability = new SettingsPortability(
                store, new PreferencesService(new PreferencesStore(dir)), "9.9.9-test");

            Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(portability.SettingsFolder));
            Assert.Equal(Path.Combine(dir, "settings.dat"), portability.SettingsFilePath);
        });
    }

    private static string Json<T>(T value)
        => System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
}
