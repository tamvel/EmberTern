using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// POMIAR (M3.4b) — czy JEDNO współdzielone <c>ContextMenu</c> jest w ogóle poprawnym kierunkiem dla
/// wiersza Metadata Explorera, i jaki byłby z niego zysk.
///
/// <para><b>Skąd pytanie.</b> Szablon wiersza <c>MetadataNodeViewModel</c> niesie inline
/// <c>ContextMenu</c> z 22 pozycjami, a szablon dostaje KAŻDY zrealizowany wiersz wirtualizowanej listy.
/// Zmierzone: przy przewijaniu 5 000 wierszy w 40 skokach szablon budowany jest 1 660 razy, więc menu jest
/// tworzone i wyrzucane 1 660 razy; kosztuje to ~0,23 ms na wiersz (~40% narzutu na ścieżce przewijania),
/// a w każdej chwili żyje 440 obiektów <c>MenuItem</c>.</para>
///
/// <para>⛔ <b>To NIE jest implementacja i nic w produkcie nie zostało zmienione.</b> Klasa odpowiada na
/// dwa pytania użytkownika i na tym się kończy: (1) czy współdzielone menu działa <b>bez obchodzenia
/// mechanizmu bindowania</b>, (2) jaki jest realny zysk. Decyzja, czy to wchodzi do M3.4, należy do
/// użytkownika i zapada po tych liczbach.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SharedContextMenuFeasibilityProbe
{
    private const int RowHeight = 24;
    private const int MenuItems = 22;

    private readonly HeadlessUnitTestSession _session;
    private readonly ITestOutputHelper _out;

    public SharedContextMenuFeasibilityProbe(HeadlessSessionFixture fixture, ITestOutputHelper output)
    {
        _session = fixture.Session;
        _out = output;
    }

    /// <summary>
    /// PYTANIE 1 — czy jedna instancja <c>ContextMenu</c> może obsłużyć wiele wierszy i czy po otwarciu
    /// widzi <b>DataContext tego wiersza</b>, na którym ją otwarto. To jest cały test „bez obejść":
    /// pozycje menu wiążą się zwyczajnie (<c>{Binding Name}</c>), a odpowiedź brzmi tak lub nie.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task SharedMenu_FollowsTheRowItWasOpenedOn()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            var rows = new ObservableCollection<Row>(
                Enumerable.Range(0, 200).Select(i => new Row("OBJ_" + i)));

            // JEDNA instancja na całą listę. Pozycje wiążą się do DataContextu — zwyczajnie, bez sztuczek.
            var shared = new ContextMenu();
            for (var i = 0; i < MenuItems; i++)
            {
                var item = new MenuItem();
                item.Bind(MenuItem.HeaderProperty, new Binding("Name"));
                shared.Items.Add(item);
            }

            var list = BuildList(rows, sharedMenu: shared, perRowMenu: false);
            var window = new Window { Width = 300, Height = RowHeight * 25, Content = list };
            window.Show();
            Pump();

            var containers = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            log.AppendLine($"zrealizowanych kontenerów = {containers.Count}");

            // ⭐ Pytanie „czy JEDNA instancja da się przypiąć do WIELU kontrolek" jest rozstrzygane
            //    już przez to, że lista w ogóle się zrealizowała — setter stylu przypisał tę samą
            //    instancję każdemu wierszowi.
            var attached = containers.Count(c => ReferenceEquals(Owner(c)?.ContextMenu, shared));
            log.AppendLine($"wierszy niosących TĘ SAMĄ instancję menu = {attached} / {containers.Count}");
            Assert.True(attached > 1,
                "Gdyby Avalonia nie pozwalała współdzielić instancji ContextMenu, ten warunek by nie "
                + "przeszedł i cały wariant A byłby zamknięty tutaj.\n" + log);

            // Otwórz na wierszu 3, potem na wierszu 7 — czy menu podąża za DataContextem?
            var readings = new StringBuilder();
            foreach (var index in new[] { 3, 7, 1 })
            {
                var target = Owner(containers[index])!;
                shared.Open(target);
                Pump();

                var dc = shared.DataContext as Row;
                var firstHeader = (shared.Items[0] as MenuItem)?.Header as string;
                readings.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  otwarte na wierszu {0} (oczekiwany '{1}') → menu.DataContext = '{2}', "
                    + "Header pierwszej pozycji = '{3}', PlacementTarget = '{4}'",
                    index, rows[index].Name, dc?.Name ?? "<null>", firstHeader ?? "<null>",
                    (shared.PlacementTarget as Control)?.DataContext is Row pr ? pr.Name : "<null>"));

                shared.Close();
                Pump();
            }

            log.AppendLine("ODCZYTY:");
            log.Append(readings);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    /// <summary>
    /// PYTANIE 2 — realny zysk. Ta sama lista, ta sama liczba pozycji, to samo przewijanie;
    /// różni je wyłącznie to, czy menu jest jedno na listę, czy jedno na wiersz.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task SharedMenu_IsMeasuredAgainstPerRowMenu()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            // Rozgrzewka — pierwszy przebieg w procesie płaci JIT i inicjalizację motywu, więc bez niej
            // porównanie mierzyłoby kolejność, a nie warianty (§19.26 — pomiar ma mieć zakres).
            Measure(perRow: true, rowCount: 500, jumps: 5);

            var perRow = Measure(perRow: true, rowCount: 5000, jumps: 40);
            var sharedA = Measure(perRow: false, rowCount: 5000, jumps: 40);
            var perRow2 = Measure(perRow: true, rowCount: 5000, jumps: 40);
            var sharedB = Measure(perRow: false, rowCount: 5000, jumps: 40);

            log.AppendLine("5 000 wierszy, 40 skoków przez całą listę, menu 22 pozycji:");
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  menu NA WIERSZ    : {0,7:F1} ms i {1,7:F1} ms   (żywych MenuItem: {2})",
                perRow.ScrollMs, perRow2.ScrollMs, perRow.LiveItems));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  menu WSPÓŁDZIELONE: {0,7:F1} ms i {1,7:F1} ms   (żywych MenuItem: {2})",
                sharedA.ScrollMs, sharedB.ScrollMs, sharedA.LiveItems));

            var bestPerRow = Math.Min(perRow.ScrollMs, perRow2.ScrollMs);
            var bestShared = Math.Min(sharedA.ScrollMs, sharedB.ScrollMs);
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  RÓŻNICA (najlepsze z dwóch przebiegów): {0:F1} ms, czyli {1:F0}% czasu przewijania",
                bestPerRow - bestShared, 100.0 * (bestPerRow - bestShared) / Math.Max(0.001, bestPerRow)));

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  szablonów zbudowanych podczas przewijania: na wiersz = {0}, współdzielone = {1}",
                perRow.TemplateBuilds, sharedA.TemplateBuilds));
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ── Uprząż ────────────────────────────────────────────────────────────────────────────────────

    private static (double ScrollMs, int LiveItems, int TemplateBuilds) Measure(
        bool perRow, int rowCount, int jumps)
    {
        var rows = new ObservableCollection<Row>(
            Enumerable.Range(0, rowCount).Select(i => new Row("OBJ_" + i)));

        ContextMenu? shared = null;
        if (!perRow)
        {
            shared = new ContextMenu();
            for (var i = 0; i < MenuItems; i++)
            {
                var item = new MenuItem();
                item.Bind(MenuItem.HeaderProperty, new Binding("Name"));
                shared.Items.Add(item);
            }
        }

        var builds = 0;
        var list = BuildList(rows, shared, perRow, () => builds++);
        var window = new Window { Width = 300, Height = RowHeight * 25, Content = list };
        window.Show();
        Pump();

        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var buildsAtStart = builds;

        var sw = Stopwatch.StartNew();
        for (var step = 1; step <= jumps; step++)
        {
            scroll.Offset = scroll.Offset.WithY(
                step * (scroll.Extent.Height - scroll.Viewport.Height) / jumps);
            Pump();
        }
        sw.Stop();

        var live = window.GetVisualDescendants().OfType<Control>()
            .Select(c => c.ContextMenu)
            .Where(m => m is not null)
            .Distinct()
            .Sum(m => m!.Items.Count);

        window.Close();
        return (sw.Elapsed.TotalMilliseconds, live, builds - buildsAtStart);
    }

    private static ListBox BuildList(
        ObservableCollection<Row> rows, ContextMenu? sharedMenu, bool perRowMenu, Action? onBuild = null)
    {
        var list = new ListBox
        {
            ItemsSource = rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<Row>((_, _) =>
            {
                onBuild?.Invoke();
                var border = new Border
                {
                    Height = RowHeight,
                    Child = new TextBlock { VerticalAlignment = VerticalAlignment.Center },
                };

                if (perRowMenu)
                {
                    // Stan dzisiejszy: nowe menu na każdą realizację wiersza.
                    var menu = new ContextMenu();
                    for (var i = 0; i < MenuItems; i++)
                    {
                        var item = new MenuItem();
                        item.Bind(MenuItem.HeaderProperty, new Binding("Name"));
                        menu.Items.Add(item);
                    }
                    border.ContextMenu = menu;
                }
                else if (sharedMenu is not null)
                {
                    // Wariant A: ta sama instancja przypięta do każdego wiersza.
                    border.ContextMenu = sharedMenu;
                }

                return border;
            }),
        };
        return list;
    }

    private static Control? Owner(ListBoxItem container)
        => container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.ContextMenu is not null);

    private static void Pump()
    {
        for (var i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }

    internal sealed class Row
    {
        public Row(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => Name;
    }
}
