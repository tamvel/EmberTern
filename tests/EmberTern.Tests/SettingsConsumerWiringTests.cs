using System;
using System.IO;
using System.Linq;
using EmberTern.App.Behaviors;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Settings;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

/// <summary>
/// Settings Center etap 6 — <b>the wiring, which is the part that actually breaks</b>.
///
/// <para>The same lesson <c>FormatterStylePreferenceTests</c> recorded one etap earlier: a stored value and a
/// mapping are each two lines, and what really fails is a consumer left on the shipped constant — a setting that
/// works on one surface, silently does nothing on another, and builds green. Every test here changes a
/// preference through the app's ONE <c>PreferencesService</c> and asserts the consumer followed.</para>
///
/// <para>⚠ The Source/Easy default (§7.6) is covered where it lives instead — <c>ProcedureDetailTests</c>,
/// <c>WorkspaceUiStatePersistenceTests</c> and <c>FunctionRoutingTests</c> already owned those assertions and
/// were rewritten in place, which keeps the "what used to be true" contrast visible.</para>
/// </summary>
public class SettingsConsumerWiringTests
{
    // ─── §7.2 — EXECUTION ROW LIMITS ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The ONE place an <see cref="ExecutionRequest"/> is given the user's limits, so a fifth execution
    /// surface inherits them instead of quietly shipping on the defaults.
    /// </summary>
    [Fact]
    public void AnExecutionRequest_CarriesTheUsersRowLimits()
    {
        InHarness(main =>
        {
            // Default state: exactly the shipped constants — a user who never opens the page sees no change.
            var shipped = main.Request("select 1 from rdb$database", ExecutionIntent.Preview, null);
            Assert.Equal(ExecutionDefaults.PreviewLimit, shipped.PreviewLimit);
            Assert.Equal(ExecutionDefaults.FullSoftThreshold, shipped.SoftThreshold);

            main.Preferences.Apply(main.Preferences.Current with
            {
                PreviewRowLimit = 250,
                FullLoadPromptThreshold = 40_000,
            });

            var chosen = main.Request("select 1 from rdb$database", ExecutionIntent.Full, null);
            Assert.Equal(250, chosen.PreviewLimit);
            Assert.Equal(40_000L, chosen.SoftThreshold);

            // ⚠ And the one that must NOT move: FullSafetyCeiling is a memory backstop, not a preference
            // (ratified Q9). A user who could raise it would get an out-of-memory crash instead of a
            // truncated grid.
            Assert.Equal(ExecutionDefaults.FullSafetyCeiling, chosen.FullSafetyCeiling);
        });
    }

    /// <summary>Read live, never captured — apply-on-change means the value moves while the window is open, and a
    /// captured limit would leave every execution after the change on the previous number.</summary>
    [Fact]
    public void TheLimitsAreReadPerRequest_NotCapturedAtStartup()
    {
        InHarness(main =>
        {
            _ = main.Request("select 1 from rdb$database", ExecutionIntent.Preview, null);

            main.Preferences.Apply(main.Preferences.Current with { PreviewRowLimit = 7 });

            Assert.Equal(7, main.Request("select 1 from rdb$database", ExecutionIntent.Preview, null).PreviewLimit);
        });
    }

    // ─── §7.7 — TABLE / VIEW DATA PAGE SIZE ──────────────────────────────────────────────────

    /// <summary>
    /// Both server-paged data grids open at the user's page size. ⚠ Both, because they are two independent view
    /// models that each used to carry their own <c>200</c> — which is exactly the pair that drifts.
    /// </summary>
    [Fact]
    public void BothDataGrids_OpenAtTheStatedPageSize()
    {
        InHarness(main =>
        {
            Assert.Equal(
                PreferenceOptions.DataPageSize.Default,
                main.CreateTableDetail(new MetadataObject("T", MetadataObjectKind.Table)).PageSize);

            main.Preferences.Apply(main.Preferences.Current with { DataPageSize = 500 });

            Assert.Equal(500, main.CreateTableDetail(new MetadataObject("T", MetadataObjectKind.Table)).PageSize);
            Assert.Equal(500, main.CreateViewDetail(new MetadataObject("V", MetadataObjectKind.View)).PageSize);
        });
    }

