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
using EmberTern.App;
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
            Assert.True(row!.IsExpandable, "a connection row is expandable.\n" + log);
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

    // NOTE: the type-to-filter headless probe was removed here — type-to-filter + Escape
    // focus-return are being retargeted from the TreeView to the flat ListBox in Phase 3,
    // and the probe will be re-added against SidebarList then. Ctrl+F (window-level) and the
    // filter box itself are unaffected.

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

}
