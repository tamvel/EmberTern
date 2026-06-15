using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
