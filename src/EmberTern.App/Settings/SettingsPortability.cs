using System;
using System.IO;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using EmberTern.Core.Workspace;

namespace EmberTern.App.Settings;

/// <summary>
/// ⭐ The App's ONE owner of settings export and import: it holds the store, supplies the app version Core
/// deliberately cannot see, and — the part that matters — knows <b>everything the running app must be told after
/// an import has written <c>settings.dat</c> behind its back</b>.
///
/// <para><b>Why that last job needs an owner at all.</b> Etap 3 learned that two holders of a
/// <see cref="Preferences"/> snapshot silently overwrite each other (§13.1), and answered it with one
/// <see cref="PreferencesService"/>. An import is the same failure one level up and across more sections: it
/// replaces the file that several in-memory holders were loaded from, and every one of them would then be stale.
/// The list below is <b>measured, not assumed</b> — each entry was read in the code before it was written here:</para>
///
/// <list type="table">
///   <item><term><c>PreferencesService</c></term><description>Holds a live <see cref="Preferences"/>. Reloaded
///   here. ⭐ Its <c>Changed</c> event is also what repaints an imported theme, through the app's single apply
///   point — an import adds no second one.</description></item>
///   <item><term><c>MainWindowViewModel._folderState</c></term><description>Loaded once in the constructor and
///   mutated in place for the rest of the session. Reloaded through <see cref="AfterImport"/>.</description></item>
///   <item><term>Connections</term><description>No snapshot — <c>ReloadConnections()</c> re-reads
///   <c>LoadAll()</c> every time. Rebuilt through <see cref="AfterImport"/> because the tree has to show the
///   imported profiles.</description></item>
///   <item><term><c>GridProfileStore</c></term><description>No snapshot — <c>Get</c> reads the file per call. An
///   already-built grid keeps the layout it applied; an imported layout takes effect when that grid is next
///   built. Stated, not hidden.</description></item>
///   <item><term>Workspaces</term><description>⚠ The one section that cannot take effect live — see
///   <see cref="ImportedWorkspaces"/>.</description></item>
///   <item><term><c>ParameterHistoryStore</c> / <c>WatchStore</c></term><description>Nothing to do: neither
///   section can be exported, so neither can be imported.</description></item>
/// </list>
/// </summary>
public sealed class SettingsPortability
{
    private readonly ApplicationSettingsStore _store;
    private readonly PreferencesService _preferences;
    private readonly string _appVersion;

    /// <param name="store">Over the same <c>settings.dat</c> and the same protector as every other section
    /// facade (gotcha #88) — an Identity-protector store here would rewrite the DPAPI-protected file
    /// unencrypted.</param>
    /// <param name="preferences">The app's one preferences owner, reloaded after an import.</param>
    /// <param name="appVersion">From <c>AppInfo.Version</c>. Written into the export header for diagnostics and
    /// never branched on — which is why Core takes it as an input it cannot derive (§15.3a).</param>
    /// <param name="afterImport">What the rest of the app must do once the file has changed. Optional so a test
    /// can exercise the file half on its own.</param>
    public SettingsPortability(
        ApplicationSettingsStore store,
        PreferencesService preferences,
        string appVersion,
        Action? afterImport = null)
    {
        _store = store;
        _preferences = preferences;
        _appVersion = appVersion;
        AfterImport = afterImport;
    }

    /// <summary>Invoked after a successful import, once <see cref="PreferencesService"/> has been reloaded. Owned
    /// by <c>MainWindowViewModel</c>, because the holders it refreshes are its.</summary>
    public Action? AfterImport { get; set; }

    /// <summary>
    /// ⚠⚠ <b>The live workspace, supplied by <c>MainWindow</c> — because <c>settings.dat</c> does not have it.</b>
    ///
    /// <para>Every other exportable section is written to the file the moment the user changes it: preferences
    /// apply-on-change, connections and folders save on edit, import profiles save on use. <b><c>Workspace</c> is
    /// the one exception — it is captured once, at app close.</b> So a mid-session <c>_store.Load()</c> returns the
    /// workspace of the <i>previous</i> session, and an export taken while the user has tabs open would carry work
    /// they did not do — which then imports "successfully" and restores the wrong tabs. That is the etap-5b QA
    /// defect, and it was a defect of <b>where the export read from</b>, not of the import, the merge or the
    /// close-capture suppression (all three were correct).</para>
    ///
    /// <para>⭐ The hook is a <see cref="Func{TResult}"/> for the same reason <see cref="AfterImport"/> is an
    /// <see cref="Action"/>: the complete state is the <i>View's</i> to build — sidebar width, the results-panel
    /// height and the import panel live on controls, not on the view model — so Core and this seam ask for it
    /// rather than reassembling it from parts they can reach. <c>MainWindow.CaptureLiveWorkspaceState()</c> is the
    /// ONE builder, shared with the app-close save, so an export and a close can never disagree about what "the
    /// workspace" means.</para>
    ///
    /// <para>⚠ Reading it is still <b>read-only</b> (ratified §15.9/4): the captured state is written into the
    /// in-memory copy the exporter is handed, never into <c>settings.dat</c>. Exporting must not persist a
    /// workspace the user has not closed the app on.</para>
    ///
    /// <para>Unset (a test, or before the window has restored) simply falls back to the persisted section — the
    /// previous behaviour, which is correct whenever there is no live session to be newer than the file.</para>
    /// </summary>
    public Func<WorkspaceState>? CaptureLiveWorkspace { get; set; }

