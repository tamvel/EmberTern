using System;
using System.IO;
using System.Linq;
using Avalonia.Input;
using EmberTern.App.Commands;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Keyboard Manager's registry contract. Pure — no Avalonia app, no headless session: the catalog is a
/// literal table and the resolvers are view-model methods, which is what makes the whole gesture map
/// assertable without a running UI.
/// </summary>
public sealed class CommandCatalogTests
{
    // ── The catalog itself ──────────────────────────────────────────────────────────────────────────

    // The collision validator the user asked for. It has to be empty here, and the failure message has to
    // NAME the clash — a bare count tells whoever broke it nothing.
    [Fact]
    public void Catalog_HasNoAmbiguousGesture()
    {
        var collisions = CommandCatalog.Collisions();
        Assert.True(collisions.Count == 0, "Ambiguous gestures:" + Environment.NewLine
                                           + string.Join(Environment.NewLine, collisions));
    }

    [Fact]
    public void EveryDescriptor_IsDeclaredOnce_AndCarriesAGesture()
    {
        var ids = CommandCatalog.All.Select(d => d.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());

        // Etap 2 declares only gesture-bearing commands; a menu-only command (no gesture) becomes valid
        // when the menus arrive in etap 5, and this assertion is the reminder to revisit it deliberately.
        Assert.All(CommandCatalog.All, d => Assert.NotNull(d.Gesture));
    }

    // Tab-scoped commands must say which tab kinds they exist on: that list IS the liveness test the router
    // runs, so a missing one would silently make the gesture fire on every tab.
    [Fact]
    public void EveryTabScopedDescriptor_DeclaresItsTabKinds()
    {
        foreach (var d in CommandCatalog.All.Where(d => d.Scope == CommandScope.Tab))
        {
            Assert.NotNull(d.TabKinds);
            Assert.NotEmpty(d.TabKinds!);
        }

        // And nothing else does — TabKinds on a Global or Editor command would be quietly ignored.
        Assert.All(CommandCatalog.All.Where(d => d.Scope != CommandScope.Tab), d => Assert.Null(d.TabKinds));
    }

    // ── Resolution order ────────────────────────────────────────────────────────────────────────────

    // The heart of the design: a more specific scope answers first. Ctrl+F is claimed twice on purpose —
    // the editor's Find bar and the Object Explorer's filter — and the EDITOR must win when the caret is in
    // one. This replaced a hand-written focus probe in the window's key handler.
    [Fact]
    public void CtrlF_OffersTheEditorBeforeTheSidebar()
    {
        var matches = CommandCatalog.Match(Key.F, KeyModifiers.Control);

        Assert.Equal([CommandId.EditorFind, CommandId.FocusSidebarFilter], matches.Select(m => m.Id));
        Assert.True((int)matches[0].Scope > (int)matches[1].Scope, "Editor must outrank Global");
    }

    // Shift+F5 is Execute-Full on a query tab and Stop in the debugger. That is legal precisely because no
    // single tab kind offers both — which is what Collisions() checks and what TabKinds makes checkable.
    [Fact]
    public void ShiftF5_IsSharedByDisjointTabKinds()
    {
        var matches = CommandCatalog.Match(Key.F5, KeyModifiers.Shift);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal(CommandScope.Tab, m.Scope));
        Assert.Empty(matches[0].TabKinds!.Intersect(matches[1].TabKinds!));
    }

    [Fact]
    public void AnUndeclaredGesture_MatchesNothing()
    {
        Assert.Empty(CommandCatalog.Match(Key.F7, KeyModifiers.None));   // arrives in etap 3 as Compile
        Assert.Empty(CommandCatalog.Match(Key.Escape, KeyModifiers.None)); // deliberately never declared
    }

    // ── ⭐ C1: F5 can no longer leak into a tab that has nothing to execute ─────────────────────────

    // The audit's confirmed defect: F5 was a window binding whose command ended with "anything else →
    // Execute Query", so pressing it on a Table editor or the Security Manager ran whatever was in the SQL
    // editor — inside the user's working transaction.
    //
    // This assertion is EXHAUSTIVE where it matters, and that is why it is written against the catalog
    // rather than against a pile of constructed tabs: the router only calls ResolveCommand for a kind the
    // descriptor declares, so every kind absent from this list is structurally unable to see F5.
    [Fact]
    public void Go_IsDeclaredOnlyForTabsThatHaveAMainAction()
    {
        var go = CommandCatalog.For(CommandId.Go);
        Assert.NotNull(go);
        Assert.Equal(new KeyGesture(Key.F5), go!.Gesture);

        Assert.Equal(
            [
                WorkspaceTabKind.Query,
                WorkspaceTabKind.Debugger,
                WorkspaceTabKind.ScriptExecutor,
                WorkspaceTabKind.DataImport,
            ],
            go.TabKinds);

        // Everything else — 16 of the 20 tab kinds, including every object editor — is out of F5's reach.
        var covered = go.TabKinds!.ToHashSet();
        var uncovered = Enum.GetValues<WorkspaceTabKind>().Where(k => !covered.Contains(k)).ToArray();
        Assert.Contains(WorkspaceTabKind.TableDetail, uncovered);
        Assert.Contains(WorkspaceTabKind.SecurityManager, uncovered);
        Assert.Contains(WorkspaceTabKind.ProcedureDetail, uncovered);
    }

    // …and the per-kind switch agrees with that declaration, so the two halves cannot drift.
    [Fact]
    public void ResolveCommand_MapsGoOnAQueryTab_AndNowhereOnADdlTab()
    {
        using var h = new Harness();

        var query = WorkspaceTabViewModel.CreateQuery(h.Main);
        Assert.Same(h.Main.ExecuteQueryCommand, query.ResolveCommand(CommandId.Go));
        Assert.Same(h.Main.ExecuteQueryCommand, query.ResolveCommand(CommandId.ExecuteQuery));
        Assert.Same(h.Main.ExecuteQueryFullCommand, query.ResolveCommand(CommandId.ExecuteQueryFull));
        Assert.Same(h.Main.FormatSqlCommand, query.ResolveCommand(CommandId.FormatSql));

        var ddl = WorkspaceTabViewModel.CreateDdl(
            h.Main, new MetadataObject("V_X", MetadataObjectKind.View), "select 1 from rdb$database", null);
        Assert.Null(ddl.ResolveCommand(CommandId.Go));
        Assert.Null(ddl.ResolveCommand(CommandId.ExecuteQuery));
        Assert.Null(ddl.ResolveCommand(CommandId.ExecuteQueryFull));
        Assert.Null(ddl.ResolveCommand(CommandId.FormatSql));
    }

    // A Global id must not be answerable by a tab, and a Tab id must not be answerable by the window —
    // otherwise the scope on the descriptor would be decoration.
    [Fact]
    public void ScopesDoNotAnswerForEachOther()
    {
        using var h = new Harness();
        var query = WorkspaceTabViewModel.CreateQuery(h.Main);

        Assert.Null(query.ResolveCommand(CommandId.GlobalSearch));
        Assert.Same(h.Main.OpenGlobalSearchCommand, h.Main.ResolveCommand(CommandId.GlobalSearch));
        Assert.Null(h.Main.ResolveCommand(CommandId.Go));

        // View actions have no view-model command anywhere; the router owns them.
        Assert.Null(h.Main.ResolveCommand(CommandId.FocusSidebarFilter));
        Assert.Null(h.Main.ResolveCommand(CommandId.EditorFind));
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dir;

        public Harness()
        {
            _dir = Path.Combine(Path.GetTempPath(), "et-cmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(new ConnectionProfileStore(_dir), Service);
        }

        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(_dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