    /// <summary>The two view models' page-size constants come from the ONE Core declaration now, so they cannot
    /// disagree with each other or with what the store enforces.</summary>
    [Fact]
    public void ThePageSizeConstants_ComeFromTheOneCoreDeclaration()
    {
        Assert.Equal(PreferenceOptions.DataPageSize.Default, TableDetailTabViewModel.DataPreviewRowLimit);
        Assert.Equal(PreferenceOptions.DataPageSize.Default, ViewDetailTabViewModel.DataPreviewRowLimit);
        Assert.Equal(PreferenceOptions.DataPageSize.Maximum, TableDetailTabViewModel.MaxPageSize);
        Assert.Equal(PreferenceOptions.DataPageSize.Maximum, ViewDetailTabViewModel.MaxPageSize);
    }

    // ─── §7.4 — GRID AUTO-FIT DEFAULT ────────────────────────────────────────────────────────

    /// <summary>
    /// A grid with no stored layout follows the setting; unset (headless, design time) keeps the <c>true</c> that
    /// was hard-coded here before the setting existed.
    /// </summary>
    [Fact]
    public void AnUnadjustedGrid_FollowsTheAutoFitDefault()
    {
        var previous = GridLayoutBehavior.DefaultAutoFitColumns;
        try
        {
            GridLayoutBehavior.DefaultAutoFitColumns = null;
            Assert.True(GridLayoutBehavior.FallbackProfile("QueryResults").AutoFitColumns);

            GridLayoutBehavior.DefaultAutoFitColumns = () => false;
            var profile = GridLayoutBehavior.FallbackProfile("QueryResults");
            Assert.False(profile.AutoFitColumns);
            Assert.Equal("QueryResults", profile.GridId);

            GridLayoutBehavior.DefaultAutoFitColumns = () => true;
            Assert.True(GridLayoutBehavior.FallbackProfile("QueryResults").AutoFitColumns);
        }
        finally
        {
            GridLayoutBehavior.DefaultAutoFitColumns = previous;
        }
    }

    // ─── §7.3 — DEBUGGER DEFAULT ISOLATION ───────────────────────────────────────────────────

    /// <summary>The boundary mapping, and its fallback. The third member of the
    /// <c>ThemePreference</c> / <c>FormatterStylePreference</c> family.</summary>
    [Theory]
    [InlineData(PreferenceOptions.DebuggerIsolationSnapshot, DebugIsolation.Snapshot)]
    [InlineData("snapshot", DebugIsolation.Snapshot)]
    [InlineData(PreferenceOptions.DebuggerIsolationReadCommitted, DebugIsolation.ReadCommitted)]
    [InlineData(null, DebugIsolation.ReadCommitted)]
    [InlineData("", DebugIsolation.ReadCommitted)]
    [InlineData("Serializable", DebugIsolation.ReadCommitted)]
    public void AStoredIsolationKey_MapsToTheFirebirdLayersOwnIsolation(string? key, DebugIsolation expected)
        => Assert.Equal(expected, DebuggerIsolationPreference.IsolationFor(key));

    [Fact]
    public void FreshPreferences_YieldReadCommitted()
        => Assert.Equal(DebugIsolation.ReadCommitted, DebuggerIsolationPreference.From(new Preferences()));

    // ─── §7.5 — RESTORE OPEN TABS ON STARTUP ─────────────────────────────────────────────────

    /// <summary>The shipped default reproduces today's behaviour: the tabs come back.</summary>
    [Fact]
    public void WithRestoreOn_TheStoredTabsComeBack()
    {
        InHarness(main =>
        {
            Assert.True(main.Preferences.Current.RestoreWorkspaceOnStartup);
            main.RestoreWorkspace(StoredWorkspace());
            main.ApplyActiveConnectionChange("A");

            Assert.Equal(2, main.WorkspaceTabs.Count);   // Query + the stored table tab
            Assert.Contains(main.WorkspaceTabs, t => t.ObjectName == "ORDERS");
        });
    }

    /// <summary>With it off, the tab strip starts clean — one Query tab, nothing restored.</summary>
    [Fact]
    public void WithRestoreOff_TheTabStripStartsClean()
    {
        InHarness(main =>
        {
            main.Preferences.Apply(main.Preferences.Current with { RestoreWorkspaceOnStartup = false });
            main.RestoreWorkspace(StoredWorkspace());
            main.ApplyActiveConnectionChange("A");

            Assert.Single(main.WorkspaceTabs);
            Assert.Equal(VmTabKind.Query, main.WorkspaceTabs[0].Kind);
        });
    }

