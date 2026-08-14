using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using EmberTern.App;
using EmberTern.App.Commands;
using EmberTern.App.Completion;
using EmberTern.App.Controls;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;
using EmberTern.Firebird;
using System.Text.RegularExpressions;
using Avalonia.Controls.Presenters;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EmberTern.Tests;

// Headless Avalonia probe — proves whether ConnectionNodeViewModel.IsExpanded
// actually propagates to the real TreeViewItem.IsExpanded through the compiled
// Style binding in MainWindow.axaml. NOT a behavioural assertion to keep green
// forever — it's an instrument. It builds the REAL MainWindow (real compiled
// bindings, real styles) so the binding under test is the production one.
/// <summary>
/// Owns the ONE headless session for the whole test process, and — unlike the <c>static readonly</c> field it
/// replaces — actually <b>disposes</b> it.
/// <para>
/// ⭐ Why the ownership matters, beyond tidiness. Avalonia's own contract for this type is: <i>"Disposing unit
/// test session stops internal dispatcher loop."</i> A session that is never disposed therefore leaves a
/// dispatcher loop spinning on its own thread for the rest of the process — after every test has finished.
/// As an <c>IClassFixture</c>, xunit creates it before the class's first test and disposes it after the last,
/// so the loop's lifetime is bounded by the tests that need it.
/// </para>
/// <para>
/// It stays ONE session (gotcha #94/#226), which is the load-bearing part: a session owns a UI thread, and
/// AvaloniaEdit builds its caret/editing <c>KeyBinding</c>s as STATIC lists created on whichever thread first
/// constructs a <c>TextEditor</c>. With a session per test, every later test's <c>TextArea</c> shares those
/// instances across threads, so any real KeyDown into an editor dies with "The calling thread cannot access
/// this object because a different thread owns it" — regardless of how the key is injected.
/// </para>
/// <para>
/// ⚠ <b>It is a COLLECTION fixture, not a class fixture, and that distinction is the whole guarantee.</b>
/// xunit creates an <c>IClassFixture</c> once <i>per test class</i>, so the moment a second class wanted a
/// headless session the process had TWO — which is precisely the state "ONE session" forbids. It was not
/// theoretical: adding <c>ContextMenuPresentationTests</c> as an <c>IClassFixture</c> consumer made the
/// full-suite run hang inside it, and <c>--blame-hang</c> named it. An <c>ICollectionFixture</c> is shared
/// across every class in the collection and also serialises them, which is what a single UI thread needs.
/// <b>Any new test that needs a headless session joins <see cref="HeadlessCollection"/> — never adds its own
/// <c>IClassFixture&lt;HeadlessSessionFixture&gt;</c>.</b>
/// </para>
/// </summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));

    // ⛔⛔ DO NOT ADD A CONSTRUCTOR THAT "WARMS THE SESSION UP". It was tried, measured, and reverted.
    //
    // HeadlessUnitTestSession builds its isolated Avalonia application LAZILY, inside the first Dispatch:
    // EnsureIsolatedApplication → AvaloniaHeadlessPlatform.Initialize → Compositor → DefaultRenderLoop.Add →
    // Dispatcher.VerifyAccess(). That last call intermittently throws "the calling thread cannot access this
    // object". It is a real, still-open infrastructure defect (#94 / #226 / #286) — whichever headless test
    // dispatches FIRST is the one that dies, which is why the failing test name changes every run.
    //
    // ⚠⚠ Adding `Session.Dispatch(() => { })` here looks like the obvious fix — one known moment, before any
    // test. It does not touch the race; it only moves it into FIXTURE CONSTRUCTION, and a collection fixture
    // that throws fails EVERY test in the collection. Measured on the full single-command run: with the
    // warm-up a bad run lost 375 tests, without it the same bad run loses 1, and the failure RATE was
    // indistinguishable (~2 in 5 either way). It bought nothing and multiplied the damage 375×.
    //
    // ⭐ The general lesson: making a flaky lazy initialisation EAGER does not make it reliable, it makes it
    // load-bearing earlier. The real fix needs Avalonia's headless dispatcher/scope question answered — its own
    // task, recorded in docs/current-state.md.
    public void Dispose() => Session.Dispose();

    private static class HeadlessAppEntry
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<global::EmberTern.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

/// <summary>
/// ⚠⚠ <b>Empties the process-global <c>Loc.LanguageChanged</c> subscriber list around every test it covers.</b>
///
/// <para>⭐ <b>This attribute is the automated isolation that replaced running the suite in hand-maintained
/// partitions.</b> A static event's subscriber list is process-wide, so every view model any earlier test built
/// stayed subscribed; the next test to swap the localization catalog then broadcast into all of them. Measured
/// on this repository: <b>45 deterministic failures out of 8 799</b>, identical across two full runs, none of
/// them about the code under test. Splitting the run into three partitions only moved the leaking test away
/// from the observing one — the view models still outlived their tests, so the defect was hidden rather than
/// fixed.</para>
///
/// <para>⚠ It is applied to the COLLECTION DEFINITION, so xunit runs it for every test in
/// <see cref="HeadlessCollection"/> automatically — a new headless test cannot forget it, which is the property
/// a per-class opt-in would not have. The tests in this collection are serialised by xunit (one session, one UI
/// thread), so the single attribute instance is never entered concurrently; healthy tests outside the
/// collection keep running in parallel and are untouched.</para>
///
/// <para>⛔ It is NOT a licence to leak. The one product leak this investigation surfaced —
/// <c>DiagnosticsPanelViewModel</c> subscribing per Package tab — was fixed at the source, by making the panel
/// an ordinary child of the app's single long-lived subscriber. What remains subscribed for the process is
/// <c>MainWindowViewModel</c>, and that is a recorded decision (one window, app lifetime), not an oversight.</para>
/// </summary>
public sealed class IsolatesGlobalLanguageStateAttribute : BeforeAfterTestAttribute
{
    private IDisposable? _scope;

    public override void Before(MethodInfo methodUnderTest) => _scope = Loc.IsolateSubscribersForVerification();

    public override void After(MethodInfo methodUnderTest)
    {
        _scope?.Dispose();
        _scope = null;
    }
}

/// <summary>
/// The one collection every headless-UI test class belongs to, so they share a single
/// <see cref="HeadlessSessionFixture"/> — one session, one UI thread, for the whole process — and, since the
/// audit follow-up, a clean global localization state per test (see
/// <see cref="IsolatesGlobalLanguageStateAttribute"/>).
/// </summary>
[CollectionDefinition(Name)]
[IsolatesGlobalLanguageState]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSessionFixture>
{
    public const string Name = "headless-avalonia";
}

[Collection(HeadlessCollection.Name)]
public sealed class ConnectionExpandBindingProbe
{
    private readonly HeadlessUnitTestSession SharedSession;
    private readonly ITestOutputHelper _out;

    public ConnectionExpandBindingProbe(HeadlessSessionFixture fixture, ITestOutputHelper output)
    {
        SharedSession = fixture.Session;
        _out = output;
    }

    // Flat sidebar (migration): the real MainWindow hosts the single-VSP ListBox
    // ("SidebarList") bound to Metadata.SidebarRows, and each root connection surfaces as a
    // SidebarRow. (The nested-VSP TreeView + its container IsExpanded binding — gotcha #38 —
    // are gone; expansion projection is covered by SidebarFlatControllerTests.)
    [Fact]
    public async System.Threading.Tasks.Task FlatSidebar_RendersRootRows()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var profile = new ConnectionProfile { Name = "Probe", Host = "h", Port = 3050 };
            store.Upsert(profile);

