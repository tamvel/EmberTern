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
        Assert.Empty(CommandCatalog.Match(Key.F1, KeyModifiers.None));
        Assert.Empty(CommandCatalog.Match(Key.Escape, KeyModifiers.None)); // deliberately never declared
    }

    // ── The ratified shortcut map ───────────────────────────────────────────────────────────────────

    // Exactly the gestures the user ratified, on exactly the commands they ratified them for. Written as a
    // table so the map is readable in one place and a silent re-binding fails here rather than in someone's
    // muscle memory.
    [Theory]
    [InlineData(CommandId.NewObject, Key.F3, KeyModifiers.None)]
    [InlineData(CommandId.CollectionAdd, Key.F3, KeyModifiers.None)]
    [InlineData(CommandId.RefreshMetadata, Key.F4, KeyModifiers.None)]
    [InlineData(CommandId.Go, Key.F5, KeyModifiers.None)]
    [InlineData(CommandId.Commit, Key.F6, KeyModifiers.None)]
    [InlineData(CommandId.Rollback, Key.F6, KeyModifiers.Shift)]
    [InlineData(CommandId.Compile, Key.F7, KeyModifiers.None)]
    [InlineData(CommandId.DeleteObject, Key.F8, KeyModifiers.None)]
    [InlineData(CommandId.CollectionRemove, Key.F8, KeyModifiers.None)]
    [InlineData(CommandId.FormatSql, Key.K, KeyModifiers.Control)]
    [InlineData(CommandId.CloseTab, Key.W, KeyModifiers.Control)]
    public void RatifiedGesture_IsTheDeclaredOne(CommandId id, Key key, KeyModifiers modifiers)
        => Assert.Equal(new KeyGesture(key, modifiers), CommandCatalog.For(id)?.Gesture);

    // Alt+letter is retired with no exceptions — Format SQL was the app's only one. Alt+F12 (Peek) is
    // Alt + a FUNCTION key, outside the rule and deliberately kept.
    [Fact]
    public void NoCommandUsesAltPlusALetter()
    {
        var offenders = CommandCatalog.All
            .SelectMany(d => new[] { d.Gesture, d.AlternateGesture })
            .Where(g => g is not null)
            .Where(g => g!.KeyModifiers.HasFlag(KeyModifiers.Alt) && g.Key is >= Key.A and <= Key.Z)
            .Select(g => g!.ToString())
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Alt+letter is unusable on the Polish keyboard (AltGr) and is retired: "
            + string.Join(", ", offenders));
    }

    // F3 and F8 are each claimed by two commands — a tree one and a grid one. Legal because the focus is in
    // at most one of those surfaces, and the point of having separate scopes rather than one global gesture.
    [Theory]
    [InlineData(Key.F3)]
    [InlineData(Key.F8)]
    public void TreeAndGridShareTheGesture_ButNotTheScope(Key key)
    {
        // Other scopes may also claim the key (F8 is Next Diagnostic in the editor); this is about the two
        // surface claims and their order.
        var surfaces = CommandCatalog.Match(key, KeyModifiers.None)
            .Where(m => m.Scope is CommandScope.Tree or CommandScope.Grid)
            .ToArray();

        Assert.Equal([CommandScope.Tree, CommandScope.Grid], surfaces.Select(m => m.Scope));
    }

    // F8 is the ratified Delete for trees and lists AND stays Next Diagnostic in the editor — the collision
    // the user resolved by scope rather than by moving either one. All three must coexist, ordered so the
    // editor wins while the caret is in code.
    [Fact]
    public void F8_MeansDeleteInTreesAndLists_ButNextDiagnosticInCode()
    {
        var matches = CommandCatalog.Match(Key.F8, KeyModifiers.None);

        Assert.Equal(
            [CommandId.EditorNextDiagnostic, CommandId.DeleteObject, CommandId.CollectionRemove],
            matches.Select(m => m.Id));
        Assert.Equal(CommandScope.Editor, matches[0].Scope);
    }

    // ── Resolution of the new commands ─────────────────────────────────────────────────────────────

    [Fact]
    public void TreeCommands_ResolveAgainstTheSelectedNode()
    {
        using var h = new Harness();
        var tree = h.Main.Metadata;

        // Nothing selected → nothing offered, so the gesture falls through instead of half-working.
        tree.SelectedNode = null;
        Assert.Null(h.Main.ResolveCommand(CommandId.NewObject));
        Assert.Null(h.Main.ResolveCommand(CommandId.DeleteObject));

        // A category group offers New but has nothing to delete.
        var group = MetadataNodeViewModel.CreateGroup(tree, MetadataObjectKind.Procedure);
        tree.SelectedNode = group;
        Assert.Same(group.NewCommand, h.Main.ResolveCommand(CommandId.NewObject));
        Assert.Null(h.Main.ResolveCommand(CommandId.DeleteObject));

        // A leaf is the other way round — and Delete is the node's OWN command, which is what makes F8 open
        // the existing confirmation dialog rather than drop anything by itself.
        var leaf = MetadataNodeViewModel.CreateLeaf(tree, new MetadataObject("T_X", MetadataObjectKind.Table));
        tree.SelectedNode = leaf;
        Assert.Same(leaf.DeleteCommand, h.Main.ResolveCommand(CommandId.DeleteObject));
        Assert.Null(h.Main.ResolveCommand(CommandId.NewObject));

        // Refresh needs a connection; with none selected it declines rather than inventing a target.
        Assert.Null(h.Main.ResolveCommand(CommandId.RefreshMetadata));
    }

    [Fact]
    public void GridAndGlobalCommands_ResolveToTheCommandsTheirButtonsUse()
    {
        using var h = new Harness();

        Assert.Same(h.Main.AddCollectionItemCommand, h.Main.ResolveCommand(CommandId.CollectionAdd));
        Assert.Same(h.Main.RemoveCollectionItemCommand, h.Main.ResolveCommand(CommandId.CollectionRemove));
        Assert.Same(h.Main.CommitAllCommand, h.Main.ResolveCommand(CommandId.Commit));
        Assert.Same(h.Main.RollbackAllCommand, h.Main.ResolveCommand(CommandId.Rollback));
        Assert.Same(h.Main.CloseActiveTabCommand, h.Main.ResolveCommand(CommandId.CloseTab));
    }

    // F7 Compile reaches every editor that compiles, and Ctrl+K reaches every tab with SQL to format —
    // both through the tab's OWN command, so the shortcut is a second trigger and never a second path.
    [Fact]
    public void Compile_AndFormatSql_ResolveOnTheEditorTabs()
    {
        using var h = new Harness();

        var view = ViewTab(h, "V_X");
        Assert.NotNull(view.ResolveCommand(CommandId.Compile));
        Assert.NotNull(view.ResolveCommand(CommandId.FormatSql));

        // Declared reach and actual resolution must agree: every kind the descriptor names has to answer.
        var compilable = CommandCatalog.For(CommandId.Compile)!.TabKinds!;
        var formattable = CommandCatalog.For(CommandId.FormatSql)!.TabKinds!;
        Assert.Contains(WorkspaceTabKind.ViewDetail, compilable);
        Assert.Contains(WorkspaceTabKind.ViewDetail, formattable);
        Assert.Equal(11, compilable.Count);
        Assert.Equal(6, formattable.Count);

        // A console tab formats but does not compile; a read-only Ddl snapshot does neither.
        var query = WorkspaceTabViewModel.CreateQuery(h.Main);
        Assert.Same(h.Main.FormatSqlCommand, query.ResolveCommand(CommandId.FormatSql));
        Assert.Null(query.ResolveCommand(CommandId.Compile));

        var ddl = WorkspaceTabViewModel.CreateDdl(
            h.Main, new MetadataObject("V_Y", MetadataObjectKind.View), "select 1 from rdb$database", null);
        Assert.Null(ddl.ResolveCommand(CommandId.Compile));
        Assert.Null(ddl.ResolveCommand(CommandId.FormatSql));
    }

    // ── UX Consistency Pass: the toolbar and the context menu describe one surface the same way ─────

    // The defect: the toolbar said "Add item" / "Remove item" while the fields context menu beside it said
    // "New field" / "Edit field" / "Delete field" — the same commands, two vocabularies, one surface. The
    // toolbar tooltip now names the ACTIVE collection's own noun and takes its gesture from the catalog, so
    // the two cannot disagree.
    [Fact]
    public void CollectionTooltips_NameTheActiveCollectionAndCarryItsGesture()
    {
        using var h = new Harness();

        // With no editable collection active the strip is hidden; the tooltip still has to be a sentence
        // rather than "New " with a hole in it.
        Assert.Equal("New item · F3", h.Main.CollectionAddTooltip);
        Assert.Equal("Edit item · F2", h.Main.CollectionEditTooltip);
        Assert.Equal("Delete item · F8", h.Main.CollectionRemoveTooltip);

        // Every gesture shown comes from the catalog, so re-binding one moves the toolbar and the menu together.
        Assert.EndsWith(CommandTip.Gesture(CommandId.CollectionAdd), h.Main.CollectionAddTooltip, StringComparison.Ordinal);
        Assert.EndsWith(CommandTip.Gesture(CommandId.CollectionEdit), h.Main.CollectionEditTooltip, StringComparison.Ordinal);
        Assert.EndsWith(CommandTip.Gesture(CommandId.CollectionRemove), h.Main.CollectionRemoveTooltip, StringComparison.Ordinal);
    }

    // Edit was reachable from the fields context menu and from nowhere on the toolbar — while a tooltip string
    // for the missing button ("Edit selected field · F2") sat unused in UiStrings, which is what showed the
    // button had been intended and dropped. It is a real routed command now.
    [Fact]
    public void CollectionEdit_IsARoutedGridCommand_AndResolvesToTheRouter()
    {
        var descriptor = CommandCatalog.For(CommandId.CollectionEdit);
        Assert.NotNull(descriptor);
        Assert.Equal(CommandScope.Grid, descriptor!.Scope);
        Assert.Equal(CommandDispatch.Routed, descriptor.Dispatch);
        Assert.Equal(new KeyGesture(Key.F2), descriptor.Gesture);

        using var h = new Harness();
        Assert.Same(h.Main.EditCollectionItemCommand, h.Main.ResolveCommand(CommandId.CollectionEdit));

        // Nothing to edit with no collection active, so the gesture falls through rather than looking broken.
        Assert.False(h.Main.EditCollectionItemCommand.CanExecute(null));
        Assert.False(h.Main.ShowCollectionEdit);
    }

    // The fields grid's long-standing Insert / Delete keep working as ALTERNATES while the ratified F3 / F8 are
    // what every surface displays — the point being that the aliases live in the catalog too, not in a local
    // DataGrid.KeyBinding that no menu could read.
    [Fact]
    public void FieldsGridLegacyKeys_AreCatalogAlternates_NotLocalBindings()
    {
        Assert.Equal(new KeyGesture(Key.Insert), CommandCatalog.For(CommandId.CollectionAdd)!.AlternateGesture);
        Assert.Equal(new KeyGesture(Key.Delete), CommandCatalog.For(CommandId.CollectionRemove)!.AlternateGesture);

        // Both spellings resolve to the same command, at Grid scope only.
        foreach (var (key, id) in new[]
                 {
                     (Key.F3, CommandId.CollectionAdd), (Key.Insert, CommandId.CollectionAdd),
                     (Key.F8, CommandId.CollectionRemove), (Key.Delete, CommandId.CollectionRemove),
                 })
        {
            var matches = CommandCatalog.Match(key, KeyModifiers.None)
                .Where(m => m.Scope == CommandScope.Grid)
                .ToArray();
            Assert.Contains(id, matches.Select(m => m.Id));
        }

        // ⚠ Delete is claimed by the EDITOR too (it deletes a character). Editor outranks Grid, so the caret
        // decides — which is the whole reason this had to be a scoped claim rather than a global gesture.
        var deleteClaims = CommandCatalog.Match(Key.Delete, KeyModifiers.None);
        Assert.Equal(CommandScope.Grid, deleteClaims.Single().Scope);
    }

    private static WorkspaceTabViewModel ViewTab(Harness h, string name)
    {
        var detail = new ViewDetailTabViewModel(name);
        return WorkspaceTabViewModel.CreateViewDetail(
            h.Main, new MetadataObject(name, MetadataObjectKind.View), detail, null);
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