    /// <summary>
    /// ⭐⭐ <b>The assertion this setting could most easily have got wrong: saved queries are NOT tabs.</b>
    ///
    /// <para>A connection's saved queries live inside the very same stored <c>ConnectionWorkspace</c> as its tab
    /// list, so the obvious implementation — "do not restore the workspace" — would have discarded named SQL the
    /// user deliberately kept, and then overwritten it at the next close. That is rule-#11 data loss wearing a
    /// preference's clothes. "Start me clean" is about a stale tab strip.</para>
    /// </summary>
    [Fact]
    public void WithRestoreOff_SavedQueriesStillComeBack()
    {
        InHarness(main =>
        {
            main.Preferences.Apply(main.Preferences.Current with { RestoreWorkspaceOnStartup = false });
            main.RestoreWorkspace(StoredWorkspace());
            main.ApplyActiveConnectionChange("A");

            Assert.Equal(new[] { "Nightly report", "Scratch" }, main.SavedQueries.Select(q => q.Name).ToArray());
            Assert.Equal("select * from orders", main.QueryText);
        });
    }

    /// <summary>
    /// ⭐⭐ <b>And the second one: OTHER profiles' stored workspaces survive the session.</b>
    ///
    /// <para>§7.5's rule is "gate restore, never capture" — and capture reads the whole per-connection
    /// dictionary. Had the suppression been implemented by not LOADING that dictionary, one session with the
    /// setting off would have erased every other connection's tabs and saved queries at the next close, silently.
    /// So the dictionary is loaded either way and only its materialisation is suppressed.</para>
    /// </summary>
    [Fact]
    public void WithRestoreOff_OtherProfilesWorkspacesAreNotErasedAtCapture()
    {
        InHarness(main =>
        {
            main.Preferences.Apply(main.Preferences.Current with { RestoreWorkspaceOnStartup = false });
            main.RestoreWorkspace(StoredWorkspace(alsoProfile: "B"));
            main.ApplyActiveConnectionChange("A");

            var captured = main.CaptureWorkspace();

            Assert.True(captured.Workspaces.ContainsKey("B"));
            Assert.Equal("Nightly report", captured.Workspaces["B"].SavedQueries[0].Name);
            Assert.Contains(captured.Workspaces["B"].Tabs, t => t.ObjectName == "ORDERS");
        });
    }

    /// <summary>
    /// The setting is about STARTUP. Reconnecting to a profile later in the same session restores the tabs
    /// <i>this</i> session built — otherwise switching connections would keep throwing the user's work away, all
    /// day, for a setting that says "on startup".
    /// </summary>
    [Fact]
    public void WithRestoreOff_AReconnectLaterInTheSameSessionKeepsThisSessionsTabs()
    {
        InHarness(main =>
        {
            main.Preferences.Apply(main.Preferences.Current with { RestoreWorkspaceOnStartup = false });
            main.RestoreWorkspace(StoredWorkspace());
            main.ApplyActiveConnectionChange("A");
            Assert.Single(main.WorkspaceTabs);

            // Open something in this session, switch away, and come back.
            var obj = new MetadataObject("CUSTOMERS", MetadataObjectKind.Table);
            main.WorkspaceTabs.Add(
                WorkspaceTabViewModel.CreateTableDetail(main, obj, main.CreateTableDetail(obj), "A"));

            main.ApplyActiveConnectionChange("B");
            main.ApplyActiveConnectionChange("A");

            Assert.Contains(main.WorkspaceTabs, t => t.ObjectName == "CUSTOMERS");
        });
    }

    // ─── Fixtures ────────────────────────────────────────────────────────────────────────────

    private static WorkspaceState StoredWorkspace(string? alsoProfile = null)
    {
        var state = new WorkspaceState();
        state.Workspaces["A"] = StoredConnectionWorkspace();
        if (alsoProfile is not null) state.Workspaces[alsoProfile] = StoredConnectionWorkspace();
        return state;
    }

    private static ConnectionWorkspace StoredConnectionWorkspace() => new()
    {
        ActiveTabIndex = 0,
        ActiveSavedQueryId = "q1",
        Tabs =
        {
            new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = "select 1 from rdb$database" },
            new WorkspaceTab
            {
                Kind = CoreTabKind.TableDetail,
                ObjectName = "ORDERS",
                ObjectKind = MetadataObjectKind.Table,
                ConnectionProfileId = "A",
            },
        },
        SavedQueries =
        {
            new SavedQuery { Id = "q1", Name = "Nightly report", SqlText = "select * from orders" },
            new SavedQuery { Id = "q2", Name = "Scratch", SqlText = "select 2" },
        },
    };

    private static void InHarness(Action<MainWindowViewModel> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-etap6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var service = new FirebirdConnectionService();
        try
        {
            body(new MainWindowViewModel(new ConnectionProfileStore(dir), service));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
