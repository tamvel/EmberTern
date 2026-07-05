using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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

    // Etap 2: every MetadataObjectKind's geometry key must resolve to a real Geometry
    // through IconGeometryConverter (the live SVG-icon pipeline), plus the tree-chrome
    // keys (Query tab / Connection node / Folder). A missing/typo'd key renders a BLANK
    // icon at runtime — no crash, so the smoke test wouldn't catch it; this would. Also
    // future-proofs: a new enum value without a matching <StreamGeometry> fails here.
    [Fact]
    public async System.Threading.Tasks.Task IconGeometries_AllKindsAndChromeResolve()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));
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
}