            var vm = new MainWindowViewModel(store, service);
            vm.ReloadConnections();

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => l.Name == "SidebarList");
            var node = vm.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
                .Single(n => n.Profile.Id == profile.Id);
            var row = vm.Metadata.SidebarRows.FirstOrDefault(r => ReferenceEquals(r.Node, node));

            log.AppendLine($"SidebarList found = {list is not null}");
            log.AppendLine($"ItemsSource is SidebarRows = {ReferenceEquals(list!.ItemsSource, vm.Metadata.SidebarRows)}");
            log.AppendLine($"root connection row exists = {row is not null}, expandable = {row?.IsExpandable}");

            Assert.Same(vm.Metadata.SidebarRows, list.ItemsSource);
            Assert.True(row is not null, "root connection must surface as a SidebarRow.\n" + log);
            // A disconnected connection has no children → no chevron (fix #2).
            Assert.False(row!.IsExpandable, "disconnected connection has no children → no chevron.\n" + log);
            Assert.False(row.IsExpanded, "collapsed until connected/expanded.\n" + log);

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // End-to-end auto-expand-on-connect through the flat projection: flipping IsConnected
    // (which OnIsConnectedChanged reacts to) must set the VM's IsExpanded AND be mirrored on
    // the node's SidebarRow (the controller reacts to the IsExpanded change) — no
    // Dispatcher-post / toggle workarounds in the VM.
    [Fact]
    public async System.Threading.Tasks.Task AutoExpandOnConnect_ReflectedInFlatList()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var profile = new ConnectionProfile { Name = "Probe2", Host = "h", Port = 3050 };
            store.Upsert(profile);

            var vm = new MainWindowViewModel(store, service);
            vm.ReloadConnections();

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var node = vm.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
                .Single(n => n.Profile.Id == profile.Id);
            var row = vm.Metadata.SidebarRows.First(r => ReferenceEquals(r.Node, node));
            Assert.False(row.IsExpanded, "row starts collapsed");

            node.IsConnected = true;
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();

            // ⚠ Re-resolve the row rather than reusing the instance captured above. The connect-time
            // category prefetch now runs under the sidebar's bulk guard (the Layer-1 fix for the
            // quadratic re-projection), and EndUpdate re-projects the whole list — so the row OBJECT is
            // replaced. That is the guard's documented trade-off, not a behaviour change: a manual
            // Refresh already ended in a full re-projection via ApplyFilterAsync. What this probe is
            // about is the MIRRORING, which is a property of the projection, not of a row's identity.
            var expandedRow = vm.Metadata.SidebarRows.First(r => ReferenceEquals(r.Node, node));

            log.AppendLine($"VM IsExpanded = {node.IsExpanded}");
            log.AppendLine($"row.IsExpanded = {expandedRow.IsExpanded}");

            Assert.True(node.IsExpanded, "VM should auto-expand on connect.\n" + log);
            Assert.True(expandedRow.IsExpanded, "the SidebarRow must mirror the node's expansion.\n" + log);

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Proves the FluentTheme SystemAccentColor override is in place: a default Win11
    // install resolves SystemAccentColor to an orange/gold, which leaks into every
    // Fluent control that derives from it (CheckBox checked fill, RadioButton,
    // ComboBox/ListBox selection, ToggleSwitch). We override it to the EmberTern accent
    // blue in both theme dictionaries. This pins that the override resolves through the
    // real app resource scope (where FluentTheme reads it) for both variants — i.e. no
    // amber leakage regression.
    [Fact]
    public async System.Threading.Tasks.Task SystemAccentColor_OverriddenToEmberternBlue()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var expected = Color.Parse("#2D6BBF");
            var window = new Window();
            window.Show();

            // Top-level (theme-invariant) override → resolves identically under both
            // requested variants. Pin both so a regression in either theme is caught.
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                window.RequestedThemeVariant = variant;
                Dispatcher.UIThread.RunJobs();
                var ok = window.TryFindResource("SystemAccentColor", out var val);
                log.AppendLine($"{variant}: found={ok} value={val}");
                Assert.True(ok, $"SystemAccentColor must resolve under {variant}.\n" + log);
                Assert.Equal(expected, Assert.IsType<Color>(val));
            }

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Issue-4 instrument: when a View Detail tab is active and the source has been
    // edited, the toolbar's ⚡ Compile button (bound to ActiveViewDetail.CompileCommand)
    // MUST be enabled. Builds the REAL MainWindow so the binding + AsyncRelayCommand
    // CanExecute under test are the production ones — gotcha #39 (prove the layer).
    [Fact]
    public async System.Threading.Tasks.Task ViewCompileButton_EnabledWhenViewTabActiveAndEdited()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var vm = new MainWindowViewModel(store, service);

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Open a View Detail tab and make it the active tab (the toolbar binds
            // ⚡ Compile to ActiveViewDetail.CompileCommand).
            var obj = new MetadataObject("V_PROBE", MetadataObjectKind.View);
            var detail = vm.CreateViewDetail(obj);
            var tab = WorkspaceTabViewModel.CreateViewDetail(vm, obj, detail, null);
            vm.WorkspaceTabs.Add(tab);
            vm.SelectedWorkspaceTab = tab;
            // Re-finds the ⚡ button bound to the active view's CompileCommand and
            // reports its real effective-enabled state. Re-found each step so a VM
            // instance swap (which would itself be a bug) can't hide behind a stale ref.
            bool HammerEnabled()
            {
                Dispatcher.UIThread.RunJobs();
                var c = vm.ActiveViewDetail?.CompileCommand;
                if (c is null) return false;
                var btn = window.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => ReferenceEquals(b.Command, c));
                return btn is not null && btn.IsEffectivelyEnabled;
            }

            // Walk every editor state the user listed, asserting the hammer stays
            // enabled at each STEADY state (the toolbar binds to the same VM the editor
            // mutates — H1; CanCompile is connection-independent so mode switches /
            // parser failures can't gate it — H2/H5).

            // (1) existing view → source edit → compile
            detail.SourceText = "CREATE OR ALTER VIEW V_PROBE (A, B) AS SELECT 1 A, 2 B FROM RDB$DATABASE";
            Assert.True(HammerEnabled(), "[1] source edit\n" + log);

            // (2) existing view → Easy mode edit → compile
            detail.EasyMode = true;
            Assert.True(HammerEnabled(), "[2] easy mode\n" + log);
            Assert.Equal(2, detail.Columns.Count);              // parsed (A, B)

            // (3) add column → compile
            detail.AddColumnCommand.Execute(null);
            Assert.True(HammerEnabled(), "[3] add column\n" + log);
            Assert.Equal(3, detail.Columns.Count);

            // (4) delete column → compile
            detail.SelectedColumn = detail.Columns[0];
            detail.DeleteColumnCommand.Execute(null);
            Assert.True(HammerEnabled(), "[4] delete column\n" + log);
            Assert.Equal(2, detail.Columns.Count);

            // (5) reorder column → compile (also re-pins issue 2: the collection order
            //     actually changes via RemoveAt+Insert)
            detail.SelectedColumn = detail.Columns[0];
            var movedName = detail.Columns[0].Name;
            detail.MoveColumnDownCommand.Execute(null);
            Assert.True(HammerEnabled(), "[5] reorder column\n" + log);
            Assert.Equal(movedName, detail.Columns[1].Name);   // first row moved down

            // (6) Source → Easy → Source → compile
            detail.EasyMode = false;
            Assert.True(HammerEnabled(), "[6] back to source\n" + log);

            // (bonus) parser failure must NOT gate Compile — last-good model kept,
            //         notice shown, hammer stays enabled.
            detail.SourceText = "this is not a view definition";
            detail.EasyMode = true;
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[parse-fail] ErrorMessage = {detail.ErrorMessage}");
            Assert.False(string.IsNullOrEmpty(detail.ErrorMessage), "[parse-fail] notice expected\n" + log);
            Assert.True(HammerEnabled(), "[parse-fail] hammer must stay enabled\n" + log);

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Type-to-filter on the flat sidebar ListBox (Phase 3 retarget): build the REAL
    // MainWindow, focus a row, and verify (a) typing redirects the char into SidebarFilterBox
    // + moves focus there, (b) Ctrl+F focuses the filter, (c) Escape clears the filter and
    // moves focus off the box (back onto a list row). Production wiring under test (gotcha #39).
    [Fact]
    public async System.Threading.Tasks.Task TypeToFilter_ListTyping_RedirectsToFilterBox()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe-ttf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            store.Upsert(new ConnectionProfile { Name = "ProbeTTF", Host = "h", Port = 3050 });

            var vm = new MainWindowViewModel(store, service);
            vm.ReloadConnections();

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
            var filter = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SidebarFilterBox");
            var rowItem = window.GetVisualDescendants().OfType<ListBoxItem>()
                .First(i => i.DataContext is SidebarRow);

            object? Focused() => TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();

            // (a) Focus a row, type 'k' → goes to the filter, focus moves there.
            rowItem.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("k");
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"after list-typing: FilterText='{vm.Metadata.FilterText}' boxText='{filter.Text}' focusIsBox={ReferenceEquals(Focused(), filter)}");
            Assert.True(vm.Metadata.FilterText == "k", "typing in the list must fill the filter.\n" + log);
            Assert.True(ReferenceEquals(Focused(), filter), "focus must move to the filter box.\n" + log);

            // (b) Ctrl+F focuses the filter.
            rowItem.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"after Ctrl+F: focusIsBox={ReferenceEquals(Focused(), filter)}");
            Assert.True(ReferenceEquals(Focused(), filter), "Ctrl+F must focus the filter box.\n" + log);

            // (c) Escape clears the filter and returns focus off the box.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"after Escape: FilterText='{vm.Metadata.FilterText}' focusIsBox={ReferenceEquals(Focused(), filter)}");
            Assert.Equal(string.Empty, vm.Metadata.FilterText);
            Assert.False(ReferenceEquals(Focused(), filter), "Escape must move focus off the filter box.\n" + log);

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // D12 Seam E1 QA — the breakpoint gutter click. Ground truth for the reported "clicking the gutter does
    // nothing": builds a real TextEditor with the production BreakpointMargin, lays it out, and sends a REAL
    // left-click over the margin — the same hit-test + OnPointerPressed path the app uses — asserting the toggle
    // callback fires. If this passes, the margin click plumbing works; if it fails, the margin is not receiving
    // the click (the bug), and the log shows visualLinesValid + the margin bounds to diagnose.
    [Fact]
    public async System.Threading.Tasks.Task BreakpointMargin_GutterClick_InvokesToggle()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            int? toggled = null;
            var bps = new System.Collections.Generic.HashSet<int>();
            var editor = new TextEditor
            {
                Text = "aaaa\nbbbb\ncccc\ndddd\neeee",
                ShowLineNumbers = true,
                FontFamily = new FontFamily("Consolas,monospace"),
                FontSize = 13,
            };
            var margin = new BreakpointMargin(() => bps, off => toggled = off);
            editor.TextArea.LeftMargins.Insert(0, margin);

            var window = new Window { Width = 500, Height = 400, Content = editor };
            window.Show();
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();

            var tv = editor.TextArea.TextView;
            log.AppendLine($"visualLinesValid={tv.VisualLinesValid} marginBounds={margin.Bounds}");

            // A point over the margin, on the first text line's row.
            var p = margin.TranslatePoint(new Point(9, 6), window);
            log.AppendLine($"clickPoint={p}");
            if (p is { } pt)
            {
                window.MouseDown(pt, MouseButton.Left);
                window.MouseUp(pt, MouseButton.Left);
                for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            }
            log.AppendLine($"toggled={toggled}");

            window.Close();
            Assert.True(toggled is not null, "gutter click must invoke the breakpoint toggle.\n" + log);
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Etap 2: every MetadataObjectKind's geometry key must resolve to a real Geometry
    // through IconGeometryConverter (the live SVG-icon pipeline), plus the tree-chrome
    // keys (Query tab / Connection node / Folder). A missing/typo'd key renders a BLANK
    // icon at runtime — no crash, so the smoke test wouldn't catch it; this would. Also
    // future-proofs: a new enum value without a matching <StreamGeometry> fails here.
    [Fact]
    public async System.Threading.Tasks.Task IconGeometries_AllKindsAndChromeResolve()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var window = new Window();
            window.Show();

            Geometry? Resolve(string key) =>
                IconGeometryConverter.Instance.Convert(key, typeof(Geometry), null, CultureInfo.InvariantCulture) as Geometry;

            foreach (MetadataObjectKind kind in Enum.GetValues<MetadataObjectKind>())
            {
                var key = MetadataNodeViewModel.GeometryKeyFor(kind);
                var geometry = Resolve(key);
                log.AppendLine($"{kind} -> {key} -> {(geometry is null ? "NULL" : "ok")}");
                Assert.True(geometry is not null, $"No geometry resolved for {key} ({kind}).\n" + log);
            }

            foreach (var key in new[] { "Icon.Query", "Icon.Connection", "Icon.Folder" })
            {
                Assert.True(Resolve(key) is not null, $"No geometry resolved for chrome key {key}.\n" + log);
            }

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Bug probe: the SQL Results aggregation add-row. Builds the REAL MainWindow so
    // the ComboBox SelectedItem TwoWay bindings (SelectedMenuColumn / SelectedMenuFunction)
    // are the production ones, then reproduces the user's sequence: a result arrives
    // (SetColumns while the Σ bar is collapsed) → open the bar → invoke "Add aggregate".
    // Proves which layer (VM picker state vs. binding clobber vs. compute) is broken.
    [Fact]
    public async System.Threading.Tasks.Task AggregationAddRow_ProducesComputedChip()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe-agg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var vm = new MainWindowViewModel(store, service);

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // A result arrives → OnCurrentResultChanged → ResultAggregationBar.SetColumns
            // (while the Σ bar is still collapsed, exactly like production).
            var rows = new object?[5][];
            for (int i = 0; i < 5; i++) rows[i] = new object?[] { i };
            vm.CurrentResult = new EmberTern.Core.Query.QueryResult
            {
                Columns = new[] { new EmberTern.Core.Query.QueryColumn("N", typeof(int)) },
                Rows = rows,
            };
            Dispatcher.UIThread.RunJobs();

            var bar = vm.ResultAggregationBar;

            // Open the aggregation bar (the ComboBoxes fully realize now).
            bar.IsBarOpen = true;
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();

            log.AppendLine($"[open] columns={bar.Columns.Count} selCol={bar.SelectedMenuColumn?.Name ?? "<null>"} " +
                           $"menuFns={bar.MenuFunctions.Count}");

            // (A) A NEW result arrives while the bar is ALREADY OPEN — SetColumns runs
            //     against realized ComboBoxes. This is the order that can clobber the
            //     selection (gotcha #71: SelectedItem set before ItemsSource is current).
            var rows2 = new object?[6][];
            for (int i = 0; i < 6; i++) rows2[i] = new object?[] { i, i * 10 };
            vm.CurrentResult = new EmberTern.Core.Query.QueryResult
            {
                Columns = new[]
                {
                    new EmberTern.Core.Query.QueryColumn("A", typeof(int)),
                    new EmberTern.Core.Query.QueryColumn("B", typeof(int)),
                },
                Rows = rows2,
            };
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[reopen-order] selCol={bar.SelectedMenuColumn?.Name ?? "<null>"} " +
                           $"menuFns={bar.MenuFunctions.Count}");

            // (B) Simulate the user changing the column pick (like the screenshot's TEST).
            bar.SelectedMenuColumn = bar.Columns[1];
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[col-change] selCol={bar.SelectedMenuColumn?.Name ?? "<null>"} " +
                           $"selFn={bar.SelectedMenuFunction?.Label ?? "<null>"}");

            // (C) Picking a function AUTO-ADDS the chip (no separate Add button); the
            //     picker then resets to its placeholder.
            bar.SelectedMenuFunction = bar.MenuFunctions.First(f => f.Aggregate == EmberTern.Core.Query.GridAggregate.Sum);
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();

            log.AppendLine($"[after-pick] lines={bar.Lines.Count} " +
                           $"result='{(bar.Lines.Count > 0 ? bar.Lines[0].ResultText : "<no chip>")}' " +
                           $"selFn={bar.SelectedMenuFunction?.Label ?? "<null>"}");

            Assert.True(bar.SelectedMenuColumn is not null, "column context must persist.\n" + log);
            Assert.Single(bar.Lines);
            Assert.False(string.IsNullOrEmpty(bar.Lines[0].ResultText), "chip result must not be empty.\n" + log);
            Assert.Null(bar.SelectedMenuFunction);   // reset to placeholder after auto-add

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Proves the shared "filter active" marker style resolves + applies. A typo'd
    // class name would silently no-op (blank at runtime, no crash) — smoke wouldn't
    // catch it; this does. The dot's IsVisible is a plain binding to the panel VM's
    // IsFilterActive (toggled by Apply/Clear/SetColumns — covered by the VM tests).
    [Fact]
    public async System.Threading.Tasks.Task FilterActiveDotStyle_Applies()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var dot = new Border { Classes = { "filter-active-dot" } };
            var window = new Window { Content = dot };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            log.AppendLine($"dot Width={dot.Width} background={(dot.Background is null ? "<null>" : "ok")}");
            // Width/Background come only from the shared Border.filter-active-dot style.
            Assert.Equal(7, dot.Width);
            Assert.NotNull(dot.Background);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Proves the SearchableComboBox templates (ControlTheme loads, PART_Shell present)
    // and open/select/clear/close don't throw headless. The visual filtering UX is
    // verified manually on the live DB (popups live in a separate PopupRoot).
    [Fact]
    public async System.Threading.Tasks.Task SearchableComboBox_TemplatesAndOpensWithoutThrowing()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var domains = new[]
            {
                new DomainSpec("T_ID", "INTEGER"),
                new DomainSpec("T_KOD", "VARCHAR(20)"),
                new DomainSpec("T_KODPOCZ", "VARCHAR(6)"),
            };
            var cb = new global::EmberTern.App.Controls.SearchableComboBox
            {
                ItemsSource = domains,
                DisplayMemberPath = nameof(DomainSpec.Name),
                Watermark = string.Empty,
                Width = 200,
                Height = 24,
            };
            var window = new Window { Width = 400, Height = 300, Content = cb };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var shell = cb.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Shell");
            log.AppendLine($"[1] PART_Shell present = {shell is not null}");
            Assert.True(shell is not null, "SearchableComboBox template did not apply.\n" + log);

            cb.SelectedItem = domains[0];
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[2] SelectedItem = {(cb.SelectedItem as DomainSpec)?.Name}");
            Assert.Same(domains[0], cb.SelectedItem);

            cb.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            log.AppendLine("[3] opened without throwing");

            cb.SelectedItem = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(cb.SelectedItem);

            cb.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Editor Find/Replace + the Editor-scope predicate the CommandRouter resolves with.
    //
    // ⭐ [1] pins the fix for a MEASURED duplication: a TextEditor creates and installs its OWN SearchPanel,
    // and EditorSearch used to call SearchPanel.Install on top of it — registering a second
    // SearchInputHandler and returning a DIFFERENT panel, so Ctrl+F drove one instance while the context
    // menu's Find/Replace drove another. Asserting the handler COUNT is what makes the regression
    // impossible to reintroduce silently; asserting the panel is non-null would pass either way.
    [Fact]
    public async System.Threading.Tasks.Task EditorSearch_InstallsOnePanel_AndEditorScopePredicateHolds()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var outside = new TextBox { Width = 100, Height = 24 };
            var root = new StackPanel { Children = { editor, outside } };
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            int before = SearchHandlerCount(editor);
            EditorSearch.Install(editor);
            int after = SearchHandlerCount(editor);
            log.AppendLine($"[1] SearchInputHandler count before={before} after={after}, "
                           + $"panel={editor.SearchPanel is not null}, menu={editor.ContextMenu is not null}");

            Assert.Equal(1, before);                  // the editor already brought one
            Assert.Equal(1, after);                   // and Install must not add a second
            Assert.NotNull(editor.SearchPanel);
            Assert.NotNull(editor.ContextMenu);

            // [2] Find opens the editor's OWN panel — the one Ctrl+F reaches.
            Assert.True(EditorSearch.OpenFind(editor));
            Assert.False(editor.SearchPanel!.IsReplaceMode);
            Assert.True(EditorSearch.OpenReplace(editor));
            Assert.True(editor.SearchPanel!.IsReplaceMode);

            // [3] Replace is refused on a read-only surface (a DDL preview must not be offered a mutation).
            var preview = new TextEditor { Width = 300, Height = 120, IsReadOnly = true };
            root.Children.Add(preview);
            Dispatcher.UIThread.RunJobs();
            EditorSearch.Install(preview);
            Assert.True(EditorSearch.OpenFind(preview));
            Assert.False(EditorSearch.OpenReplace(preview));

            // [4] CommandScope.Editor liveness: the editor and its inner visual descendants count as
            // "in an editor"; a sibling TextBox and null do not. This is the whole of the router's Editor
            // scope test, and it replaced the focus probe the window's Ctrl+F handler used to run.
            Assert.Same(editor, EditorSearch.EditorFor(editor));
            var inner = editor.GetVisualDescendants().OfType<Avalonia.Visual>().FirstOrDefault(v => v != editor);
            if (inner is not null)
                Assert.Same(editor, EditorSearch.EditorFor(inner));
            Assert.Null(EditorSearch.EditorFor(outside));
            Assert.Null(EditorSearch.EditorFor(null));

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ⭐ The CommandRouter's resolution, driven through the real thing — the catalog declaration plus the
    // live focus probe. CommandCatalogTests pins the declaration; this pins that the router obeys it.
    //
    // [C1] is the audit's confirmed defect: F5 used to be a window binding whose command ended with
    // "anything else → Execute Query", so on a tab with nothing to execute it ran the SQL editor's text in
    // the user's working transaction. Here the router must simply decline.
    [Fact]
    public async System.Threading.Tasks.Task CommandRouter_ResolvesByScope_AndDeclinesWhereNothingIsLive()
    {
        var session = SharedSession;
        var log = new StringBuilder();
        var dir = Path.Combine(Path.GetTempPath(), "et-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        await session.Dispatch(() =>
        {
            using var service = new FirebirdConnectionService();
            var vm = new MainWindowViewModel(new ConnectionProfileStore(dir), service);

            var editor = new TextEditor { Width = 300, Height = 120 };
            var outside = new TextBox { Width = 120, Height = 24 };
            var tree = new ListBox { Width = 120, Height = 60 };
            var grid = new DataGrid { Width = 120, Height = 60 };
            var root = new StackPanel { Children = { editor, outside, tree, grid } };
            var window = new Window { Width = 400, Height = 400, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            EditorSearch.Install(editor);

            int sidebarFocusRequests = 0;
            var router = CommandRouter.Attach(
                window, () => vm, () => { sidebarFocusRequests++; return true; }, () => tree);

            // [1] F5 on a Query tab → the tab's main action is live, so the router handles it.
            var query = WorkspaceTabViewModel.CreateQuery(vm);
            vm.WorkspaceTabs.Add(query);
            vm.SelectTab(query);
            bool onQuery = router.Handle(Key.F5, KeyModifiers.None);
            log.AppendLine($"[1] F5 on a Query tab handled = {onQuery}");
            Assert.True(onQuery);

            // [2] ⭐ C1 — F5 on a tab that has no main action must be DECLINED, not repurposed.
            var ddl = WorkspaceTabViewModel.CreateDdl(
                vm, new MetadataObject("V_X", MetadataObjectKind.View), "select 1 from rdb$database", null);
            vm.WorkspaceTabs.Add(ddl);
            vm.SelectTab(ddl);
            bool onDdl = router.Handle(Key.F5, KeyModifiers.None);
            log.AppendLine($"[2] F5 on a Ddl tab handled = {onDdl} (must be False)");
            Assert.False(onDdl);

            // [3] Ctrl+F with the caret OUTSIDE an editor → Global scope: focus the sidebar filter.
            outside.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(router.Handle(Key.F, KeyModifiers.Control));
            log.AppendLine($"[3] sidebar focus requests after Ctrl+F outside an editor = {sidebarFocusRequests}");
            Assert.Equal(1, sidebarFocusRequests);

            // [4] Ctrl+F with the caret INSIDE an editor → Editor scope wins: the Find bar opens and the
            //     sidebar is left alone. This is the behaviour the deleted focus probe used to hand-code.
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(router.Handle(Key.F, KeyModifiers.Control));
            log.AppendLine($"[4] sidebar focus requests after Ctrl+F inside an editor = {sidebarFocusRequests}"
                           + $", replaceMode = {editor.SearchPanel!.IsReplaceMode}");
            Assert.Equal(1, sidebarFocusRequests);          // unchanged → Global never ran
            Assert.False(editor.SearchPanel!.IsReplaceMode); // Find, not Replace

            // [5] A gesture nobody claims is left alone.
            Assert.False(router.Handle(Key.F1, KeyModifiers.None));

            // ── etap 3: the focus scopes ─────────────────────────────────────────────────────────────
            // Every assertion below is deliberately about DECLINING. A gesture that fires here would run a
            // real New / Delete / Compile / Close flow, and a test must not need those to prove routing;
            // the resolution mappings are asserted without a UI in CommandCatalogTests.

            // [6] F3 / F8 are Tree- and Grid-scoped only: with the caret in a plain text box neither scope
            //     is live, so nothing claims them. (Before the scopes existed there was nowhere to put this
            //     distinction — the gesture would have had to be global and always-on.)
            outside.Focus();
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[6] outside a tree/grid: F3={router.Handle(Key.F3, KeyModifiers.None)} "
                           + $"F8={router.Handle(Key.F8, KeyModifiers.None)}");
            Assert.False(router.Handle(Key.F3, KeyModifiers.None));
            Assert.False(router.Handle(Key.F8, KeyModifiers.None));

            // [7] Tree scope becomes live with the caret in the object tree — but nothing is selected, so
            //     the command resolves to null and the key is still left alone rather than swallowed.
            tree.Focus();
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[7] in the tree, nothing selected: F3={router.Handle(Key.F3, KeyModifiers.None)} "
                           + $"F4={router.Handle(Key.F4, KeyModifiers.None)}");
            Assert.False(router.Handle(Key.F3, KeyModifiers.None));
            Assert.False(router.Handle(Key.F4, KeyModifiers.None));

            // [8] Grid scope live, but the selected tab (Ddl) owns no collection, so the unified collection
            //     router's own CanExecute declines — no per-grid knowledge needed anywhere.
            grid.Focus();
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[8] in a grid on a Ddl tab: F3={router.Handle(Key.F3, KeyModifiers.None)} "
                           + $"F8={router.Handle(Key.F8, KeyModifiers.None)}");
            Assert.False(router.Handle(Key.F3, KeyModifiers.None));
            Assert.False(router.Handle(Key.F8, KeyModifiers.None));

            // [9] F7 is Compile, declared only for the compilable tab kinds — a Ddl snapshot is not one.
            Assert.False(router.Handle(Key.F7, KeyModifiers.None));

            // [10] Ctrl+W closes the active tab, and the console tab is not closable, so it declines.
            vm.SelectTab(query);
            Assert.False(vm.CanCloseActiveTab);
            log.AppendLine($"[10] Ctrl+W on the non-closable console tab = {router.Handle(Key.W, KeyModifiers.Control)}");
            Assert.False(router.Handle(Key.W, KeyModifiers.Control));

            // [11] Ctrl+K is FormatSql for a query tab; F6 / Shift+F6 decline with no live transaction.
            Assert.False(vm.CanCommitAll);
            Assert.False(router.Handle(Key.F6, KeyModifiers.None));
            Assert.False(router.Handle(Key.F6, KeyModifiers.Shift));

            window.Close();
        }, CancellationToken.None);

        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        _out.WriteLine(log.ToString());
    }

    // ⭐ The Keyboard Manager's foundation, measured rather than assumed: AvaloniaEdit must not claim any
    // FUNCTION key inside a TextEditor, because the whole shortcut map is F-key-first.
    //
    // This started as a throwaway probe during the audit and it overturned inherited AvalonEdit lore —
    // F3/Shift+F3 are NOT find-next/previous here: SearchInputHandler registers its commands with no
    // KeyGesture at all. It is a permanent test because the risk is a silent one: an AvaloniaEdit upgrade
    // that started binding a function key would steal a global shortcut with the build still green.
    //
    // It also records what the editor DOES claim (Delete / Back / Return / arrows / Shift+Alt box-select),
    // which is why a global gesture may never be one of those and why "no Alt combos" is the right policy.
    [Fact]
    public async System.Threading.Tasks.Task Editor_ClaimsNoFunctionKey_AndClaimsTheEditingKeys()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            EditorSearch.Install(editor);
            Dispatcher.UIThread.RunJobs();

            var claimed = ClaimedGestures(editor).ToArray();
            log.AppendLine($"[gestures] {claimed.Length} claimed inside a TextEditor");

            var functionKeys = Enumerable.Range((int)Key.F1, 12).Cast<Key>().ToArray();
            var stolen = claimed.Where(g => functionKeys.Contains(g.Key)).ToArray();
            log.AppendLine("[function keys claimed] "
                           + (stolen.Length == 0 ? "<none>" : string.Join(", ", stolen.Select(g => g.ToString()))));

            Assert.True(stolen.Length == 0,
                "AvaloniaEdit now claims a function key, which collides with the F-key-first shortcut map: "
                + string.Join(", ", stolen.Select(g => g.ToString())));

            // The keys it genuinely owns — a global command must never take one of these.
            Assert.Contains(new KeyGesture(Key.Delete), claimed);
            Assert.Contains(new KeyGesture(Key.Back), claimed);
            Assert.Contains(new KeyGesture(Key.Return), claimed);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Every KeyGesture reachable from a live editor: its own KeyBindings, the TextArea's, and every input
    // handler in the TextArea's (recursive) default-handler chain — which is where AvaloniaEdit actually
    // keeps them.
    private static System.Collections.Generic.IEnumerable<KeyGesture> ClaimedGestures(TextEditor editor)
    {
        foreach (var kb in editor.KeyBindings) if (kb.Gesture is not null) yield return kb.Gesture;
        foreach (var kb in editor.TextArea.KeyBindings) if (kb.Gesture is not null) yield return kb.Gesture;
        foreach (var g in FromHandler(editor.TextArea.DefaultInputHandler)) yield return g;

        static System.Collections.Generic.IEnumerable<KeyGesture> FromHandler(object? handler)
        {
            if (handler is null) yield break;
            var type = handler.GetType();

            if (type.GetProperty("KeyBindings")?.GetValue(handler) is System.Collections.IEnumerable bindings)
            {
                foreach (var b in bindings)
                    if (b.GetType().GetProperty("Gesture")?.GetValue(b) is KeyGesture g) yield return g;
            }

            if (type.GetProperty("NestedInputHandlers")?.GetValue(handler) is System.Collections.IEnumerable nested)
            {
                foreach (var child in nested)
                    foreach (var g in FromHandler(child)) yield return g;
            }
        }
    }

    // How many SearchInputHandlers the editor's input pipeline carries. Reflection because
    // TextAreaDefaultInputHandler.NestedInputHandlers is the only place the count is observable.
    private static int SearchHandlerCount(TextEditor editor)
    {
        var handler = editor.TextArea.DefaultInputHandler;
        var nested = handler.GetType()
            .GetProperty("NestedInputHandlers")?
            .GetValue(handler) as System.Collections.IEnumerable;
        return nested?.Cast<object>().Count(h => h.GetType().Name == "SearchInputHandler") ?? -1;
    }

    // Stage 8 / Language Completion (App layer) — the LIVE interaction contract, which the pure Core
    // tests (resolver / arming / expansion / casing) structurally cannot reach: they know nothing about
    // focus, selections, the tunnelled Tab, or the overlay. Every assertion below is a rule the frozen
    // design states outright (docs/design/editor-language-expansion.md §2.2 "the hint shows the exact text
    // Tab will produce"; §7 the explicit-control contract):
    //   [1] TextArea really holds keyboard focus — the load-bearing assumption the hint's focus guard
    //       rests on. Were it false, CurrentEdit would return null forever and the whole feature would be
    //       silently dead with a green build (gotcha #199 — reflect the real API, never assume it).
    //   [2] the hint shows EXACTLY what Tab inserts, casing included — the hint must never lie;
    //   [3] Tab inserts precisely the previewed text, caret at the construct's edit point;
    //   [4] Escape dismisses → Tab is a plain indent again (never a hidden special action);
    //   [5] a selection means Tab is (block) indent — Language Completion never replaces selected code;
    //   [6] losing focus removes the hint — it never floats over another control.
    // Drives the REAL production seam (SqlEditorBehavior.Attach) and types through real key events, so
    // what is pinned is the shipped wiring rather than a hand-built rehearsal of it.
    [Fact]
    public async System.Threading.Tasks.Task LanguageCompletion_HintNeverLies_AndYieldsTabWhenNotArmed()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-probe-langexp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var vm = new MainWindowViewModel(store, service);

            var editor = new TextEditor { Width = 400, Height = 200 };
            var outside = new TextBox { Width = 100, Height = 24 };
            var root = new StackPanel { Children = { editor, outside } };
            var window = new Window { Width = 600, Height = 400, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            SqlEditorBehavior.Attach(editor, vm);   // the production wiring seam
            window.Activate();
            // NOTE: TextEditor itself is NOT focusable in AvaloniaEdit 12 — editor.Focus() is a no-op that
            // returns false. Keyboard focus lives on the TextArea, which is what a real click focuses; that
            // is precisely why the controller's guard reads TextArea.IsKeyboardFocusWithin (gotcha #225).
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();

            // [1] The assumption the focus guard is built on.
            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
            log.AppendLine($"[1] focused={focused?.GetType().Name ?? "<null>"} "
                + $"TextArea.IsKeyboardFocusWithin={editor.TextArea.IsKeyboardFocusWithin}");
            Assert.True(editor.TextArea.IsKeyboardFocusWithin,
                "the hint's focus guard requires TextArea to hold keyboard focus — if this is false, "
                + "Language Completion never arms at all.\n" + log);

            // The hint card's expansion label: the overlay's only text that isn't the ⇥ glyph.
            string? Hint() => OverlayLayer.GetOverlayLayer(editor)?.Children
                .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
                .Select(t => t.Text)
                .FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "⇥");

            // Sets the document and lands the caret at the end, always via a real 0 → end caret change so
            // Caret.PositionChanged fires exactly as it does while typing (that event is what updates the
            // hint). Deliberately NOT window.KeyTextInput: the headless input-injection path routes through
            // PresentationSource/Dispatcher.Send and only lands on the right thread for the FIRST
            // HeadlessUnitTestSession in the process — and this class starts 24 of them (gotcha #94).
            void Type(string text)
            {
                editor.SelectionLength = 0;
                editor.Document.Text = string.Empty;
                editor.CaretOffset = 0;
                Dispatcher.UIThread.RunJobs();
                editor.Document.Text = text;
                editor.CaretOffset = text.Length;
                Dispatcher.UIThread.RunJobs();
            }

            // Raises the key straight at the TextArea, on this thread. KeyDownEvent is registered
            // Tunnel|Bubble, so the route still runs our TUNNEL handler on the editor (an ancestor) and
            // then AvaloniaEdit's own bubble handler at the source — i.e. both the interception under test
            // and the real indent it must fall through to.
            void Press(Key key)
            {
                editor.TextArea.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = key,
                    KeyModifiers = KeyModifiers.None,
                });
                Dispatcher.UIThread.RunJobs();
            }

            // [2] The catalog spelling is lowercase; typing IF must PREVIEW as IF () THEN, not if () then.
            Type("IF");
            log.AppendLine($"[2] hint after typing 'IF' = {Hint() ?? "<none>"}");
            Assert.Equal("IF () THEN", Hint());

            // [3] Tab inserts exactly what was previewed (the document holds only the construct, so the
            //     preview and the whole document text must be character-identical), caret inside the parens.
            var previewed = Hint();
            Press(Key.Tab);
            log.AppendLine($"[3] after Tab: text='{editor.Document.Text}' caret={editor.CaretOffset}");
            Assert.Equal(previewed, editor.Document.Text);
            Assert.Equal(4, editor.CaretOffset);            // IF (▌) THEN
            Assert.Null(Hint());                            // expanded → hint gone

            // [4] Escape dismisses; Tab must then do the editor's normal thing (indent), NOT expand.
            Type("if");
            Assert.NotNull(Hint());
            Press(Key.Escape);
            log.AppendLine($"[4] hint after Escape = {Hint() ?? "<none>"}");
            Assert.Null(Hint());
            Press(Key.Tab);
            log.AppendLine($"[4] after Escape+Tab: text='{editor.Document.Text.Replace("\t", "\\t")}'");
            Assert.Equal("if", editor.Document.Text.TrimEnd());          // not expanded …
            Assert.True(editor.Document.Text.Length > 2, "Tab should have indented normally.\n" + log);

            // [5] With a selection, Tab belongs to (block) indent. The caret sits right after `where`,
            //     whose previous token (CUSTOMER) arms the WHERE clause — i.e. exactly the case that would
            //     otherwise eat the selected code.
            editor.Document.Text = "select *\nfrom CUSTOMER\nwhere";
            editor.CaretOffset = 0;
            Dispatcher.UIThread.RunJobs();
            editor.Select(0, editor.Document.TextLength);
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[5] selection={editor.SelectionLength}, hint = {Hint() ?? "<none>"}");
            Assert.True(editor.SelectionLength > 0, "the selection must be live for this case.\n" + log);
            Assert.Null(Hint());   // armed text under the caret, but a selection owns Tab
            Press(Key.Tab);
            var afterTab = editor.Document.Text;
            log.AppendLine($"[5] after Tab: text='{afterTab.Replace("\n", "\\n").Replace("\t", "\\t")}'");
            Assert.Contains("select *", afterTab);        // every line survived — nothing was replaced
            Assert.Contains("from CUSTOMER", afterTab);
            Assert.Contains("where", afterTab);

            // [6] The hint must not outlive the editor's focus.
            editor.SelectionLength = 0;
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();
            Type("sele");
            log.AppendLine($"[6] hint while focused = {Hint() ?? "<none>"}");
            Assert.Equal("select ", Hint());
            outside.Focus();
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[6] hint after focus moved away = {Hint() ?? "<none>"}");
            Assert.Null(Hint());

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Etap 5 / M1 — proves the editor's language service builds + caches a SemanticModel from a
    // captured metadata snapshot (the factory → AppMetadataSnapshot → SemanticModel wiring), and
    // that it resolves a simple aliased query. The alias-map path is unchanged (M5 switches
    // completion to the model); this only pins the M1 glue.
    [Fact]
    public async System.Threading.Tasks.Task EditorLanguageService_BuildsAndCachesSemanticModel()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // An immutable App snapshot (the real M1 provider) with one table + column.
            var objects = new[] { new MetadataObject("T", MetadataObjectKind.Table) };
            var columnCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new[] { new ColumnSpec("X", "INTEGER") },
            };
            var snapshot = AppMetadataSnapshot.Build(objects, columnCache);

            using var svc = new EditorLanguageService(editor, () => snapshot);
            const string sql = "select k.x from t k";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();

            // Deliberate-trigger sync path (mirrors what Ctrl+Space/dot use in M5).
            svc.EnsureFreshModel();

            log.AppendLine($"[1] model built = {svc.Model is not null}, fresh = {svc.ModelFresh}");
            Assert.NotNull(svc.Model);
            Assert.True(svc.ModelFresh);

            var model = svc.Model!;
            // Qualifier "k" (SELECT list, before FROM) resolves to the table reference.
            var qualifier = model.ReferenceAt(sql.IndexOf("k.", System.StringComparison.Ordinal));
            log.AppendLine($"[2] qualifier resolved = {qualifier?.IsResolved}, symbol = {qualifier?.Symbol?.GetType().Name}");
            Assert.NotNull(qualifier);
            var tref = Assert.IsType<TableReferenceSymbol>(qualifier!.Symbol);
            Assert.Equal("T", tref.TargetName);

            // Column "x" resolves against the snapshot's T.X.
            var col = model.ResolveAt(sql.IndexOf(".x", System.StringComparison.Ordinal) + 1);
            log.AppendLine($"[3] column symbol = {col?.GetType().Name} {col?.Name}");
            Assert.IsType<ColumnSymbol>(col);
            Assert.Equal("X", col!.Name);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Etap 6 / M3 — proves the semantic highlighter attaches to the editor, colorizes a resolved
    // query without throwing, and that the theme brush tokens it needs (the new editor tokens + the
    // reused per-kind IconColor_* palette) resolve from the real App resources. Catches a mistyped
    // resource key or a ColorizeLine offset bug that a build can't.
    [Fact]
    public async System.Threading.Tasks.Task SemanticHighlighter_AttachesAndColorizesWithoutThrowing()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var objects = new[] { new MetadataObject("T", MetadataObjectKind.Table) };
            var columnCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new[] { new ColumnSpec("X", "INTEGER") },
            };
            var snapshot = AppMetadataSnapshot.Build(objects, columnCache);

            using var svc = new EditorLanguageService(editor, () => snapshot);
            var hl = SemanticHighlighter.Attach(editor, () => svc.Model); // internal test overload

            const string sql = "select k.x from t k";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel(); // build the model synchronously (deliberate-trigger path)

            Assert.NotNull(svc.Model);
            Assert.Contains(editor.TextArea.TextView.LineTransformers, t => ReferenceEquals(t, hl));

            // Force layout/redraw so the transformer's ColorizeLine runs over the visible line.
            editor.TextArea.TextView.Redraw();
            editor.Measure(new Size(400, 200));
            editor.Arrange(new Rect(0, 0, 400, 200));
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine("[1] colorize ran without throwing");

            // The brush tokens the highlighter resolves must exist in both classes it uses.
            var theme = editor.ActualThemeVariant;
            foreach (var key in new[] { "EditorColumnBrush", "EditorLocalBrush", "IconColor_Table", "IconColor_Procedure" })
            {
                var ok = Application.Current!.Resources.TryGetResource(key, theme, out var v) && v is IBrush;
                log.AppendLine($"[token] {key} = {ok}");
                Assert.True(ok, $"theme brush '{key}' did not resolve");
            }

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Etap 6 / M4 — proves the navigation controller attaches to the editor without throwing, that
    // its underline renderer is registered, that the AccentBrush it paints resolves from the real App
    // resources, and — the behavioural bit — that Ctrl+Click at a resolved table offset dispatches to
    // the schema-object open callback with the mapped kind. The pointer/cursor/tooltip UX itself is
    // manual visual verification (design §25 / §9.5); this pins the plumbing a build can't.
    [Fact]
    public async System.Threading.Tasks.Task NavigationController_AttachesAndDispatchesGoToDefinition()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var objects = new[] { new MetadataObject("T", MetadataObjectKind.Table) };
            var columnCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new[] { new ColumnSpec("X", "INTEGER") },
            };
            var snapshot = AppMetadataSnapshot.Build(objects, columnCache);

            using var svc = new EditorLanguageService(editor, () => snapshot);

            (string Name, MetadataObjectKind Kind)? opened = null;
            string? openedByName = null;
            var nav = NavigationController.Attach(
                editor,
                () => svc.Model,
                // The cached, version-matched diagnostics the unified hover explains — the same list the
                // squiggles paint from.
                () => svc.Diagnostics,
                () => false, // no completion list / Parameter Helper competing in this probe
                (name, kind) => { opened = (name, kind); return true; },
                word => { openedByName = word; return true; });

            const string sql = "select k.x from t k";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            Assert.NotNull(svc.Model);

            // The underline renderer is registered on the text view.
            Assert.Contains(editor.TextArea.TextView.BackgroundRenderers, r => r.GetType().Name == "UnderlineRenderer");
            log.AppendLine("[1] underline renderer registered");

            // The accent brush the underline paints must resolve in the current theme.
            var theme = editor.ActualThemeVariant;
            var accentOk = Application.Current!.Resources.TryGetResource("AccentBrush", theme, out var v) && v is IBrush;
            Assert.True(accentOk, "AccentBrush did not resolve");
            log.AppendLine("[2] AccentBrush resolves");

            // Ctrl+Click on the table name "t" (offset 16 in "select k.x from t k") → open schema
            // object "T" as a Table (the authoritative kind comes from loaded metadata in production).
            int tOffset = sql.IndexOf(" t ", System.StringComparison.Ordinal) + 1;
            var navigated = nav.NavigateForTest(tOffset);
            Assert.True(navigated, "expected navigation at the table offset");
            Assert.NotNull(opened);
            Assert.Equal("T", opened!.Value.Name);
            Assert.Equal(MetadataObjectKind.Table, opened.Value.Kind);
            Assert.Null(openedByName); // resolved via the model, not the name fallback
            log.AppendLine($"[3] go-to-def dispatched: {opened.Value.Name}/{opened.Value.Kind}");

            // Stage 8 / M1 — the caret-symbol reference HIGHLIGHT moved to the unified RelatedElementsRenderer
            // (CaretSymbolReferenceProducer); NavigationController no longer registers a reference renderer.
            // The computation stays reachable via ReferencesForTest (which delegates to the producer), pinned below.

            // Local find references: the alias `k` occurs twice (declaration + qualifier).
            int kOffset = sql.IndexOf("k.", System.StringComparison.Ordinal);
            Assert.True(nav.ReferencesForTest(kOffset).Count >= 2, "expected >= 2 alias occurrences");
            // A schema object / column has no local-reference highlight (calm — not boxed).
            Assert.Empty(nav.ReferencesForTest(tOffset));
            log.AppendLine("[5] local find-references highlights only locals");

            // M5 — safe local rename: renaming a database object is refused (§0).
            Assert.False(nav.TryRenameForTest(tOffset, "renamed"), "a table must not be locally renamed");
            Assert.Equal(sql, editor.Text);
            // Renaming the alias rewrites every occurrence atomically.
            Assert.True(nav.TryRenameForTest(kOffset, "m"), "expected the alias rename to apply");
            Assert.Equal("select m.x from t m", editor.Text);
            log.AppendLine("[6] safe local rename applied; DB object rename refused");

            nav.Detach();
            Assert.DoesNotContain(editor.TextArea.TextView.BackgroundRenderers, r => r.GetType().Name == "UnderlineRenderer");
            log.AppendLine("[7] detach removed the renderer");

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Stage 8 / M1 — the Related Elements Highlighting renderer, driven by a REAL caret move on a real
    // editor: placing the caret next to the first EXECUTE PROCEDURE call's '(' must produce the bracket
    // pair (the manual-QA "first call doesn't activate" report). Pins the caret → recompute → spans wiring
    // end to end; the paint-timing fix itself (Redraw over InvalidateVisual) is visual QA.
    [Fact]
    public async System.Threading.Tasks.Task RelatedElementsRenderer_CaretAtFirstCall_ProducesBracketPair()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var objects = new[] { new MetadataObject("XXX_ZEST_FAKTUR_CR", MetadataObjectKind.Procedure) };
            var snapshot = AppMetadataSnapshot.Build(
                objects,
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                    System.StringComparer.OrdinalIgnoreCase));
            using var svc = new EditorLanguageService(editor, () => snapshot);

            var renderer = RelatedElementsRenderer.Attach(editor, () => svc.Model);

            const string sql = "execute procedure sp$_x(:id)\n\nselect * from xxx_zest_faktur_cr(:a, :b)";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();

            int open = sql.IndexOf('(');
            int close = sql.IndexOf(')');
            editor.CaretOffset = open + 1; // right after the FIRST '(' — the reported spot
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(new TextSpan(open, 1), renderer.SpansForTest);
            Assert.Contains(new TextSpan(close, 1), renderer.SpansForTest);

            window.Close();
        }, CancellationToken.None);
    }

    // D15.1 Seam B — the current-line marker is the BACKDROP. CurrentLineRenderer.Attach inserts itself
    // FIRST in BackgroundRenderers, so the squiggle / related-element renderers (added earlier by the shared
    // editor wiring) draw ON TOP and stay legible over the calm full-line wash. This pins that ordering
    // decision and exercises the real full-line Draw path over a paused span (must not throw). Visual
    // appearance (colour, alpha, the left bar) is user QA.
    [Fact]
    public async System.Threading.Tasks.Task CurrentLineRenderer_Attach_IsBackdropBelowOtherRenderers()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            editor.Text = "begin\n  a = 1;\n  b = 2;\nend";
            Dispatcher.UIThread.RunJobs();

            // A sibling background renderer added FIRST, exactly as the shared editor wiring does before the
            // current-line one.
            using var svc = new EditorLanguageService(editor);
            var related = RelatedElementsRenderer.Attach(editor, () => svc.Model);

            int start = editor.Text.IndexOf("b = 2", System.StringComparison.Ordinal);
            var current = CurrentLineRenderer.Attach(editor, () => (start, "b = 2".Length));

            // Inserted first ⇒ it is the backdrop; the related renderer sits above it.
            var bg = editor.TextArea.TextView.BackgroundRenderers;
            Assert.Same(current, bg[0]);
            Assert.True(bg.IndexOf(related) > bg.IndexOf(current));

            // A real paint with a live paused span must not throw (full-line geometry path).
            editor.TextArea.TextView.Redraw();
            Dispatcher.UIThread.RunJobs();

            window.Close();
        }, CancellationToken.None);
    }

    // D15.5 Seam A — the inline-values renderer paints greyed name=value annotations at line ends. It is
    // APPENDED after the current-line renderer (so it draws on top of the calm wash) and never shifts text
    // (it paints past the line's text end). This pins the ordering + exercises the real annotation Draw path
    // (must not throw); appearance/positioning is user QA.
    [Fact]
    public async System.Threading.Tasks.Task InlineValuesRenderer_Attach_AppendedAboveCurrentLine_DrawsWithoutThrow()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            editor.Text = "begin\n  a = 1;\n  b = 2;\nend";
            Dispatcher.UIThread.RunJobs();

            int start = editor.Text.IndexOf("b = 2", System.StringComparison.Ordinal);
            var current = CurrentLineRenderer.Attach(editor, () => (start, "b = 2".Length));
            var inline = InlineValuesRenderer.Attach(editor,
                () => new[] { new InlineValueAnnotation(start, "B = 2") });

            // Appended after the current-line renderer ⇒ it paints on top of the wash.
            var bg = editor.TextArea.TextView.BackgroundRenderers;
            Assert.True(bg.IndexOf(inline) > bg.IndexOf(current));

            // A real paint with a live annotation must not throw (FormattedText + line-end geometry path).
            editor.TextArea.TextView.Redraw();
            Dispatcher.UIThread.RunJobs();

            window.Close();
        }, CancellationToken.None);
    }

    // D15.2 Seam A — the debugger toolbar's icon geometries and the new loop-category brush must resolve
    // at runtime. The build already validates the compiled StaticResource Icon.* usages; this also covers
    // the DynamicResource brush token (resolved at runtime, not compile) and pins it in BOTH themes.
    [Fact]
    public async System.Threading.Tasks.Task DebuggerToolbarIcons_GeometriesAndLoopBrush_Resolve()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;

            string[] geometries =
            {
                "Icon.Play", "Icon.Stop", "Icon.StepInto", "Icon.StepOver", "Icon.StepOut",
                "Icon.RunToCursor", "Icon.RunToSuspend", "Icon.NextIteration", "Icon.LoopExit",
                "Icon.Restart", "Icon.BreakException",
            };
            foreach (var key in geometries)
            {
                Assert.True(
                    app.Resources.TryGetResource(key, null, out var g) && g is Avalonia.Media.Geometry,
                    $"debugger icon geometry '{key}' does not resolve");
            }

            foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
            {
                Assert.True(
                    app.Resources.TryGetResource("DebugLoopIconBrush", theme, out var b) && b is Avalonia.Media.IBrush,
                    $"DebugLoopIconBrush does not resolve in {theme}");

                // D15.2 Seam B — the debugger identity mark (DebuggerIcon, replaces Icon.Bug)
                // is a two-colour composite: pin both of its REUSED brush tokens in both themes.
                Assert.True(
                    app.Resources.TryGetResource("AccentIconBrush", theme, out var a) && a is Avalonia.Media.IBrush,
                    $"AccentIconBrush does not resolve in {theme}");
                Assert.True(
                    app.Resources.TryGetResource("DebugBreakpointBrush", theme, out var d) && d is Avalonia.Media.IBrush,
                    $"DebugBreakpointBrush does not resolve in {theme}");
            }

            // The DebuggerIcon composite must construct + apply its ControlTheme (it is not a
            // keyed geometry, so the StaticResource build-validation above does not cover it).
            var dbgIcon = new EmberTern.App.Controls.DebuggerIcon();
            Assert.NotNull(dbgIcon);

            // Stage Q / Q3 — the code-action bulb is built in CODE (an overlay control, not XAML), so
            // its geometry and both of its brush states are resolved at runtime and nothing else would
            // catch a typo in a key.
            Assert.True(
                app.Resources.TryGetResource("Icon.LightbulbFilled", null, out var bulb) && bulb is Avalonia.Media.Geometry,
                "code-action bulb geometry does not resolve");
            foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
            {
                Assert.True(
                    app.Resources.TryGetResource("CodeActionBrush", theme, out var rest) && rest is Avalonia.Media.IBrush,
                    $"CodeActionBrush (bulb at rest) does not resolve in {theme}");
                Assert.True(
                    app.Resources.TryGetResource("AccentIconBrush", theme, out var hot) && hot is Avalonia.Media.IBrush,
                    $"AccentIconBrush (bulb hovered) does not resolve in {theme}");
            }
        }, CancellationToken.None);
    }

    // Stage Q — the APP half of the code-action pipeline on a REAL editor: GetActionsAtCaret sees the
    // engine's actions, Ctrl+. opens the menu, and the menu is dismissed / invalidated the way a context
    // menu must be. Lives here because real key events need the ONE shared headless session (#94/#226).
    //
    // The bulb's own path is covered by CodeActionBulb_* below. It is testable at all only because the
    // dwell timer was removed: headless runs no DispatcherTimer whatsoever (measured during the Q3 QA
    // trace — a plain 450ms control timer ticked 0 times), so while the bulb depended on one, the path
    // that actually failed for the user was the one path no test could reach.
    [Fact]
    public async System.Threading.Tasks.Task CodeActionMenu_OpensOnCtrlPeriod_AndDismissesLikeAContextMenu()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            const string Sql = "select id_rozliczenie from rozliczenie r join pozycja p on 1 = 1";
            var meta = new ProbeMetadata()
                .Col("ROZLICZENIE", "ID_ROZLICZENIE")
                .Col("POZYCJA", "ID_ROZLICZENIE");

            var model = SemanticModel.Build(Sql, meta);
            var diagnostics = EmberTern.Core.Sql.Language.DiagnosticsEngine.Analyze(model);
            log.AppendLine($"[1] diagnostics = {diagnostics.Count}");
            foreach (var d in diagnostics) log.AppendLine($"    {d.Code} {d.Category} [{d.Start}..{d.End})");

            var editor = new TextEditor { Document = new AvaloniaEdit.Document.TextDocument(Sql) };
            var window = new Window { Content = editor, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var nav = NavigationController.Attach(
                editor,
                () => model,
                () => diagnostics,
                () => false,
                (_, _) => false,
                _ => false);

            int columnOffset = Sql.IndexOf("id_rozliczenie", StringComparison.Ordinal);
            editor.CaretOffset = columnOffset + 3;   // inside the ambiguous column
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[2] caret = {editor.CaretOffset}, IsReadOnly = {editor.IsReadOnly}");

            var actions = nav.CodeActionsForTest(editor.CaretOffset);
            log.AppendLine($"[3] GetActionsAtCaret = {actions.Count}");
            Assert.Equal(2, actions.Count);

            void CtrlPeriod()
            {
                editor.TextArea.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.OemPeriod,
                    KeyModifiers = KeyModifiers.Control,
                });
                Dispatcher.UIThread.RunJobs();
            }

            // Opens.
            CtrlPeriod();
            Assert.True(nav.IsCodeActionMenuOpen);

            // Escape dismisses without applying, and hands the keyboard back so typing can continue.
            editor.TextArea.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
                KeyModifiers = KeyModifiers.None,
            });
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[4] after Escape: open = {nav.IsCodeActionMenuOpen}, text unchanged = {editor.Document.Text == Sql}");
            Assert.False(nav.IsCodeActionMenuOpen);
            Assert.Equal(Sql, editor.Document.Text);   // Escape performed NO action

            // Moving the caret invalidates an open menu: its actions describe the position it was built
            // for, and offering them somewhere else is the wrong behaviour.
            CtrlPeriod();
            Assert.True(nav.IsCodeActionMenuOpen);
            editor.CaretOffset = editor.Document.TextLength;
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[5] after caret move: open = {nav.IsCodeActionMenuOpen}");
            Assert.False(nav.IsCodeActionMenuOpen);

            // A text change under an open menu invalidates it too.
            editor.CaretOffset = columnOffset + 3;
            Dispatcher.UIThread.RunJobs();
            CtrlPeriod();
            Assert.True(nav.IsCodeActionMenuOpen);
            editor.Document.Insert(editor.Document.TextLength, " ");
            Dispatcher.UIThread.RunJobs();
            log.AppendLine($"[6] after text change: open = {nav.IsCodeActionMenuOpen}");
            Assert.False(nav.IsCodeActionMenuOpen);

            nav.Detach();
            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Stage Q — the menu must be fully drivable from the keyboard, like every other completion list:
    // first item preselected, arrows move, Enter applies. Focus deliberately stays in the EDITOR (an
    // overlay-hosted list does not reliably take it), so these keys are raised at the TextArea — which
    // is also exactly how they arrive in the real app.
    [Fact]
    public async System.Threading.Tasks.Task CodeActionMenu_IsDrivableFromTheKeyboard()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            const string Sql = "select id_rozliczenie from rozliczenie r join pozycja p on 1 = 1";
            var meta = new ProbeMetadata()
                .Col("ROZLICZENIE", "ID_ROZLICZENIE")
                .Col("POZYCJA", "ID_ROZLICZENIE");
            var model = SemanticModel.Build(Sql, meta);
            var diagnostics = EmberTern.Core.Sql.Language.DiagnosticsEngine.Analyze(model);

            var editor = new TextEditor { Document = new AvaloniaEdit.Document.TextDocument(Sql) };
            var window = new Window { Content = editor, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var nav = NavigationController.Attach(
                editor, () => model, () => diagnostics, () => false, (_, _) => false, _ => false);
            editor.CaretOffset = Sql.IndexOf("id_rozliczenie", StringComparison.Ordinal) + 3;
            Dispatcher.UIThread.RunJobs();

            void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
            {
                editor.TextArea.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = key,
                    KeyModifiers = modifiers,
                });
                Dispatcher.UIThread.RunJobs();
            }

            Press(Key.OemPeriod, KeyModifiers.Control);
            Assert.True(nav.IsCodeActionMenuOpen);
            Assert.Equal(0, nav.CodeActionSelectionForTest);   // the first item is armed on open

            Press(Key.Down);
            Assert.Equal(1, nav.CodeActionSelectionForTest);
            Press(Key.Up);
            Assert.Equal(0, nav.CodeActionSelectionForTest);
            Press(Key.Up);                                      // wraps rather than sticking at the end
            Assert.Equal(1, nav.CodeActionSelectionForTest);

            // Enter applies the SELECTED action — the second one, so this also proves the arrows really
            // chose it rather than the menu always running its first entry.
            var expected = nav.CodeActionsForTest(editor.CaretOffset)[1];
            Press(Key.Enter);

            Assert.False(nav.IsCodeActionMenuOpen);
            Assert.NotEqual(Sql, editor.Document.Text);
            Assert.Contains(expected.Edits[0].NewText, editor.Document.Text, StringComparison.Ordinal);

            nav.Detach();
            window.Close();
        }, CancellationToken.None);
    }

    // Stage Q / Q3 — THE path that failed in QA: the user simply moves the caret onto a line that has a
    // code action, and the bulb must appear. Nothing else happens — no model rebuild, no scroll, no
    // explicit refresh. This is now reachable by a test only because the bulb no longer waits on a
    // DispatcherTimer, which headless cannot run.
    [Fact]
    public async System.Threading.Tasks.Task CodeActionBulb_AppearsWhenTheCaretMovesOntoAnActionableLine()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            // The actionable column is on line 1; line 2 has nothing to offer.
            const string Sql = "select id_rozliczenie\nfrom rozliczenie r join pozycja p on 1 = 1";
            var meta = new ProbeMetadata()
                .Col("ROZLICZENIE", "ID_ROZLICZENIE")
                .Col("POZYCJA", "ID_ROZLICZENIE");
            var model = SemanticModel.Build(Sql, meta);
            var diagnostics = EmberTern.Core.Sql.Language.DiagnosticsEngine.Analyze(model);
            Assert.NotEmpty(diagnostics);

            var editor = new TextEditor { Document = new AvaloniaEdit.Document.TextDocument(Sql) };
            var window = new Window { Content = editor, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var nav = NavigationController.Attach(
                editor, () => model, () => diagnostics, () => false, (_, _) => false, _ => false);

            // Caret parked away from the ambiguity: nothing offered, nothing shown.
            editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset + 2;
            Dispatcher.UIThread.RunJobs();
            Assert.False(nav.IsCodeActionIndicatorVisible);

            // The user clicks onto the ambiguous column. Caret movement alone must produce the bulb.
            editor.CaretOffset = Sql.IndexOf("id_rozliczenie", StringComparison.Ordinal) + 3;
            Dispatcher.UIThread.RunJobs();
            Assert.True(nav.IsCodeActionIndicatorVisible, "the bulb did not appear on caret movement alone");

            // …and leaving the line takes it away again.
            editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset + 2;
            Dispatcher.UIThread.RunJobs();
            Assert.False(nav.IsCodeActionIndicatorVisible);

            nav.Detach();
            window.Close();
        }, CancellationToken.None);
    }

    // Stage Q / Q3 — the bulb's PLACEMENT must survive being asked while the view's line geometry is not
    // valid. It positions from a timer tick and from ModelUpdated, i.e. OUTSIDE the render pass, where
    // TextView.VisualLines THROWS if a re-measure is pending (EditorPopups' rule; the double-click crash).
    // The Q3 QA bug was exactly this: the placement idiom was lifted from a background RENDERER, whose
    // Draw only ever runs when the lines are valid. A freshly-laid-out editor cannot reproduce it, so
    // this drives the case directly: invalidate, then ask.
    [Fact]
    public async System.Threading.Tasks.Task CodeActionBulb_PlacementSurvivesInvalidVisualLines()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            const string Sql = "select id_rozliczenie from rozliczenie r join pozycja p on 1 = 1";
            var meta = new ProbeMetadata()
                .Col("ROZLICZENIE", "ID_ROZLICZENIE")
                .Col("POZYCJA", "ID_ROZLICZENIE");
            var model = SemanticModel.Build(Sql, meta);
            var diagnostics = EmberTern.Core.Sql.Language.DiagnosticsEngine.Analyze(model);

            var editor = new TextEditor { Document = new AvaloniaEdit.Document.TextDocument(Sql) };
            var window = new Window { Content = editor, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var overlay = OverlayLayer.GetOverlayLayer(editor);
            int baseline = overlay!.Children.Count;   // BEFORE anything can add a bulb

            var nav = NavigationController.Attach(
                editor, () => model, () => diagnostics, () => false, (_, _) => false, _ => false);
            editor.CaretOffset = Sql.IndexOf("id_rozliczenie", StringComparison.Ordinal) + 3;

            // Force a pending re-measure, then ask for the bulb: this must not throw, and must not leave
            // a control stranded in the overlay at a position that was never computed.
            editor.TextArea.TextView.Redraw();
            editor.TextArea.TextView.InvalidateMeasure();

            nav.RefreshCodeActionIndicator();   // must not throw

            Assert.True(
                nav.IsCodeActionIndicatorVisible || overlay.Children.Count == baseline,
                "the bulb was left in the overlay without a computed position");

            // Once the view settles, it must be placeable.
            Dispatcher.UIThread.RunJobs();
            nav.RefreshCodeActionIndicator();
            Assert.True(nav.IsCodeActionIndicatorVisible);

            // …and being "added" is not the same as being SEEN. The icon is a TemplatedControl, so it
            // draws only through its ControlTheme; a missing theme, a zero measure, or a position off the
            // visible area all leave the user with no bulb while every assertion about state still passes.
            window.Width = 900;
            window.Height = 400;
            Dispatcher.UIThread.RunJobs();
            var bulb = Assert.IsAssignableFrom<Control>(overlay.Children[overlay.Children.Count - 1]);
            bulb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _out.WriteLine($"bulb desired = {bulb.DesiredSize}, left = {Canvas.GetLeft(bulb)}, top = {Canvas.GetTop(bulb)}");

            // Being "added" is not the same as being SEEN — the state assertions above would all pass for
            // a control measuring to nothing or sitting outside the view. THIS is the pair that would have
            // caught the placement bug: the line here runs the full width of the editor, which put the
            // anchor past the right edge (measured: Right=896 in a 900-wide view) where the overlay clips
            // it away — present in every field we track, invisible to the user.
            Assert.True(
                bulb.DesiredSize.Width > 0 && bulb.DesiredSize.Height > 0,
                "the bulb measures to nothing — it would be invisible");

            // The assertion that was missing, and the one that would have caught the live bug: SvgIcon
            // STROKES its geometry with Foreground, so a null brush paints nothing while every other
            // property still looks healthy. Theme-scoped brushes need the theme variant on lookup —
            // Control.FindResource(key) does not supply one and silently yields UNSET.
            var bulbIcon = Assert.IsType<Avalonia.Controls.Shapes.Path>(((Border)bulb).Child);
            Assert.NotNull(bulbIcon.Data);
            Assert.NotNull(bulbIcon.Fill);
            Assert.True(bulb.Opacity > 0, "the bulb is fully transparent");
            Assert.InRange(Canvas.GetLeft(bulb), 0, editor.Bounds.Width - 1);
            Assert.InRange(Canvas.GetTop(bulb), 0, Math.Max(1, editor.Bounds.Height) - 1);

            nav.Detach();
            // Nothing stranded: this is what caught the re-entrancy that added a second, orphaned bulb.
            Assert.Equal(baseline, overlay.Children.Count);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class ProbeMetadata : ISqlMetadataProvider
    {
        private readonly System.Collections.Generic.Dictionary<string, ObjectMetadata> _objects =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<ColumnMetadata>> _cols =
            new(StringComparer.OrdinalIgnoreCase);

        public ProbeMetadata Col(string table, string name)
        {
            if (!_objects.ContainsKey(table)) _objects[table] = new ObjectMetadata(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, "INTEGER"));
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public System.Collections.Generic.IReadOnlyList<ColumnMetadata> GetColumns(string t)
            => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public System.Collections.Generic.IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r)
            => Array.Empty<RoutineParameterMetadata>();
        public System.Collections.Generic.IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // UX Polish Seam 4 — MessageBanner is the IDE's ONE message surface (debugger Error Bar + pre-flight rows
    // + every object editor + Execute Procedure + Security Manager). Its severity mapping is resolved at
    // RUNTIME (DynamicResource brush key + geometry key), so the build cannot validate it: pin that every
    // severity's brush resolves in BOTH themes and its geometry resolves, and that the control constructs
    // (its XAML, incl. the element bindings onto its own properties, actually loads).
    [Fact]
    public async System.Threading.Tasks.Task MessageBannerSeverities_GeometriesAndBrushes_Resolve()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            var severities = new[]
            {
                EmberTern.App.Controls.MessageSeverity.Info,
                EmberTern.App.Controls.MessageSeverity.Success,
                EmberTern.App.Controls.MessageSeverity.Warning,
                EmberTern.App.Controls.MessageSeverity.Error,
            };

            foreach (var severity in severities)
            {
                var geometryKey = EmberTern.App.Controls.MessageBanner.GeometryKeyFor(severity);
                Assert.True(
                    app.Resources.TryGetResource(geometryKey, null, out var g) && g is Avalonia.Media.Geometry,
                    $"MessageBanner geometry '{geometryKey}' ({severity}) does not resolve");

                var brushKey = EmberTern.App.Controls.MessageBanner.BrushKeyFor(severity);
                foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
                {
                    Assert.True(
                        app.Resources.TryGetResource(brushKey, theme, out var b) && b is Avalonia.Media.IBrush,
                        $"MessageBanner brush '{brushKey}' ({severity}) does not resolve in {theme}");
                }
            }

            // The control loads, and changing Severity re-derives BOTH keys (the bindings paint from these).
            var banner = new EmberTern.App.Controls.MessageBanner { Message = "boom" };
            Assert.Equal("ErrorBrush", banner.SeverityBrushKey); // default severity is Error
            Assert.True(banner.ShowCopy); // Copy is on by default — no per-host decision
            banner.Severity = EmberTern.App.Controls.MessageSeverity.Warning;
            Assert.Equal("WarningBrush", banner.SeverityBrushKey);
            Assert.Equal("Icon.AlertTriangle", banner.SeverityGeometryKey);
        }, CancellationToken.None);
    }

    // UX Polish Seam 4 (QA) — the banner's chrome comes from exactly TWO shared variants in
    // ControlStyles.axaml, never from a per-host local value. Style setters only apply once the control is
    // in a styled tree, so this hosts them in a real window and asserts the applied values: standalone =
    // a full border, .docked = horizontal rules only, both on PanelBrush.
    [Fact]
    public async System.Threading.Tasks.Task MessageBannerChrome_HasExactlyTwoVariants()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var standalone = new EmberTern.App.Controls.MessageBanner { Message = "x" };
            var docked = new EmberTern.App.Controls.MessageBanner { Message = "x" };
            docked.Classes.Add("docked");

            var window = new Avalonia.Controls.Window
            {
                Content = new Avalonia.Controls.StackPanel { Children = { standalone, docked } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Avalonia.Thickness(1), standalone.BorderThickness);
            Assert.Equal(new Avalonia.Thickness(0, 1, 0, 1), docked.BorderThickness);
            Assert.NotNull(standalone.Background);
            Assert.Equal(standalone.Background, docked.Background);
            Assert.Equal(standalone.BorderBrush, docked.BorderBrush);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// The settings-group card is a real, APPLIED style, not just a class name someone typed.
    /// <para>
    /// Pinned in a real window because "the style is in the file" and "the border paints" are different
    /// claims (#251), and because a card that silently resolves to no background is exactly the failure the
    /// brush-lookup gotcha (#250) produces — everything looks healthy and nothing is drawn. It also pins the
    /// figure/ground pair the grouping depends on: the card is RECESSED against the panel chrome that hosts
    /// it, so the two must not resolve to the same brush.
    /// </para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task SettingsGroupCard_IsAnAppliedStyle_AndReadsAgainstItsHost()
    {
        var session = SharedSession;

        await session.Dispatch(() =>
        {
            var group = new Border();
            group.Classes.Add("settings-group");

            var header = new TextBlock { Text = "Parsing" };
            header.Classes.Add("group-header");

            var caption = new TextBlock { Text = "Column separator" };
            caption.Classes.Add("field-label");

            var host = new Border { Background = null };
            var window = new Avalonia.Controls.Window
            {
                Content = new StackPanel { Children = { host, group, header, caption } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The card is enclosed and filled — the two things that make it read as a group.
            Assert.Equal(new Avalonia.Thickness(1), group.BorderThickness);
            Assert.NotNull(group.Background);
            Assert.NotNull(group.BorderBrush);
            Assert.Equal(new Avalonia.CornerRadius(3), group.CornerRadius);

            // Recessed against the panel chrome it sits in: same brush would erase the grouping.
            var panelBrush = window.FindResource("PanelBrush");
            Assert.NotNull(panelBrush);
            Assert.NotEqual(panelBrush, group.Background);

            // A group header must outweigh a field caption, or the two compete and neither reads as a title.
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, header.FontWeight);
            Assert.NotEqual(caption.FontWeight, header.FontWeight);
            Assert.NotEqual(caption.Foreground, header.Foreground);

            window.Close();
        }, CancellationToken.None);
    }

    // UX Polish Seam 4 (QA) — the SQL Editor's Messages panel stays a log, but a problem entry speaks the
    // one message language: its stripe + colour come from the SAME MessageBanner mapping (no icon — that
    // would widen only the marked rows and break the timestamp alignment). An Info line keeps the normal
    // reading colour and earns no marker.
    [Fact]
    public void QueryMessage_SeverityPresentation_MatchesTheBanner()
    {
        var error = new QueryMessageViewModel(EmberTern.App.Controls.MessageSeverity.Error, "boom");
        var warning = new QueryMessageViewModel(EmberTern.App.Controls.MessageSeverity.Warning, "careful");
        var info = new QueryMessageViewModel(EmberTern.App.Controls.MessageSeverity.Info, "done");

        Assert.Equal(
            EmberTern.App.Controls.MessageBanner.BrushKeyFor(EmberTern.App.Controls.MessageSeverity.Error),
            error.SeverityBrushKey);

        Assert.True(error.ShowSeverityMarker);
        Assert.True(warning.ShowSeverityMarker);
        Assert.False(info.ShowSeverityMarker);

        Assert.Equal("ErrorBrush", error.MessageBrushKey);
        Assert.Equal("WarningBrush", warning.MessageBrushKey);
        Assert.Equal("ForegroundBrush", info.MessageBrushKey); // a log is mostly Info — keep it legible
    }

    // UX Polish Seam 4 — a blocking pre-flight item is an Error row, everything else a Warning row. The
    // severity split is the ITEM's own decision (no brush in the data, no severity logic in the view).
    [Fact]
    public void DebugPreflightItem_BannerSeverity_FollowsIsBlocking()
    {
        var blocking = new EmberTern.App.Debugging.DebugPreflightItem(
            EmberTern.App.Debugging.DebugPreflightSeverity.Error, "no step points", IsBlocking: true);
        var advisory = new EmberTern.App.Debugging.DebugPreflightItem(
            EmberTern.App.Debugging.DebugPreflightSeverity.Warning, "autonomous transaction");

        Assert.Equal(EmberTern.App.Controls.MessageSeverity.Error, blocking.BannerSeverity);
        Assert.Equal(EmberTern.App.Controls.MessageSeverity.Warning, advisory.BannerSeverity);
    }

    // (D15.3 F5 routing is now a window-level Go router — MainWindowViewModel.GoCommand → DebuggerTabViewModel
    //  .RequestGoAsync — tested deterministically at the VM level in DebuggerTabVmTests, so the earlier headless
    //  view test that raised F5 into a hosted DebuggerTabView is retired.)

    // Etap 6 UX-polish regression pin — the "FROM view / FROM proc(…) don't resolve" report. A view /
    // selectable procedure used in FROM only resolves once its metadata category has loaded, which (on
    // connect) happens AFTER the model was first built (categories prefetch sequentially; Views /
    // Procedures load last). The fix is: when metadata grows, rebuild the model against a FRESH
    // snapshot. This pins that exact contract — RefreshModelWithMetadata (what the ObjectsChanged →
    // NotifyMetadataChanged path ultimately invokes) picks up a view/proc that was absent at first
    // build and resolves it as a schema object — end to end through the real EditorLanguageService.
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_RebuildsAgainstGrownMetadata_ResolvesViewAndProc()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var emptyCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);

            // The snapshot the editor sees, swappable to simulate a category loading AFTER first build.
            ISqlMetadataProvider snapshot = AppMetadataSnapshot.Build(Array.Empty<MetadataObject>(), emptyCols);

            using var svc = new EditorLanguageService(editor, () => snapshot);
            const string sql = "select * from myview";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();

            int viewOffset = sql.IndexOf("myview", System.StringComparison.Ordinal) + 3;

            // Before the Views category loads: the FROM name is bound only as an unresolved table
            // reference (Target == null) — NOT a navigable schema object, so it gets the low-chroma
            // "local" treatment, no Ctrl-nav, no object Quick Info (exactly the reported symptom).
            var before = svc.Model!.ReferenceAt(viewOffset);
            log.AppendLine($"[1] before load: symbol={before?.Symbol?.GetType().Name ?? "null"}");
            Assert.True(before?.Symbol is not SchemaObjectSymbol, "view must NOT resolve to a schema object before its category loads");

            // The Views + Procedures categories finish loading — the snapshot now carries them.
            snapshot = AppMetadataSnapshot.Build(
                new[]
                {
                    new MetadataObject("MYVIEW", MetadataObjectKind.View),
                    new MetadataObject("MYPROC", MetadataObjectKind.Procedure),
                },
                emptyCols);

            // Exactly what the ObjectsChanged → NotifyMetadataChanged (coalesced) path ultimately runs.
            svc.RefreshModelWithMetadata();

            var afterView = svc.Model!.ReferenceAt(viewOffset);
            Assert.NotNull(afterView);
            var viewSym = Assert.IsType<SchemaObjectSymbol>(afterView!.Symbol);
            Assert.Equal(SymbolKind.View, viewSym.Kind);
            log.AppendLine($"[2] after load: view resolves as {viewSym.Kind}");

            // A selectable procedure in FROM must resolve the same way after its category loads.
            const string procSql = "select a, b from myproc(:x)";
            editor.Text = procSql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            int procOffset = procSql.IndexOf("myproc", System.StringComparison.Ordinal) + 3;
            var procRef = svc.Model!.ReferenceAt(procOffset);
            Assert.NotNull(procRef);
            var procSym = Assert.IsType<SchemaObjectSymbol>(procRef!.Symbol);
            Assert.Equal(SymbolKind.Procedure, procSym.Kind);
            log.AppendLine($"[3] selectable proc resolves as {procSym.Kind}");

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Sprint 1 (point b) regression pin — the model pipeline warms the columns of the tables the
    // current statement references and rebuilds, so a BARE column resolves WITHOUT the user first
    // typing "table." to trigger the dot warm. Column cache starts EMPTY; the injected warm callback
    // (mirroring MainWindowViewModel.WarmReferencedAsync) loads T's columns; the fire-and-forget pass then
    // rebuilds the model against the now-complete snapshot. Before this fix, `x` stayed unresolved
    // until a dot warmed T — the "everything comes alive only after r." symptom.
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_WarmsReferencedTableColumns_WithoutDot()
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Columns start UNCACHED; the warm callback populates the cache the snapshot reads from.
            var cols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            ISqlMetadataProvider Snap() => AppMetadataSnapshot.Build(new[] { new MetadataObject("T", MetadataObjectKind.Table) }, cols);
            System.Func<System.Collections.Generic.IReadOnlyList<string>, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> warm =
                (tables, ct) =>
                {
                    bool any = false;
                    foreach (var t in tables)
                    {
                        if (!cols.ContainsKey(t)) { cols[t] = new[] { new ColumnSpec("X", "INTEGER") }; any = true; }
                    }
                    return System.Threading.Tasks.Task.FromResult(any);
                };

            using var svc = new EditorLanguageService(editor, Snap, null, warm);
            const string sql = "select x from t";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel(); // builds against the EMPTY column cache, then fires the warm pass

            int xOffset = sql.IndexOf(" x ", System.StringComparison.Ordinal) + 1; // the bare column `x`

            // Let the fire-and-forget warm pass run: it warms T's columns and rebuilds.
            for (var i = 0; i < 10; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }

            var xref = svc.Model!.ReferenceAt(xOffset);
            Assert.NotNull(xref);
            Assert.IsType<ColumnSymbol>(xref!.Symbol); // resolved WITHOUT a dot — the pipeline warmed T
            Assert.True(cols.ContainsKey("T"), "the referenced table's columns were warmed by the pipeline");

            window.Close();
        }, CancellationToken.None);
    }

    // Package 5 diagnostic — does the warm pipeline collect + warm a GENERATOR's detail for a
    // NEXT VALUE FOR reference (a non-table object), and does the description reach Quick Info? This
    // isolates whether Problem 2 is a data-path bug (collection/warm) or a runtime timing issue.
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_WarmsGeneratorDetail_ForNextValueFor()
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var noCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            var detail = new System.Collections.Generic.Dictionary<string, ObjectDetail>(System.StringComparer.OrdinalIgnoreCase);
            var objects = new[] { new MetadataObject("GEN_X", MetadataObjectKind.Generator) };
            ISqlMetadataProvider Snap() => AppMetadataSnapshot.Build(objects, noCols, null, detail);

            var captured = new System.Collections.Generic.List<string>();
            System.Func<System.Collections.Generic.IReadOnlyList<string>, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> warm =
                (names, ct) =>
                {
                    captured.AddRange(names);
                    bool any = false;
                    foreach (var n in names)
                    {
                        if (!detail.ContainsKey(n)) { detail[n] = new ObjectDetail("The FA generator", null, null); any = true; }
                    }
                    return System.Threading.Tasks.Task.FromResult(any);
                };

            using var svc = new EditorLanguageService(editor, Snap, null, warm);
            const string sql = "next value for gen_x";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            for (var i = 0; i < 10; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }

            Assert.Contains("GEN_X", captured); // the generator is collected for warming (not just tables)
            var qi = QuickInfoEngine.GetQuickInfo(svc.Model!, sql.IndexOf("gen_x", System.StringComparison.Ordinal) + 2);
            Assert.NotNull(qi);
            Assert.Equal(SymbolKind.Sequence, qi!.Kind);
            Assert.Equal("The FA generator", qi.Description); // Stage B detail reached Quick Info

            window.Close();
        }, CancellationToken.None);
    }

    // Package 5 closure pin — the warm pipeline warms ROUTINE PARAMETERS for a referenced procedure, so
    // its full signature is in the published model WITHOUT the user typing "(" (Signature Help/Quick
    // Info were the piecemeal, gesture-triggered case). Params start uncached; the warm fills them.
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_WarmsRoutineParameters_ForReferencedProcedure()
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var noCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            var routineParams = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<RoutineParameterMetadata>>(
                System.StringComparer.OrdinalIgnoreCase);
            var objects = new[] { new MetadataObject("MY_PROC", MetadataObjectKind.Procedure) };
            ISqlMetadataProvider Snap() => AppMetadataSnapshot.Build(objects, noCols, routineParams);

            System.Func<System.Collections.Generic.IReadOnlyList<string>, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> warm =
                (names, ct) =>
                {
                    bool any = false;
                    foreach (var n in names)
                    {
                        if (!routineParams.ContainsKey(n))
                        {
                            routineParams[n] = new[] { new RoutineParameterMetadata("ID_K", "INTEGER", ParameterDirection.Input) };
                            any = true;
                        }
                    }
                    return System.Threading.Tasks.Task.FromResult(any);
                };

            using var svc = new EditorLanguageService(editor, Snap, null, warm);
            const string sql = "execute procedure my_proc";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            for (var i = 0; i < 10; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }

            var qi = QuickInfoEngine.GetQuickInfo(svc.Model!, sql.IndexOf("my_proc", System.StringComparison.Ordinal) + 2);
            Assert.NotNull(qi);
            Assert.Equal(SymbolKind.Procedure, qi!.Kind);
            Assert.Contains(qi.Members, m => m.Text.Contains("ID_K")); // params warmed → shown, no "(" needed

            window.Close();
        }, CancellationToken.None);
    }

    // Package 5 root-cause pin — an object whose category loads DURING an in-flight warm must still be
    // warmed. GEN_X is absent from the snapshot when the model is first built (so it isn't collected);
    // it becomes available during the first warm pass (simulating prefetch loading the Generators
    // category mid-warm). The warm loop must re-collect after its rebuild and warm GEN_X's detail —
    // before the fix, the guard dropped the re-warm and GEN_X's description never loaded (the "existing
    // tab / generator shows only basic info" bug).
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_WarmConverges_WhenMetadataGrowsMidWarm()
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var noCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            var detail = new System.Collections.Generic.Dictionary<string, ObjectDetail>(System.StringComparer.OrdinalIgnoreCase);
            // GEN_X is NOT present initially — it "loads" during the first warm.
            var objects = new System.Collections.Generic.List<MetadataObject> { new("T", MetadataObjectKind.Table) };
            ISqlMetadataProvider Snap() => AppMetadataSnapshot.Build(objects, noCols, null, detail);

            int warmCalls = 0;
            System.Func<System.Collections.Generic.IReadOnlyList<string>, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> warm =
                (names, ct) =>
                {
                    warmCalls++;
                    if (warmCalls == 1 && !objects.Exists(o => o.Name == "GEN_X"))
                    {
                        objects.Add(new MetadataObject("GEN_X", MetadataObjectKind.Generator)); // category loads mid-warm
                    }
                    bool any = false;
                    foreach (var n in names)
                    {
                        if (!detail.ContainsKey(n)) { detail[n] = new ObjectDetail($"desc-{n}", null, null); any = true; }
                    }
                    return System.Threading.Tasks.Task.FromResult(any);
                };

            using var svc = new EditorLanguageService(editor, Snap, null, warm);
            const string sql = "select * from t\nnext value for gen_x";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            for (var i = 0; i < 20; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }

            // GEN_X, which appeared mid-warm, was re-collected and its detail warmed → reaches Quick Info.
            var qi = QuickInfoEngine.GetQuickInfo(svc.Model!, sql.IndexOf("gen_x", System.StringComparison.Ordinal) + 2);
            Assert.NotNull(qi);
            Assert.Equal(SymbolKind.Sequence, qi!.Kind);
            Assert.Equal("desc-GEN_X", qi.Description);

            window.Close();
        }, CancellationToken.None);
    }

    // QA Package 1 regression pin — "IntelliSense is dead until I edit after connecting". The model's
    // synchronous deliberate-trigger refresh (EnsureFreshModel, what a Ctrl+Space runs) was gated on the
    // TEXT version only, so metadata that loaded AFTER the model was first built (prefetch on connect)
    // never reached a Ctrl+Space unless a keystroke bumped the text version. The fix: the model also
    // tracks a metadata GENERATION and rebuilds on a deliberate trigger when it moved — no text change.
    // This drives EnsureFreshModel directly (NOT RefreshModelWithMetadata, which always rebuilds) and
    // proves the generation bump alone forces the rebuild.
    [Fact]
    public async System.Threading.Tasks.Task EditorModel_EnsureFresh_RebuildsWhenMetadataGenerationMoves()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 300, Height = 120 };
            var window = new Window { Width = 400, Height = 300, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var emptyCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);

            // Simulate connect ordering: the model is first built BEFORE the Views category loads.
            ISqlMetadataProvider snapshot = AppMetadataSnapshot.Build(Array.Empty<MetadataObject>(), emptyCols);
            int generation = 0; // the host's ObjectsGeneration

            using var svc = new EditorLanguageService(editor, () => snapshot, () => generation);
            const string sql = "select * from myview";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();

            int viewOffset = sql.IndexOf("myview", System.StringComparison.Ordinal) + 3;
            Assert.True(svc.Model!.ReferenceAt(viewOffset)?.Symbol is not SchemaObjectSymbol,
                "view must NOT resolve before its category loads");

            // Views category loads: snapshot grows AND the generation bumps — but the TEXT is unchanged.
            snapshot = AppMetadataSnapshot.Build(new[] { new MetadataObject("MYVIEW", MetadataObjectKind.View) }, emptyCols);
            generation++;

            // The deliberate-trigger path (a Ctrl+Space). Text-version-fresh, but the generation moved,
            // so the model MUST rebuild — this is the exact call that previously no-op'd and left the
            // user with dead IntelliSense until a keystroke.
            svc.EnsureFreshModel();

            var after = svc.Model!.ReferenceAt(viewOffset);
            var viewSym = Assert.IsType<SchemaObjectSymbol>(after!.Symbol);
            Assert.Equal(SymbolKind.View, viewSym.Kind);
            log.AppendLine($"[1] generation bump alone rebuilt the model → view resolves as {viewSym.Kind}");

            // And a second deliberate trigger with NOTHING changed is a genuine no-op (still resolved,
            // no churn) — the model stays fresh once text + generation match.
            svc.EnsureFreshModel();
            Assert.IsType<SchemaObjectSymbol>(svc.Model!.ReferenceAt(viewOffset)!.Symbol);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // The unified Parameter Helper (design §28) across the constructs it must cover: INSERT (cached +
    // warm-then-retry + context-driven lifetime), UPDATE OR INSERT (same as INSERT), and EXECUTE
    // PROCEDURE (routine params warmed on a miss). Drives ParameterHelper.ShowAt directly (the same call
    // both the double-click and the typing triggers make) and asserts the overlay card opens / stays /
    // closes by semantic context.
    [Fact]
    public async System.Threading.Tasks.Task ParameterHelper_InsertUpdateOrInsertProcedure()
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 500, Height = 200 };
            var window = new Window { Width = 600, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.Measure(new Size(500, 200));
            editor.Arrange(new Rect(0, 0, 500, 200));
            Dispatcher.UIThread.RunJobs();

            var emptyCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            var cols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["T"] = new[] { new ColumnSpec("A", "INTEGER"), new ColumnSpec("B", "VARCHAR(10)") },
            };
            ISqlMetadataProvider snap = AppMetadataSnapshot.Build(new[] { new MetadataObject("T", MetadataObjectKind.Table) }, cols);

            // ── (1) INSERT, columns cached → opens; CONTEXT-driven lifetime ──────────────────────────
            using (var svc = new EditorLanguageService(editor, () => snap))
            {
                var helper = ParameterHelper.Attach(editor, () => svc.Model);
                const string sql = "insert into t (a, b) values (1, 2)";
                editor.Text = sql; Dispatcher.UIThread.RunJobs(); svc.EnsureFreshModel();
                int v2 = sql.IndexOf(", 2)", System.StringComparison.Ordinal) + 2; // on the "2"

                Assert.True(helper.ShowAt(v2), "a VALUES value is a parameter site");
                Dispatcher.UIThread.RunJobs();
                Assert.True(helper.IsOpen, "INSERT Parameter Helper opens at a VALUES value");
                editor.CaretOffset = v2 + 1; Dispatcher.UIThread.RunJobs();
                Assert.True(helper.IsOpen, "stays open on a caret jitter inside the same argument");
                editor.CaretOffset = sql.IndexOf("(1", System.StringComparison.Ordinal) + 1; Dispatcher.UIThread.RunJobs();
                Assert.True(helper.IsOpen, "stays open when moving to another argument of the same INSERT");
                editor.CaretOffset = 0; Dispatcher.UIThread.RunJobs();
                Assert.False(helper.IsOpen, "closes when the caret leaves the INSERT context");
                helper.Detach();
            }

            // ── (2) UPDATE OR INSERT → behaves exactly like INSERT ───────────────────────────────────
            using (var svc = new EditorLanguageService(editor, () => snap))
            {
                var helper = ParameterHelper.Attach(editor, () => svc.Model);
                const string sql = "update or insert into t (a, b) values (1, 2)";
                editor.Text = sql; Dispatcher.UIThread.RunJobs(); svc.EnsureFreshModel();
                int v2 = sql.IndexOf(", 2)", System.StringComparison.Ordinal) + 2;
                Assert.True(helper.ShowAt(v2), "UPDATE OR INSERT is a parameter site");
                Dispatcher.UIThread.RunJobs();
                Assert.True(helper.IsOpen, "UPDATE OR INSERT Parameter Helper opens like INSERT");
                helper.Detach();
            }

            // ── (3) EXECUTE PROCEDURE → routine params, WARMED on a miss ─────────────────────────────
            var procParams = new[]
            {
                new RoutineParameterMetadata("A", "INTEGER", ParameterDirection.Input),
                new RoutineParameterMetadata("B", "VARCHAR(10)", ParameterDirection.Input),
            };
            var warmedProcs = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<RoutineParameterMetadata>>(
                System.StringComparer.OrdinalIgnoreCase);
            ISqlMetadataProvider ProcSnap() => AppMetadataSnapshot.Build(
                new[] { new MetadataObject("P", MetadataObjectKind.Procedure) }, emptyCols, warmedProcs);

            using (var svc = new EditorLanguageService(editor, ProcSnap))
            {
                var helper = ParameterHelper.Attach(editor, () => svc.Model, (label, kind) =>
                {
                    warmedProcs[label] = procParams;      // simulate the App warming the routine's params
                    svc.RefreshModelWithMetadata();
                    return System.Threading.Tasks.Task.FromResult(svc.Model);
                });
                const string sql = "execute procedure p(1, 2)";
                editor.Text = sql; Dispatcher.UIThread.RunJobs(); svc.EnsureFreshModel();
                int arg = sql.IndexOf("p(", System.StringComparison.Ordinal) + 2; // on the "1"

                // Params not cached yet → the engine reports a known routine with 0 params; the helper
                // warms them, rebuilds, and shows the list.
                Assert.True(helper.ShowAt(arg), "an EXECUTE PROCEDURE argument is a parameter site");
                for (var i = 0; i < 5; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }
                Assert.True(helper.IsOpen, "EXECUTE PROCEDURE Parameter Helper opens after warming routine params");
                helper.Detach();
            }

            window.Close();
        }, CancellationToken.None);
    }

    // QA Package 2 diagnostic — the UPDATE OR INSERT / INSERT COLUMN-warm path (uncached columns →
    // warm → retry), which the section-2 test above does NOT cover (it pre-caches columns). This is the
    // exact path a first double-click on a fresh editor takes. Drives ShowAt with EMPTY columns, a warm
    // callback that caches them + rebuilds (what the App's WarmForSignatureAndRebuildAsync does), and
    // asserts the card opens after the warm — for BOTH INSERT and UPDATE OR INSERT, proving the two are
    // symmetric on the warm path too (or catching the asymmetry if there is one).
    [Theory]
    [InlineData("insert into t (a, b) values (1, 2)")]
    [InlineData("update or insert into t (a, b) values (1, 2)")]
    public async System.Threading.Tasks.Task ParameterHelper_ColumnWarm_OpensAfterWarm(string sql)
    {
        var session = SharedSession;

        await session.Dispatch(async () =>
        {
            var editor = new TextEditor { Width = 500, Height = 200 };
            var window = new Window { Width = 600, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            editor.Measure(new Size(500, 200));
            editor.Arrange(new Rect(0, 0, 500, 200));
            Dispatcher.UIThread.RunJobs();

            // Columns start UNCACHED — the warm callback fills them (mirrors EnsureColumnsAsync).
            var warmedCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            ISqlMetadataProvider Snap() => AppMetadataSnapshot.Build(
                new[] { new MetadataObject("T", MetadataObjectKind.Table) }, warmedCols);

            using var svc = new EditorLanguageService(editor, Snap);
            var helper = ParameterHelper.Attach(editor, () => svc.Model, (label, kind) =>
            {
                warmedCols[label] = new[] { new ColumnSpec("A", "INTEGER"), new ColumnSpec("B", "VARCHAR(10)") };
                svc.RefreshModelWithMetadata();
                return System.Threading.Tasks.Task.FromResult(svc.Model);
            });

            editor.Text = sql; Dispatcher.UIThread.RunJobs(); svc.EnsureFreshModel();
            int v2 = sql.IndexOf(", 2)", System.StringComparison.Ordinal) + 2; // on the "2"

            // First activation, columns not cached → the engine returns null; the helper must warm the
            // target table's columns, rebuild, and open — no second trigger needed.
            Assert.True(helper.ShowAt(v2), "a VALUES value is a parameter site even before columns are cached");
            for (var i = 0; i < 5; i++) { await System.Threading.Tasks.Task.Yield(); Dispatcher.UIThread.RunJobs(); }
            Assert.True(helper.IsOpen, "Parameter Helper opens after warming the target columns (first activation)");
            helper.Detach();
            window.Close();
        }, CancellationToken.None);
    }

    // QA Fix Sprint — PROOF for the "view/proc coloured like a plain identifier" report. Builds the
    // model WITH the view in metadata (so it resolves — both a SchemaObject and an implicit
    // TableReference occurrence overlap on the bare name), attaches the REAL SemanticHighlighter, and
    // asserts the brush it paints on the view name is the per-kind OBJECT brush (IconColor_View), not
    // the low-chroma "local" brush. If this passes, the highlighter/classifier/brush-mapping are
    // correct and a live "no colour" is a model-resolution (metadata-not-loaded) issue, NOT a paint
    // bug — the exact distinction the QA sprint asked to prove before touching the binder.
    [Fact]
    public async System.Threading.Tasks.Task SemanticHighlighter_BareFromView_PaintsObjectColour_NotLocal()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 120 };
            var window = new Window { Width = 500, Height = 240, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var emptyCols = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                System.StringComparer.OrdinalIgnoreCase);
            ISqlMetadataProvider snap = AppMetadataSnapshot.Build(
                new[]
                {
                    new MetadataObject("MYVIEW", MetadataObjectKind.View),
                    new MetadataObject("MYPROC", MetadataObjectKind.Procedure),
                },
                emptyCols);

            using var svc = new EditorLanguageService(editor, () => snap);
            var hl = SemanticHighlighter.Attach(editor, () => svc.Model); // internal test overload

            const string sql = "select * from myview";
            editor.Text = sql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();

            var theme = editor.ActualThemeVariant;
            IBrush Res(string key)
            {
                Assert.True(Application.Current!.Resources.TryGetResource(key, theme, out var v) && v is IBrush,
                    $"brush '{key}' must resolve");
                return (IBrush)v!;
            }
            var viewBrush = Res("IconColor_View");
            var localBrush = Res("EditorLocalBrush");

            int viewOffset = sql.IndexOf("myview", System.StringComparison.Ordinal) + 2;
            var painted = hl.PaintedBrushAt(viewOffset);
            log.AppendLine($"[1] view painted brush == IconColor_View: {ReferenceEquals(painted, viewBrush)}; ==Local: {ReferenceEquals(painted, localBrush)}");
            Assert.Same(viewBrush, painted);
            Assert.NotSame(localBrush, painted);

            // Selectable procedure in FROM → its own object colour too.
            const string procSql = "select a from myproc(:x)";
            editor.Text = procSql;
            Dispatcher.UIThread.RunJobs();
            svc.EnsureFreshModel();
            int procOffset = procSql.IndexOf("myproc", System.StringComparison.Ordinal) + 2;
            var procPainted = hl.PaintedBrushAt(procOffset);
            log.AppendLine($"[2] proc painted brush == IconColor_Procedure: {ReferenceEquals(procPainted, Res("IconColor_Procedure"))}");
            Assert.Same(Res("IconColor_Procedure"), procPainted);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // Completion Matching Philosophy — the objective pin for "Core owns filtering, the UI shows exactly
    // that". It reproduces the user's 2026-07-17 report verbatim: typing "cont" listed every object merely
    // CONTAINING the text (XXX_PS_CONTRACTORMAP, GEN_XXX_PS_CONTRACTORMAP, MON$CONTEXT_VARIABLES, the
    // FK_/PK_ indices) beside the keywords that genuinely start with it.
    //
    // This has to be a headless probe, not a Core unit test. CompletionMatcher's rule was already unit-
    // tested and correct while the bug was live — nothing called it, and the real filter was AvaloniaEdit's
    // substring-admitting GetMatchQuality. So the only assertion that can catch the regression class is one
    // made against the REAL CompletionWindow: what does the rendered list actually hold.
    [Fact]
    public async System.Threading.Tasks.Task Completion_PrefixFirst_ListsOnlyStartsWithMatches()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            var editor = new TextEditor { Width = 400, Height = 200 };
            var window = new Window { Width = 500, Height = 320, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The reported catalog: names that merely CONTAIN "cont", none of which start with it.
            var objects = new[]
            {
                new MetadataObject("XXX_PS_CONTRACTORMAP", MetadataObjectKind.Table),
                new MetadataObject("XXX_PS_CONTRACTORADDRESSMAP", MetadataObjectKind.Table),
                new MetadataObject("GEN_XXX_PS_CONTRACTORMAP", MetadataObjectKind.Generator),
                new MetadataObject("GEN_XXX_PS_CONTRACTORADDRESSMAP", MetadataObjectKind.Generator),
                new MetadataObject("PK_XXX_PS_CONTRACTORMAP", MetadataObjectKind.Index),
                new MetadataObject("FK_XXX_PS_CONTRACTORMAP_3", MetadataObjectKind.Index),
                new MetadataObject("MON$CONTEXT_VARIABLES", MetadataObjectKind.Table),
                // A name that DOES start with the prefix — proves the list isn't just empty.
                new MetadataObject("CONTRACT_LINES", MetadataObjectKind.Table),
            };
            var snapshot = AppMetadataSnapshot.Build(
                objects,
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<ColumnSpec>>(
                    System.StringComparer.OrdinalIgnoreCase));

            var controller = new SqlCompletionController(editor, () => snapshot);

            // Types like the Language Completion probe above: a real 0 → end caret change, so
            // Caret.PositionChanged fires exactly as it does while typing (gotcha #94 — do not inject keys).
            void Type(string text)
            {
                editor.SelectionLength = 0;
                editor.Document.Text = string.Empty;
                editor.CaretOffset = 0;
                Dispatcher.UIThread.RunJobs();
                editor.Document.Text = text;
                editor.CaretOffset = text.Length;
                Dispatcher.UIThread.RunJobs();
            }

            // Ctrl+Space at the caret — the deliberate trigger, which bypasses the idle auto-popup timer.
            void CtrlSpace()
            {
                editor.TextArea.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Space,
                    KeyModifiers = KeyModifiers.Control,
                });
                Dispatcher.UIThread.RunJobs();
            }

            Type("cont");
            CtrlSpace();

            var visible = controller.VisibleItemsForTest;
            log.AppendLine($"[1] 'cont' → {visible.Count} rows: {string.Join(", ", visible)}");

            Assert.NotEmpty(visible);
            // The heart of the report: NOTHING that merely contains the text.
            Assert.All(visible, t => Assert.StartsWith("cont", t, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("XXX_PS_CONTRACTORMAP", visible);
            Assert.DoesNotContain("GEN_XXX_PS_CONTRACTORMAP", visible);
            Assert.DoesNotContain("MON$CONTEXT_VARIABLES", visible);
            Assert.DoesNotContain("FK_XXX_PS_CONTRACTORMAP_3", visible);
            // The keywords the user explicitly wants kept, plus the object that genuinely starts with it.
            Assert.Contains("CONTAINING", visible);
            Assert.Contains("CONTINUE", visible);
            Assert.Contains("CONTRACT_LINES", visible);

            // Every row the user can SEE — not just the collection we handed over. Both must agree: the
            // list going stale on screen while its data reads correctly is precisely the 2026-07-17
            // regression (CompletionData is a plain List and broadcasts no change, so a mutation after
            // Show() updates the data and nothing else).
            Assert.Equal(visible, controller.RenderedRowsForTest);

            // Now the REFRESH path: keep typing INTO THE OPEN WINDOW, one character at a time, exactly as
            // a user does. `Type` above cannot test this — it clears the document, which closes the window,
            // so each step there was a fresh open. That gap is why the stale-list bug shipped.
            editor.TextArea.PerformTextInput("i"); // "cont" → "conti"
            for (var i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
            var narrowed = controller.RenderedRowsForTest;
            log.AppendLine($"[2] typed 'i' → 'conti' → {narrowed.Count} rendered rows: {string.Join(", ", narrowed)}");
            Assert.Equal(controller.VisibleItemsForTest, narrowed);
            Assert.All(narrowed, t => Assert.StartsWith("conti", t, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("CONTINUE", narrowed);
            Assert.DoesNotContain("CONTAINING", narrowed);

            // A backspace must WIDEN it again — the reason the session keeps the unfiltered candidate set
            // rather than letting the engine return an already-filtered list.
            editor.Document.Remove(editor.CaretOffset - 1, 1); // "conti" → "cont"
            for (var i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
            var widened = controller.RenderedRowsForTest;
            log.AppendLine($"[3] backspace → 'cont' → {widened.Count} rendered rows: {string.Join(", ", widened)}");
            Assert.Contains("CONTAINING", widened);
            Assert.Contains("CONTINUE", widened);

            // Zero StartsWith matches → no list at all. Never a Contains fallback, which is exactly what
            // would resurrect the report: "tractor" must not surface XXX_PS_CONTRACTORMAP.
            Type("tractor");
            CtrlSpace();
            log.AppendLine($"[3] 'tractor' → popup open = {controller.IsPopupOpen}, rows = {controller.VisibleItemsForTest.Count}");
            Assert.Empty(controller.VisibleItemsForTest);

            // Ctrl+Space with no prefix still offers everything in scope (the rule is prefix-first, not
            // prefix-required).
            Type("");
            CtrlSpace();
            var all = controller.VisibleItemsForTest;
            log.AppendLine($"[4] no prefix → {all.Count} rows");
            Assert.Contains("XXX_PS_CONTRACTORMAP", all);
            Assert.Contains("CONTAINING", all);

            controller.Detach();
            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // P2c — the matched fragment of a row is picked out in the match colour (the IBExpert cue). Pins the
    // SPLIT and the colour source: the run boundary must follow CompletionMatcher's StartsWith ruling, and
    // the colour must come from the theme token (a hardcoded brush would break the Light/Dark rule and no
    // build could catch it). Needs the headless session — it resolves a real brush from App resources.
    [Fact]
    public async System.Threading.Tasks.Task CompletionRow_HighlightsMatchedPrefix()
    {
        var session = SharedSession;
        var log = new StringBuilder();

        await session.Dispatch(() =>
        {
            static TextBlock NameBlock(SqlCompletionData d)
                => ((StackPanel)d.Content).Children.OfType<TextBlock>().First();

            var item = new EmberTern.Core.Sql.Language.Completion.CompletionItem(
                "CONTAINING", "CONTAINING", EmberTern.Core.Sql.Language.Completion.CompletionItemKind.Keyword, 1.0);

            // Typed "con" → "con" in the match colour, "TAINING" in the default foreground.
            var withPrefix = NameBlock(SqlCompletionData.FromItem(item, null, "con"));
            var runs = withPrefix.Inlines!.OfType<Run>().ToList();
            log.AppendLine($"[1] runs = {string.Join(" | ", runs.Select(r => $"'{r.Text}' fg={r.Foreground}"))}");
            Assert.Equal(2, runs.Count);
            Assert.Equal("CON", runs[0].Text);       // case-insensitive match, catalog casing preserved
            Assert.Equal("TAINING", runs[1].Text);

            // The highlight brush IS the theme token — not a look-alike literal — and only the matched run
            // carries it.
            var expected = Application.Current!.Resources.TryGetResource(
                "CompletionMatchBrush", Application.Current.ActualThemeVariant, out var brush) ? brush : null;
            Assert.NotNull(expected);
            Assert.Same(expected, runs[0].Foreground);
            Assert.NotSame(expected, runs[1].Foreground);

            // The token must exist in BOTH dictionaries (styling rule 3) — a one-theme token is a bug.
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                Assert.True(
                    Application.Current.Resources.TryGetResource("CompletionMatchBrush", variant, out var v) && v is IBrush,
                    $"CompletionMatchBrush must resolve in the {variant} dictionary");
            }

            // No prefix (Ctrl+Space on whitespace) → plain text, no meaningless colour on every row.
            var noPrefix = NameBlock(SqlCompletionData.FromItem(item, null, ""));
            log.AppendLine($"[2] no prefix → Text='{noPrefix.Text}' inlines={noPrefix.Inlines?.Count ?? 0}");
            Assert.Equal("CONTAINING", noPrefix.Text);
            Assert.True(noPrefix.Inlines is null || noPrefix.Inlines.Count == 0);

            // A fully-typed name is all match — one run, no empty tail.
            var wholeRuns = NameBlock(SqlCompletionData.FromItem(item, null, "containing")).Inlines!.OfType<Run>().ToList();
            Assert.Single(wholeRuns);
            Assert.Equal("CONTAINING", wholeRuns[0].Text);
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ── Data Import — etap I12, the UI audit made REPRODUCIBLE ──────────────────────────────────────────
    //
    // The audit's mechanical half (no hard-coded colours, no local brushes, no StaticResource on a brush,
    // no local <Style> blocks) is a property of the XAML and was checked by reading it. THIS half cannot be
    // read: a {DynamicResource} key is resolved at runtime, per theme, so a token that exists in Dark and
    // was forgotten in Light compiles, renders in the palette the developer happens to use, and paints
    // nothing in the other (gotcha #250 is the same failure one level down).
    //
    // So the list of tokens the module paints with is pinned here rather than re-grepped by hand at some
    // future review. A token added to the surface without a Light counterpart fails this test.
    [Fact]
    public async System.Threading.Tasks.Task DataImportSurface_EveryThemeToken_ResolvesInBothPalettes()
    {
        var session = SharedSession;

        // Every {DynamicResource} key used by DataImportTabView.axaml and TextPromptDialog.axaml.
        var tokens = new[]
        {
            // ⚠ Zaktualizowane 2026-08-03 przy domknięciu języka kolorów: moduł nie maluje już
            // `AccentIconBrush` ani `SuccessIconBrush` (wskaż plik → `AccentBrush`, odśwież i waliduj →
            // neutralnie), za to Commit/Rollback dostały własne tokeny ról. ⭐ Lista kluczy jest tym
            // samym rodzajem zapisu co filtr partycji z §18.1.6: starzeje się CICHO, bo nazwa, której
            // nikt nie używa, nadal się rozwiązuje i test przechodzi.
            "AccentBrush", "BackgroundBrush", "BorderBrush", "ChromeStrongBrush", "CommitButtonBrush",
            "DangerIconBrush", "ErrorBrush", "ForegroundBrush", "OnAccentBrush", "PanelBrush",
            "RollbackButtonBrush", "SubtleForegroundBrush", "WarningBrush",
        };

        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;

            foreach (var token in tokens)
            {
                foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
                {
                    Assert.True(
                        app.Resources.TryGetResource(token, theme, out var brush) && brush is Avalonia.Media.IBrush,
                        $"Data Import paints with '{token}', which does not resolve in {theme}");
                }
            }
        }, CancellationToken.None);
    }
    // ══════ Context-menu presentation (Keyboard Manager etap 5) ══════════════════════════════════ 

    // ⭐ The one that matters most, because its failure mode is INVISIBLE: {app:MenuIcon} returns null for an
    // unknown geometry key (deliberately — a typo must not take down a menu), so a mistyped key yields a
    // menu item with no icon and nothing else wrong. Nobody notices one missing glyph among a hundred.
    // So: every key any view actually passes to {app:MenuIcon} must resolve to a real geometry.
    [Fact]
    public async System.Threading.Tasks.Task EveryMenuIconKeyUsedInAViewResolvesToAGeometry()
    {
        var keys = MenuIconKeysUsedInViews();
        Assert.NotEmpty(keys); // the scan itself must not silently find nothing

        var unresolved = new List<string>();
        await SharedSession.Dispatch(() =>
        {
            foreach (var key in keys)
            {
                if (new MenuIconExtension(key).ProvideValue() is not SvgIcon { Data: not null })
                {
                    unresolved.Add(key);
                }
            }
        }, CancellationToken.None);

        _out.WriteLine($"{keys.Count} distinct MenuIcon keys used across the views:"
                       + Environment.NewLine + string.Join(", ", keys));

        Assert.True(unresolved.Count == 0,
            "These geometry keys are passed to {app:MenuIcon} but resolve to nothing, so those menu items "
            + "show no icon and nothing fails: " + string.Join(", ", unresolved));
    }

    [Fact]
    public async System.Threading.Tasks.Task MenuIcon_InheritsForegroundByDefault_AndTakesAThemeBrushWhenAsked()
    {
        await SharedSession.Dispatch(() =>
        {
            // Default: no local Foreground, so the icon follows the menu item's own colour — which is what
            // makes a menu read as one calm block and gives selected/disabled states for free.
            var plain = (SvgIcon)new MenuIconExtension("Icon.Copy").ProvideValue()!;
            Assert.False(plain.IsSet(TemplatedControl.ForegroundProperty));

            // The destructive exception, bound as a DYNAMIC resource so it still re-colours on theme toggle.
            var danger = (SvgIcon)new MenuIconExtension("Icon.Trash") { Brush = "DangerIconBrush" }
                .ProvideValue()!;
            var host = new ContentControl { Content = danger };
            var window = new Window { Width = 100, Height = 100, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(danger.Foreground);
            Assert.IsAssignableFrom<IBrush>(danger.Foreground);
            window.Close();
        }, CancellationToken.None);
    }

    // The gesture column reads the catalog, so a re-bind reaches every menu offering the command. If this
    // ever returned null for a declared command, menus would quietly stop showing shortcuts.
    [Fact]
    public void CommandGesture_ComesFromTheCatalog()
    {
        Assert.Equal(new KeyGesture(Key.F8), new CommandGestureExtension(CommandId.DeleteObject).ProvideValue());
        Assert.Equal(new KeyGesture(Key.F3), new CommandGestureExtension(CommandId.NewObject).ProvideValue());
        Assert.Equal(new KeyGesture(Key.F4), new CommandGestureExtension(CommandId.RefreshMetadata).ProvideValue());

        // A command with no declared gesture leaves the column empty rather than inventing one.
        Assert.Null(new CommandGestureExtension((CommandId)(-1)).ProvideValue());
    }

    // The shared style IS the app's menu control. If it stopped applying, every menu would silently revert to
    // FluentTheme's 27px rows, 14px type and SystemAccentColor hover — the exact look this etap removed.
    [Fact]
    public async System.Threading.Tasks.Task TheSharedStyle_AppliesToEveryContextMenuWithoutOptIn()
    {
        var log = new StringBuilder();

        await SharedSession.Dispatch(() =>
        {
            var item = new MenuItem
            {
                Header = "Delete",
                Icon = new MenuIconExtension("Icon.Trash").ProvideValue(),
                InputGesture = new CommandGestureExtension(CommandId.DeleteObject).ProvideValue(),
            };
            var menu = new ContextMenu { Items = { item } };
            var host = new Border { Width = 120, Height = 30, ContextMenu = menu };
            var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { host } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            menu.Open(host);
            Dispatcher.UIThread.RunJobs();

            log.AppendLine($"menu: fontSize={menu.FontSize} padding={menu.Padding} border={menu.BorderThickness}");
            log.AppendLine($"item: fontSize={item.FontSize} padding={item.Padding} "
                           + $"minHeight={item.MinHeight} height={item.Bounds.Height}");

            // Density — the whole point of the etap's typography brief.
            Assert.Equal(12d, item.FontSize);
            Assert.Equal(22d, item.MinHeight);
            Assert.Equal(new Avalonia.Thickness(10, 3), item.Padding);
            Assert.Equal(12d, menu.FontSize);

            // The chrome comes from the shared style, so a host never has to (and must never) set it.
            Assert.Equal(new Avalonia.Thickness(1), menu.BorderThickness);
            Assert.NotNull(menu.Background);

            // The gesture really reached the template's own column, not a hand-built TextBlock.
            var gesture = item.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "PART_InputGestureText");
            Assert.NotNull(gesture);
            Assert.Equal("F8", gesture!.Text);
            log.AppendLine($"gesture column text = \"{gesture.Text}\", "
                           + $"fontSize={gesture.FontSize}, foreground set={gesture.Foreground is not null}");
            Assert.Equal(11d, gesture.FontSize);

            // …and the icon reached the icon column.
            var icon = item.GetVisualDescendants().OfType<SvgIcon>().FirstOrDefault();
            Assert.NotNull(icon);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ── Application Menu (hamburger-navigation etap 2) ──────────────────────────────────────────────
    // The MEASUREMENT the design made a precondition (§2.2): whether a plain ContextMenu hosted on a
    // toolbar Button really is the whole answer, so no MenuFlyoutPresenter chrome variant has to exist.
    // It also pins the two things a later edit could silently undo: that the hamburger is the FIRST button
    // of the action zone with NO separator between it and the sidebar toggle (§6, ratified), and that
    // Settings ships disabled-but-present rather than absent.
    //
    // ⚠ What this canNOT measure, and is therefore owed to visual QA: where the menu actually lands on
    // screen. Headless has no real popup surface, so Placement is asserted as the declared value only.
    [Fact]
    public async System.Threading.Tasks.Task ApplicationMenu_IsTheFirstToolbarButton_AndReusesTheSharedMenuChrome()
    {
        var log = new StringBuilder();

        await SharedSession.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "embertern-appmenu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var store = new ConnectionProfileStore(tempDir);
            using var service = new FirebirdConnectionService();
            var vm = new MainWindowViewModel(store, service);
            vm.ReloadConnections();

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Name == "AppMenuButton");
            var menu = Assert.IsType<ContextMenu>(button.ContextMenu);

            // [1] Placement in the toolbar. The hamburger's parent panel holds the whole action zone, so
            // "first button" and "nothing between it and the next one" are both readable from the children.
            var zone = Assert.IsType<StackPanel>(button.Parent);
            var buttons = zone.Children.OfType<Button>().ToList();
            Assert.Same(button, buttons[0]);

            int hamburgerAt = zone.Children.IndexOf(button);
            var next = zone.Children[hamburgerAt + 1];
            log.AppendLine($"action zone: hamburger at index {hamburgerAt} of {zone.Children.Count}, "
                           + $"next sibling = {next.GetType().Name}");

            // ⭐ The ratified rule: no separator of its own. The element right after the hamburger is the
            // sidebar toggle Button — a Border here would be the fence the user rejected.
            var sidebarToggle = Assert.IsType<Button>(next);
            Assert.Same(buttons[1], sidebarToggle);
            Assert.Equal(UiStrings.SidebarToggleTooltip, ToolTip.GetTip(sidebarToggle));

            // [2] Left-click opens it — Avalonia only opens a ContextMenu on right-click by itself, so the
            // button's own handler is what a menu button needs. A second click closes rather than re-opens.
            Assert.False(menu.IsOpen);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen, "left-clicking the hamburger opens the application menu");

            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(menu.IsOpen, "clicking the hamburger again closes the menu");

            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // [3] ⭐ The measurement the host decision rested on: the shared ContextMenu + MenuItem style set
            // applies with NOTHING added for this menu. Same values TheSharedStyle_… pins for every other
            // menu — so a MenuFlyoutPresenter variant would have been chrome nobody needed.
            Assert.Equal(new Avalonia.Thickness(1), menu.BorderThickness);
            Assert.Equal(12d, menu.FontSize);
            Assert.NotNull(menu.Background);
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, menu.Placement);

            var rows = menu.Items.OfType<MenuItem>().ToList();
            foreach (var row in rows)
            {
                Assert.Equal(22d, row.MinHeight);
                Assert.Equal(12d, row.FontSize);
                // Every row carries a mark from the existing icon system, via {app:MenuIcon}.
                Assert.NotNull(row.Icon);
            }

            // [4] The rows themselves. ⭐ Settings shipped present-but-DISABLED while the window did not exist
            // (a row never ships ahead of what it opens); Settings Center etap 3 built the window, so the same
            // etap enabled the row and removed the "Not available yet" tooltip. A row that is enabled and still
            // says it is unavailable would be the worse of the two states.
            var settings = rows.Single(r => Equals(r.Header, UiStrings.AppMenuSettings));
            Assert.True(settings.IsEnabled);
            Assert.Null(ToolTip.GetTip(settings));

            var shortcuts = rows.Single(r => Equals(r.Header, UiStrings.AppMenuKeyboardShortcuts));
            Assert.True(shortcuts.IsEnabled);

            var about = rows.Single(r => Equals(r.Header, UiStrings.AppMenuAbout));
            Assert.True(about.IsEnabled);

            var exit = rows.Single(r => Equals(r.Header, UiStrings.AppMenuExit));
            Assert.True(exit.IsEnabled);

            // Real separators between the three groups, styled by the menu style set (not stray Borders).
            // ⭐ A row never ships ahead of what it opens, so the count grows one etap at a time: etap 2 had
            // Settings + Exit, etap 3 adds About WITH its window, and Keyboard Shortcuts follows in etap 5.
            Assert.Equal(2, menu.Items.OfType<Separator>().Count());

            // [5] ⚠ No gesture column anywhere in this menu, and that is deliberate: Exit's key is Alt+F4,
            // which EmberTern does not own, so showing it would be a hand-typed gesture (gotcha #284).
            foreach (var row in rows)
            {
                Assert.Null(row.InputGesture);
            }

            log.AppendLine($"menu: rows={rows.Count} separators={menu.Items.OfType<Separator>().Count()} "
                           + $"placement={menu.Placement} fontSize={menu.FontSize} border={menu.BorderThickness}");

            // [6] ⭐ Optical size AND sub-pixel phase — two QA rounds' worth of geometry, in one assertion.
            //
            // The SvgIcon ControlTheme scales a FIXED 24×24 Canvas, not the path's ink, so every icon renders
            // at the same 24→16 scale (×2/3) and a geometry filling less of the box simply looks smaller:
            // verbatim Lucide `menu` is 18×14 against PanelLeft's 20×20 (QA round 1).
            //
            // The same ×2/3 scale then decides the ANTI-ALIASING: a rule at y has its top edge at 2(y−1)/3,
            // and rules whose fractional edges differ are drawn differently no matter how symmetric they look
            // — at y4/12/20 the middle rule spreads over two 67% pixel rows and reads thicker while the outer
            // two stay crisp (QA round 2). Equal phases require the spacing to be a multiple of 3.
            //
            // ⚠⚠ QA round 3 then rejected the obvious-looking invariant: this assertion USED to demand that
            // the hamburger's ink box EQUAL the neighbour's, and that is what made it look too big. Equal
            // boxes are not equal weight — three full-width rules are far denser than a thin rectangle
            // outline, so at the same extent the hamburger dominates. **The target is optical, not
            // geometric**, and a dense glyph needs a SMALLER box to look the same size. So the bound is a
            // RANGE, not an equality: big enough not to look lost (round 1), strictly smaller than a
            // rectangle outline (round 3). The exact value inside that range is an eye's decision, and it was
            // taken from a side-by-side sheet, not from arithmetic.
            static (Avalonia.Size Ink, Avalonia.Point Centre) Box(Button host)
            {
                var data = host.GetVisualDescendants().OfType<SvgIcon>().First().Data!;
                var b = data.Bounds;
                // ±1 on every side: half of the theme's 2px stroke.
                return (new Avalonia.Size(b.Width + 2, b.Height + 2), b.Center);
            }

            var hamburger = Box(button);
            var neighbour = Box(sidebarToggle);
            log.AppendLine($"ink box: hamburger {hamburger.Ink} @ {hamburger.Centre} "
                           + $"vs sidebar toggle {neighbour.Ink} @ {neighbour.Centre}");

            Assert.True(hamburger.Ink.Height < neighbour.Ink.Height,
                $"a full-width three-rule glyph at the neighbour's own extent reads bigger than it: "
                + $"{hamburger.Ink} vs {neighbour.Ink}");
            Assert.True(hamburger.Ink.Height >= neighbour.Ink.Height - 4,
                $"too small to sit level with the bar: {hamburger.Ink} vs {neighbour.Ink}");
            Assert.True(hamburger.Ink.Width <= neighbour.Ink.Width && hamburger.Ink.Width >= neighbour.Ink.Width - 3,
                $"width out of range: {hamburger.Ink} vs {neighbour.Ink}");

            // Centred in the 24×24 grid, so the glyph is symmetric in both axes by construction.
            Assert.Equal(new Avalonia.Point(12, 12), hamburger.Centre);

            menu.Close();
            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // The About window shows the version the BUILD declares, and it gets there through the real bindings.
    // AppInfoTests already proves AppInfo agrees with Directory.Build.props and that no literal exists in
    // src/; this closes the last link — that the window actually renders it — because a correct AppInfo behind
    // an unbound TextBlock would satisfy every other test in the sprint.
    [Fact]
    public async System.Threading.Tasks.Task AboutWindow_ShowsTheAssemblyVersionAndIdentity()
    {
        var log = new StringBuilder();

        await SharedSession.Dispatch(() =>
        {
            var window = new AboutWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty).ToArray();
            log.AppendLine("about window text: " + string.Join(" | ", texts));

            Assert.Contains(AppInfo.Product, texts);
            Assert.Contains(AppInfo.Copyright, texts);

            // The version reaches the surface, and carries its label rather than sitting there as a bare number.
            var version = Assert.Single(texts, t => t.Contains(AppInfo.Version, StringComparison.Ordinal));
            Assert.NotEqual(AppInfo.Version, version);

            // ⚠ The author line is LABELLED — the bare name read as unsigned text, and it recurs in the
            // copyright below, so the label is what makes that repetition read as authorship.
            var author = Assert.Single(texts, t => t.StartsWith("Created by", StringComparison.Ordinal));
            Assert.Contains(AppInfo.Author, author, StringComparison.Ordinal);
            Assert.DoesNotContain(AppInfo.Author, texts);

            // The release date, under the version — from <ReleaseDate>, never typed into the view.
            Assert.NotNull(AppInfo.ReleaseDate);
            Assert.Single(texts, t => t.StartsWith("Released", StringComparison.Ordinal));

            // The brand mark is the subject of the window, so it is present and it is the dominant element.
            var logo = Assert.Single(window.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(logo.Source);
            Assert.True(logo.Width >= 96, $"the logo leads this window; it is {logo.Width}px");

            // A product window, not a diagnostic one: nothing about the runtime, the OS or the libraries.
            // ⚠ Checked over the TEXT BLOCKS only, not the whole window — the footer's "Third-party notices"
            // button is a way to REACH the component list, which is the opposite of putting it on this face.
            foreach (var banned in new[] { ".NET", "Avalonia", "Windows", "Firebird", "x64" })
            {
                Assert.DoesNotContain(banned, string.Join(" ", texts), StringComparison.OrdinalIgnoreCase);
            }

            // The notices are reachable from here, and that button is the whole of the licence surface: no
            // library names on the face (§9.6 — nothing requires them there).
            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Contains(buttons, b => Equals(b.Content, UiStrings.AboutThirdPartyNotices));
            Assert.Contains(buttons, b => Equals(b.Content, UiStrings.AboutClose));

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ⭐ The Keyboard Shortcuts window, and the MEASUREMENT the design made a precondition (§8.5.4): the
    // canonical order must come back after a user sort is cleared, and whether this grid even offers a
    // "cleared" third state was an open question rather than something to assume.
    [Fact]
    public async System.Threading.Tasks.Task KeyboardShortcutsWindow_SortsByColumn_AndReturnsToTheCanonicalOrder()
    {
        var log = new StringBuilder();

        await SharedSession.Dispatch(() =>
        {
            var window = new KeyboardShortcutsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
            var reset = window.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, UiStrings.KeyboardShortcutsResetOrder));

            // ⚠ Read through the grid's own selection, not the visual rows: DataGridRow.Index is the index in
            // the underlying items, so with recycled rows it says nothing about DISPLAY order. Selecting
            // display position 0 and reading SelectedItem asks the grid what it is actually showing first.
            string FirstCommand()
            {
                grid.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                return ((KeyboardShortcutRowViewModel)grid.SelectedItem!).Command;
            }

            // [1] First open is canonical: Global scope leads, alphabetically inside it.
            var canonicalFirst = FirstCommand();
            log.AppendLine($"first open → {canonicalFirst}");
            Assert.Equal(UiStrings.CommandTitleCloseTab, canonicalFirst);

            // [2] ⭐ A user sort — and this is the assertion that caught a real defect. Column sorting did
            // NOTHING until every column got an explicit SortMemberPath: the grid derives one from the column's
            // Binding, and this project compiles bindings by default, which leaves the grid without a usable
            // path. Clickable headers that sort nothing would have reached QA.
            var commandColumn = grid.Columns[0];
            commandColumn.Sort(System.ComponentModel.ListSortDirection.Descending);
            Dispatcher.UIThread.RunJobs();

            var sortedFirst = FirstCommand();
            log.AppendLine($"sorted desc → {sortedFirst}");
            Assert.NotEqual(canonicalFirst, sortedFirst);

            // [3] Clearing returns to the canonical order — the ratified requirement.
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var afterReset = FirstCommand();
            log.AppendLine($"after reset → {afterReset}");
            Assert.Equal(canonicalFirst, afterReset);

            // The affordance is stateless and always available — see the window's code-behind for why driving
            // its visibility from the grid's own Sorting event did not work.
            Assert.True(reset.IsVisible);

            // [5] Sorting still works after a reset, so the reset restores the canonical order without
            // leaving the grid in a state where its own sorting has stopped responding.
            commandColumn.Sort(System.ComponentModel.ListSortDirection.Descending);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(sortedFirst, FirstCommand());

            // ⚠ MEASURED AND NOT ASSERTED: Avalonia 12's DataGridColumn exposes no public sort-direction
            // property, so the header's direction glyph cannot be inspected from a test. The reset calls
            // DataGridColumn.ClearSort() (which does exist) and re-assigns ItemsSource; that the ROW ORDER
            // returns is proven above, but whether the header glyph clears with it is owed to visual QA.
            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ⭐ ONE version for the whole application, proven where it is actually displayed: on screen.
    //
    // ⚠⚠ REPOINTED IN M3.1b (product-polish.md §19.3). This used to assert the STATUS BAR's version chip,
    // written after the user found the literal "EmberTern 0.1.0" there disagreeing with About. Ratified
    // decision D3 removed the chip — the application name and version belong to About, where they are the
    // subject, and not to a bar that reports what is happening RIGHT NOW. The test's subject moved with it.
    //
    // ⭐ Deleting it would have been the lazy reading. The property worth keeping is not "the status bar
    // renders it" but "the version reaches the SCREEN from AppInfo rather than from a literal" — and that
    // property simply has a new single home. `AppInfoTests` guards the other half (no version literal
    // anywhere under src/); this guards that the surviving display actually resolves.
    //
    // ⚠ It also stopped constructing `MainWindow`, which is the documented hang-prone shape — and this is
    // the very class the full-suite hang keeps being reported in (#94/#226/#286). `AboutWindow` has a
    // parameterless constructor and builds its own view model, so the assertion is both cheaper and
    // stronger: nothing but the About window itself can be supplying the value.
    [Fact]
    public async System.Threading.Tasks.Task AboutWindowShowsTheVersionFromAppInfo()
    {
        await SharedSession.Dispatch(() =>
        {
            var window = new AboutWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var shown = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .Where(t => t.Contains(AppInfo.Version, StringComparison.Ordinal))
                .ToArray();

            _out.WriteLine("About — teksty z wersją: " + string.Join(" | ", shown));
            Assert.NotEmpty(shown);

            var product = window.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => (t.Text ?? string.Empty).Contains(AppInfo.Product, StringComparison.Ordinal));
            Assert.True(product, "Okno About nie pokazuje nazwy produktu z AppInfo.");

            window.Close();
        }, CancellationToken.None);
    }

    // The hamburger's three rules must be DRAWN identically, which the ink-box assertion above no longer
    // implies now that it is a range rather than an equality. This pins the arithmetic directly, from the
    // geometry source — the same source-scanning idiom TheSameMenuOperationAlwaysCarriesTheSameIcon uses.
    //
    // ⭐ Why it is arithmetic and not taste. The 24→16 render is a ×2/3 scale, so a rule declared at y has its
    // top edge at 2(y−1)/3 with a 1.333px thickness, and the FRACTION of that edge decides the anti-aliasing.
    // At y4/12/20 the fractions were .000 / .333 / .667: the middle rule spread over two 67%-covered pixel
    // rows and read visibly thicker than the outer two, whose coverage was 100% + 33%. Equal phases require
    // 2·Δy/3 ∈ ℤ, i.e. **Δy a multiple of 1.5**. No nudging of individual lines can substitute for that.
    [Fact]
    public void HamburgerRulesAllRenderIdentically()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Themes", "IconGeometries.axaml"));

        var geometry = Regex.Match(xaml, @"<StreamGeometry x:Key=""Icon\.Menu"">([^<]*)</StreamGeometry>");
        Assert.True(geometry.Success, "Icon.Menu geometry not found");

        var rules = Regex.Matches(geometry.Groups[1].Value, @"M([\d.]+) ([\d.]+) H([\d.]+)")
            .Select(m => (
                X0: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                X1: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.Equal(3, rules.Length);

        // Every rule spans the same x range, so none can look shorter or end differently from the others.
        Assert.Single(rules.Select(r => (r.X0, r.X1)).Distinct());

        // Evenly spaced, and the spacing is a multiple of 1.5 — the phase condition.
        double gap = rules[1].Y - rules[0].Y;
        Assert.Equal(gap, rules[2].Y - rules[1].Y, precision: 6);
        Assert.Equal(0d, (gap / 1.5) % 1, precision: 6);

        // The condition restated as the thing the user actually sees: one sub-pixel phase for all three.
        var phases = rules.Select(r => Math.Round(2 * (r.Y - 1) / 3 % 1, 6)).Distinct().ToArray();
        _out.WriteLine($"rules at y={string.Join("/", rules.Select(r => r.Y))}, gap={gap}, "
                       + $"phase(s)={string.Join(",", phases)}");
        Assert.Single(phases);

        // Symmetric about the centre of the 24×24 grid in both axes.
        Assert.Equal(12d, (rules[0].X0 + rules[0].X1) / 2, precision: 6);
        Assert.Equal(12d, (rules[0].Y + rules[2].Y) / 2, precision: 6);
    }

    // Etap 4 stopped gestures being typed by hand into UiStrings; this closes the same hole in XAML.
    // A menu's gesture column must come from {app:CommandGesture}, never from a literal
    // InputGesture="F7" — a literal is exactly what went stale when Format SQL moved from Alt+F to Ctrl+K.
    [Fact]
    public void NoMenuItemTypesItsGestureByHand()
    {
        // ⭐ The allowlist is EMPTY, and that is the finished state. It used to hold the fields grid's
        // Insert / F2 / Delete, which were three local DataGrid.KeyBindings — the last hand-typed gestures in
        // the application. The UX Consistency Pass routed them (CollectionAdd keeps Insert as an alternate,
        // CollectionRemove keeps Delete, and F2 became CollectionEdit), so every gesture a menu shows now
        // comes from the catalog and there is nothing left to excuse.
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        var pattern = new Regex(@"InputGesture=""([^""{][^""]*)""", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot(), "src", "EmberTern.App"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                if (!allowed.Contains(m.Groups[1].Value))
                {
                    offenders.Add($"{Path.GetFileName(file)}: InputGesture=\"{m.Groups[1].Value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These menu items type a gesture by hand instead of reading it from the catalog via "
            + "{app:CommandGesture}, so they will go stale when the shortcut is re-bound: "
            + string.Join(", ", offenders));
    }

    // ⭐ UX Consistency Pass: ONE operation, ONE icon — wherever the user meets it.
    //
    // The user found this by eye: "Debug procedure" carried the debugger's composite identity mark in the
    // Object Explorer and a plain crosshair in the Package Members menu. Same command, same label, two
    // different glyphs depending on where you right-clicked. That is not a thing anyone re-checks by hand
    // across 133 menu items, so it is checked here: group every menu item by the UiStrings constant it is
    // labelled with, and require the group to agree on its icon.
    [Fact]
    public void TheSameMenuOperationAlwaysCarriesTheSameIcon()
    {
        // <MenuItem …> up to its own close — captures attributes AND an inline <MenuItem.Icon> child, so a
        // composite icon counts as an icon rather than reading as "none".
        var item = new Regex(@"<MenuItem\b(.*?)(?:/>|</MenuItem>)", RegexOptions.Compiled | RegexOptions.Singleline);
        var header = new Regex(@"Header=""\{x:Static app:UiStrings\.(\w+)\}""", RegexOptions.Compiled);
        var menuIcon = new Regex(@"\{app:MenuIcon ([\w.]+)", RegexOptions.Compiled);
        var inlineIcon = new Regex(@"<MenuItem\.Icon>\s*<\w+:(\w+)", RegexOptions.Compiled);

        var iconsByOperation = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot(), "src", "EmberTern.App"), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match m in item.Matches(File.ReadAllText(file)))
            {
                var body = m.Groups[1].Value;
                var name = header.Match(body);
                if (!name.Success) continue; // bound or literal header — nothing stable to group by

                string icon = menuIcon.Match(body) is { Success: true } k ? k.Groups[1].Value
                    : inlineIcon.Match(body) is { Success: true } c ? c.Groups[1].Value
                    : "(no icon)";

                if (!iconsByOperation.TryGetValue(name.Groups[1].Value, out var set))
                {
                    iconsByOperation[name.Groups[1].Value] = set = new SortedSet<string>(StringComparer.Ordinal);
                }
                set.Add(icon);
            }
        }

        var drift = iconsByOperation
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key} → {string.Join(" / ", kv.Value)}")
            .ToArray();

        _out.WriteLine($"{iconsByOperation.Count} distinct menu operations checked");

        Assert.True(drift.Length == 0,
            "These operations show a different icon depending on which menu the user opened:"
            + Environment.NewLine + string.Join(Environment.NewLine, drift));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    // Every distinct key passed to {app:MenuIcon ...} anywhere in the views, read from the source .axaml.
    // Scanning the source is what makes the check exhaustive: a hand-maintained list would drift the first
    // time somebody adds a menu item without updating it.
    private static IReadOnlyList<string> MenuIconKeysUsedInViews()
    {
        var root = RepositoryRoot();
        var pattern = new Regex(@"\{app:MenuIcon\s+([A-Za-z0-9.]+)", RegexOptions.Compiled);
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "EmberTern.App"), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                keys.Add(m.Groups[1].Value);
            }
        }

        return keys.ToArray();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }


}
