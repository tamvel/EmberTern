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

    [Fact]
    public async System.Threading.Tasks.Task IsExpanded_VmToTreeViewItem_Binding()
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

            var node = vm.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
                .Single(n => n.Profile.Id == profile.Id);

            TreeViewItem? Container() => window.GetVisualDescendants()
                .OfType<TreeViewItem>()
                .FirstOrDefault(t => ReferenceEquals(t.DataContext, node));

            log.AppendLine($"[1] VM IsExpanded initial = {node.IsExpanded}");
            log.AppendLine($"[2] container exists initial = {Container() is not null}");
            log.AppendLine($"[3] container.IsExpanded initial = {Container()?.IsExpanded}");

            // The exact thing auto-expand-on-connect does: flip the VM property.
            node.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            var c = Container();
            log.AppendLine($"[1] VM IsExpanded after set = {node.IsExpanded}");
            log.AppendLine($"[2] container exists after set = {c is not null}");
            log.AppendLine($"[3] container.IsExpanded after set = {c?.IsExpanded}");

            // The binding must propagate: VM true => container true. This is the
            // regression pin for the single-container-style fix in MainWindow.axaml.
            Assert.True(c is not null, "TreeViewItem container should exist for a root connection node.\n" + log);
            Assert.True(c!.IsExpanded, "TreeViewItem.IsExpanded must follow the VM through the container-style binding.\n" + log);

            window.Close();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // End-to-end proof of the auto-expand-on-connect path: flipping IsConnected (which
    // OnIsConnectedChanged + LoadCategoriesAsync react to) must leave the real
    // TreeViewItem expanded — with NO Dispatcher-post / toggle workarounds in the VM.
    [Fact]
    public async System.Threading.Tasks.Task AutoExpandOnConnect_ExpandsTreeViewItem()
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

            // Drive the exact connect reaction. The real FbConnection isn't open, so
            // LoadGroupAsync bails per category — but categories are still built and the
            // auto-expand path runs, which is what we're proving. Pump the dispatcher a
            // few times to let LoadCategoriesAsync's awaited continuations resume.
            node.IsConnected = true;
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();

            var c = window.GetVisualDescendants().OfType<TreeViewItem>()
                .FirstOrDefault(t => ReferenceEquals(t.DataContext, node));

            log.AppendLine($"VM IsExpanded = {node.IsExpanded}");
            log.AppendLine($"container exists = {c is not null}");
            log.AppendLine($"container.IsExpanded = {c?.IsExpanded}");

            Assert.True(node.IsExpanded, "VM should auto-expand on connect.\n" + log);
            Assert.True(c is not null, "Container should exist.\n" + log);
            Assert.True(c!.IsExpanded, "TreeViewItem must be expanded after connect.\n" + log);

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

    // Type-to-filter (replaces type-ahead): build the REAL MainWindow, focus the tree, and
    // verify (a) typing redirects the char into the SidebarFilterBox + moves focus there,
    // (b) Ctrl+F focuses the filter, (c) Escape clears the filter and returns focus to the
    // tree. Production wiring under test (gotcha #39).
    [Fact]
    public async System.Threading.Tasks.Task TypeToFilter_TreeTyping_RedirectsToFilterBox()
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

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "SidebarTree");
            var filter = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SidebarFilterBox");
            var nodeItem = window.GetVisualDescendants().OfType<TreeViewItem>()
                .First(t => t.DataContext is ConnectionNodeViewModel);

            object? Focused() => TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();

            // (a) Focus a tree item, type 'k' → goes to the filter, focus moves there.
            nodeItem.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("k");
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"after tree-typing: FilterText='{vm.Metadata.FilterText}' boxText='{filter.Text}' focusIsBox={ReferenceEquals(Focused(), filter)}");
            Assert.True(vm.Metadata.FilterText == "k", "typing in the tree must fill the filter.\n" + log);
            Assert.True(ReferenceEquals(Focused(), filter), "focus must move to the filter box.\n" + log);

            // (b) Ctrl+F from elsewhere focuses the filter.
            nodeItem.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            log.AppendLine($"after Ctrl+F: focusIsBox={ReferenceEquals(Focused(), filter)}");
            Assert.True(ReferenceEquals(Focused(), filter), "Ctrl+F must focus the filter box.\n" + log);

            // (c) Escape in the filter clears it and returns focus to the tree.
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
}