    /// <summary>The folder holding <c>settings.dat</c>, its <c>.bak</c>, and any preserved copies — what the
    /// <i>Open settings folder</i> button opens. Cheap, and the only place in the UI this path is visible.</summary>
    public string SettingsFolder => Path.GetDirectoryName(_store.FilePath)!;

    public string SettingsFilePath => _store.FilePath;

    /// <summary>
    /// ⚠ True once an import in this session brought the <c>Workspaces</c> section in.
    ///
    /// <para><b>The problem it solves, which no amount of care inside the import can:</b> the window captures the
    /// live workspace when the application closes and saves it. So a session that imported workspaces and then
    /// exits would write its own tabs straight over them — <b>the import would silently undo itself</b>, which is
    /// exactly the outcome rule #11 forbids. §7.5's rule that a workspace setting must gate <i>restore</i> and
    /// never <i>capture</i> is about a persistent preference; this is a one-shot, session-scoped suppression
    /// following an explicit instruction from the user to replace their stored workspace, and honouring that
    /// instruction is the whole point.</para>
    ///
    /// <para>The alternative — restoring the imported workspace into the live session — was rejected: it would
    /// rebuild tabs under an open connection and an open transaction, discarding unsaved editor work.</para>
    /// </summary>
    public bool ImportedWorkspaces { get; private set; }

    // ---- Export ------------------------------------------------------------------

    /// <summary>Writes an export of the CURRENT settings to <paramref name="path"/>.</summary>
    /// <remarks>
    /// <para>Reads the settings straight off the store, because for every section but one the file <i>is</i> the
    /// current state — and <c>SettingsExporter</c> deep-copies before it strips, so nothing in the running app is
    /// touched (ratified §15.9/4).</para>
    /// <para>⚠ The one exception is <c>Workspace</c>, which the app persists only at close. When it is being
    /// exported and a live capture is available, the loaded copy's workspace is replaced by the live one — see
    /// <see cref="CaptureLiveWorkspace"/>. The replacement happens on the <b>loaded copy</b>, so this method still
    /// reads and never writes.</para>
    /// </remarks>
    public void ExportTo(string path, SettingsExportOptions options, string passphrase)
    {
        var settings = _store.Load() ?? new ApplicationSettings();

        if (options.Workspaces && CaptureLiveWorkspace is { } capture)
        {
            var live = capture();
            if (live is not null)
            {
                settings.Workspace = live;
            }
        }

        SettingsExporter.ExportTo(path, settings, options, passphrase, _appVersion);
    }

    // ---- Import ------------------------------------------------------------------

    /// <summary>Phase one — identity, versions, encryption capability. <b>No passphrase.</b></summary>
    public SettingsImportInspection Inspect(string path) => SettingsImportReader.Inspect(path);

    /// <summary>Phase two — decrypt and migrate. Takes the inspection, so the ordering cannot be inverted
    /// (§15.3b, ratified).</summary>
    public SettingsImportResult Open(SettingsImportInspection inspection, string passphrase)
        => SettingsImportReader.Open(inspection, passphrase);

    /// <summary>
    /// Merges the selected sections into <c>settings.dat</c> and then brings the running app back into step with
    /// the file.
    /// </summary>
    public SettingsImportApplyResult Apply(SettingsExportContent content, SettingsImportSelection selection)
    {
        var result = SettingsImportApplier.Apply(_store, content, selection);
        if (!result.Applied)
        {
            return result;
        }

        if (selection.IntersectWith(content).Workspaces)
        {
            ImportedWorkspaces = true;
        }

        // Order matters only in that both must happen: the preferences service raises Changed (which repaints an
        // imported theme through the app's one apply point), and AfterImport rebuilds what MainWindowViewModel
        // holds. Neither is optional, and putting them in one method is what stops a future section from being
        // imported into a file nobody re-reads.
        _preferences.Reload();
        AfterImport?.Invoke();

        return result;
    }
}
