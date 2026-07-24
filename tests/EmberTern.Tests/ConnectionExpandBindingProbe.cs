using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;
using EmberTern.Firebird;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

// Headless Avalonia probe — proves whether ConnectionNodeViewModel.IsExpanded
// actually propagates to the real TreeViewItem.IsExpanded through the compiled
// Style binding in MainWindow.axaml. NOT a behavioural assertion to keep green
// forever — it's an instrument. It builds the REAL MainWindow (real compiled
// bindings, real styles) so the binding under test is the production one.
public sealed class ConnectionExpandBindingProbe
{
    // ONE headless session for the whole class (gotcha #94). This is not a tidy-up: a session owns a UI
    // thread, and AvaloniaEdit builds its caret/editing KeyBindings as STATIC lists created on whichever
    // thread first constructs a TextEditor. With a session per test, every later test's TextArea shares
    // those KeyBinding instances across threads, so any real KeyDown into an editor dies with
    // "The calling thread cannot access this object because a different thread owns it" — regardless of how
    // the key is injected. One session keeps every test on one thread, which is also what the gotcha has
    // always said to do.
    private static readonly HeadlessUnitTestSession SharedSession =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));

    private readonly ITestOutputHelper _out;

    public ConnectionExpandBindingProbe(ITestOutputHelper output) => _out = output;

    private static class HeadlessAppEntry
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<global::EmberTern.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
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

            log.AppendLine($"VM IsExpanded = {node.IsExpanded}");
            log.AppendLine($"row.IsExpanded = {row.IsExpanded}");

            Assert.True(node.IsExpanded, "VM should auto-expand on connect.\n" + log);
            Assert.True(row.IsExpanded, "the SidebarRow must mirror the node's expansion.\n" + log);

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

    // Etap 1 — Editor Find/Replace: the SearchPanel installer must attach cleanly and
    // set a context menu, and the Ctrl+F router predicate (IsInsideEditor) must return
    // true for an element inside a TextEditor and false otherwise. Guards the routing
    // decision that leaves Ctrl+F for the editor vs. the sidebar filter.
    [Fact]
    public async System.Threading.Tasks.Task EditorSearch_InstallsAndRoutingPredicateHolds()
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

            var panel = EditorSearch.Install(editor);
            log.AppendLine($"[1] panel installed = {panel is not null}, contextMenu = {editor.ContextMenu is not null}");
            Assert.NotNull(panel);
            Assert.NotNull(editor.ContextMenu);

            // Routing predicate: the editor (and its inner visual descendants) count as
            // "inside an editor"; a sibling TextBox and null do not.
            Assert.True(EditorSearch.IsInsideEditor(editor), "editor itself should be inside-editor");
            var inner = editor.GetVisualDescendants().OfType<Avalonia.Visual>().FirstOrDefault(v => v != editor);
            if (inner is not null)
                Assert.True(EditorSearch.IsInsideEditor(inner), "inner text view should be inside-editor");
            Assert.False(EditorSearch.IsInsideEditor(outside), "sibling TextBox is not inside-editor");
            Assert.False(EditorSearch.IsInsideEditor(null), "null is not inside-editor");

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
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
        }, CancellationToken.None);
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
}
