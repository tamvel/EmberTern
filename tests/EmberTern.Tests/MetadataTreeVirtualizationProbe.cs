using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ KROK 15b — EKSPERYMENT ROZSTRZYGAJĄCY dla starego zgłoszenia „drzewo samo przewija się w dół,
/// po czym aplikacja zawiesza się i zamyka" (Product Polish, handover M3 §3.7a).
///
/// <para><b>Po co on jest.</b> M3.4a zmierzyło ścieżkę „rozwiń kliknięciem" w warstwie MODELU
/// (<c>tools/probes/MetadataPerfProbe</c>, sekcja B4) i wyszło <b>2,3 ms</b> na 2 400 liściach przy
/// 6 000 wierszy ogona — wobec 916,9 ms defektu naprawionego przez Layer 1. Hipoteza „drogi splice
/// zawiesza aplikację" upadła. ⚠⚠ Ale tamten pomiar <b>nie dotykał Avalonii</b>: 2 400 powiadomień
/// <c>CollectionChanged</c> trafia w produkcie do <b>wirtualizującego <c>ListBox</c>a</b>, a zgłoszony
/// objaw — samoczynne przewijanie — jest zachowaniem <b>panelu</b>, nie kolekcji. Ta klasa mierzy
/// dokładnie tę brakującą połowę.</para>
///
/// <para>⭐⭐ <b>IZOLUJE JEDNĄ ZMIENNĄ, I TO JEST CAŁY SENS JEJ ODDZIELNEGO ISTNIENIA.</b> Kandydatów na
/// przyczynę zawieszeń jest DWÓCH i dotąd występowały razem:
/// <list type="number">
/// <item><b>A — konstruowanie <c>MainWindow</c> w teście headless.</b> Zmierzone jako kształt podatny na
/// zawieszenie: <c>BrandingPresentationTests</c> zawieszało się, dopóki budowało <c>MainWindow</c>,
/// i schodzi do 476 ms na gołym <c>new Window()</c>. <c>ConnectionExpandBindingProbe</c> — klasa, którą
/// użytkownik kazał uruchamiać SAMĄ — buduje <c>MainWindow</c> w wielu testach.</item>
/// <item><b>B — inkrementalny splice do wirtualizującej listy.</b> Hipoteza z §3.7a(a).</item>
/// </list>
/// Ta klasa buduje <b>gołe <c>Window</c> + <c>ListBox</c></b>, więc jeśli zawiesi się TUTAJ, przyczyną
/// jest B. Jeśli nie zawiesi się tutaj, a <c>ConnectionExpandBindingProbe</c> nadal się zawiesza —
/// przyczyną jest A i „felerny test" nie ma nic wspólnego ze starym bugiem drzewa.
/// ⛔ Dlatego <b>nie wolno jej dopisać do <c>ConnectionExpandBindingProbe</c></b>: to skleiłoby z powrotem
/// dwie zmienne, które ona rozdziela.</para>
///
/// <para>⚠ <b>Kontener jest częścią mechanizmu</b> (pułapka 14 handovera M3). Okno ma skończoną wysokość,
/// lista ma <c>VirtualizingStackPanel</c> i wiersz o stałej wysokości <c>Size.Row.Tree</c> — bez tego
/// wirtualizacja w ogóle się nie włącza i test mierzyłby coś innego, niż nazwa obiecuje.</para>
///
/// <para>⚠ Szablon wiersza jest UPROSZCZONY (sam tekst o właściwej wysokości), i to jest świadome:
/// przedmiotem jest zachowanie PANELU przy N wstawieniach, a nie treść wiersza. Własnością, która ma tu
/// znaczenie, jest <b>jednolita wysokość wiersza</b> — bo od niej zależy ekstent i kotwiczenie — i ta jest
/// odtworzona wiernie.</para>
///
/// <para>⚠ Klasa dołącza do <see cref="HeadlessCollection"/> i nigdy nie zakłada własnego
/// <c>IClassFixture</c> (#94/#226/#286). Musi też trafić do <b>filtra partycji</b> — filtr jest listą nazw
/// i starzeje się cicho.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MetadataTreeVirtualizationProbe
{
    private const int RowHeight = 24;      // Size.Row.Tree — po korekcie katalogu w M3.4a.
    private const int ViewportRows = 25;   // ~600 px okna: realistyczna wysokość paska bocznego.

    private readonly HeadlessUnitTestSession _session;
    private readonly ITestOutputHelper _out;

    public MetadataTreeVirtualizationProbe(HeadlessSessionFixture fixture, ITestOutputHelper output)
    {
        _session = fixture.Session;
        _out = output;
    }

    /// <summary>
    /// ⭐ Zmienna B, przypadek realistyczny: użytkownik widzi chevron kategorii i klika go.
    /// Mierzone: czy rozwinięcie 2 400 liści w wirtualizującej liście (a) w ogóle się kończy,
    /// (b) mieści się w rozsądnym czasie, (c) <b>nie przesuwa pozycji przewijania samo z siebie</b>.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ClickExpandOfALargeCategory_CompletesAndDoesNotMoveTheScrollPosition()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            var (window, list, controller, group) = BuildTree(leaves: 2400, siblingsBelow: 3000);

            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            log.AppendLine($"ScrollViewer znaleziony = {scroll is not null}");
            Assert.True(scroll is not null,
                "Bez ScrollViewera ten test nie mierzy wirtualizacji, tylko listę bez viewportu — "
                + "kontener jest częścią mechanizmu (pułapka 14).\n" + log);

            var panel = list.GetVisualDescendants().OfType<VirtualizingStackPanel>().FirstOrDefault();
            log.AppendLine($"VirtualizingStackPanel znaleziony = {panel is not null}");
            log.AppendLine($"wierszy w modelu przed = {controller.Rows.Count}");
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "viewport = {0:0.0} px, ekstent = {1:0.0} px, offset = {2:0.0}",
                scroll!.Viewport.Height, scroll.Extent.Height, scroll.Offset.Y));

            var offsetBefore = scroll.Offset.Y;

            // Rozwinięcie kliknięciem — dokładnie to, co robi chevron: przełącz IsExpanded, nic więcej.
            var sw = Stopwatch.StartNew();
            controller.Toggle(controller.Rows.First(r => ReferenceEquals(r.Node, group)));
            Pump();
            sw.Stop();

            var offsetAfter = scroll.Offset.Y;

            log.AppendLine($"wierszy w modelu po = {controller.Rows.Count}");
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "czas rozwinięcia z układem = {0:0.0} ms", sw.Elapsed.TotalMilliseconds));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "offset przed = {0:0.0}, po = {1:0.0}, delta = {2:0.0}",
                offsetBefore, offsetAfter, offsetAfter - offsetBefore));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "ekstent po = {0:0.0} px", scroll.Extent.Height));

            // 2 400 liści + 3 000 rodzeństwa + 3 wiersze kontenerów (połączenie, Tables, Below).
            Assert.Equal(2400 + 3000 + 3, controller.Rows.Count);

            // ⚠ Granica jest CELOWO hojna. To nie jest test wydajności — pomiar wydajności mieszka
            // w sondzie (MetadataPerfProbe B4, 2,3 ms w warstwie modelu). Tutaj przedmiotem jest
            // „czy to się KOŃCZY": zawieszenie objawia się sekundami albo brakiem powrotu, nie
            // dziesiątkami milisekund. Granica zawężona do pomiaru byłaby testem migoczącym na CI.
            Assert.True(sw.Elapsed.TotalSeconds < 5,
                $"Rozwinięcie 2 400 liści zajęło {sw.Elapsed.TotalSeconds:0.0} s — to jest kształt "
                + "zgłoszonego zawieszenia, a nie koszt splice'u.\n" + log);

            // ⭐ TO JEST ASERCJA, DLA KTÓREJ TEN TEST POWSTAŁ. Zgłoszony objaw brzmiał „drzewo samo
            // zaczyna przewijać się w dół" — czyli pozycja przewijania zmienia się bez udziału
            // użytkownika. Kategoria stoi NAD viewportem po rozwinięciu wciąż w tym samym miejscu,
            // więc pozycja nie ma powodu się ruszyć.
            Assert.True(Math.Abs(offsetAfter - offsetBefore) < 0.5,
                $"Pozycja przewijania przesunęła się sama o {offsetAfter - offsetBefore:0.0} px przy "
                + "rozwinięciu kategorii. To jest dokładnie objaw ze zgłoszenia użytkownika.\n" + log);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    /// <summary>
    /// ⭐ Przypadek ostrzejszy: lista jest <b>przewinięta w głąb</b>, a rozwijana kategoria stoi
    /// <b>POWYŻEJ</b> viewportu. Wtedy 2 400 nowych wierszy wchodzi NAD tym, na co użytkownik patrzy —
    /// czyli jedyny układ, w którym „treść ucieka spod wzroku" jest w ogóle możliwa.
    ///
    /// <para>⚠ Ten test <b>nie zakłada</b>, która odpowiedź jest poprawna, i dlatego niczego nie
    /// przesądza asercją poza tym, że operacja się KOŃCZY. Avalonia może zachować pozycję pikselową
    /// (treść ucieka w dół) albo zakotwiczyć element (offset rośnie o wysokość wstawionego bloku) —
    /// obie są spójnymi decyzjami frameworka. Liczba trafia do logu jako <b>pomiar</b>, bo to ona jest
    /// wynikiem eksperymentu; przekucie jej w wymaganie wymaga decyzji produktowej użytkownika.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ExpandAboveTheViewport_IsMeasured_AndCompletes()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            var (window, list, controller, group) = BuildTree(leaves: 2400, siblingsBelow: 3000);

            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

            // Przewiń w głąb ogona — kategoria (wiersz 1) zostaje daleko nad viewportem.
            scroll.Offset = scroll.Offset.WithY(1500);
            Pump();

            var offsetBefore = scroll.Offset.Y;
            var firstVisibleBefore = FirstRealizedIndex(list);

            var sw = Stopwatch.StartNew();
            controller.Toggle(controller.Rows.First(r => ReferenceEquals(r.Node, group)));
            Pump();
            sw.Stop();

            var offsetAfter = scroll.Offset.Y;
            var firstVisibleAfter = FirstRealizedIndex(list);

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "czas = {0:0.0} ms · offset {1:0.0} → {2:0.0} (delta {3:0.0}) · "
                + "pierwszy zrealizowany wiersz {4} → {5} · ekstent {6:0.0} px",
                sw.Elapsed.TotalMilliseconds, offsetBefore, offsetAfter, offsetAfter - offsetBefore,
                firstVisibleBefore, firstVisibleAfter, scroll.Extent.Height));

            Assert.True(sw.Elapsed.TotalSeconds < 5,
                $"Rozwinięcie nad viewportem zajęło {sw.Elapsed.TotalSeconds:0.0} s.\n" + log);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    /// <summary>
    /// ⭐ Kontrola: <b>ta sama zmiana liczby wierszy</b>, ale wykonana jako JEDNA pełna re-projekcja
    /// (ścieżka pod strażnikiem zbiorczym), a nie jako N pojedynczych wstawień. Różnica między tym
    /// testem a pierwszym jest jedynym miejscem, w którym widać koszt <i>samego</i> inkrementalnego
    /// splice'u po stronie panelu — czyli dokładnie tę wielkość, której sonda w warstwie modelu
    /// zmierzyć nie mogła.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task IncrementalSplice_IsComparedWithASingleReprojection()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            var (window, list, controller, group) = BuildTree(leaves: 2400, siblingsBelow: 3000);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

            var sw = Stopwatch.StartNew();
            controller.Toggle(controller.Rows.First(r => ReferenceEquals(r.Node, group)));
            Pump();
            sw.Stop();

            // Ta sama zawartość, ale zbudowana jednym Rebuild-em (Clear + wstawienia bez pośrednich
            // przebiegów układu, zakończone jednym Reset-em dla panelu).
            var sw2 = Stopwatch.StartNew();
            controller.Rebuild();
            Pump();
            sw2.Stop();

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "splice inkrementalny (N wstawień) = {0:0.0} ms · jedna re-projekcja = {1:0.0} ms · "
                + "wierszy = {2} · ekstent = {3:0.0} px",
                sw.Elapsed.TotalMilliseconds, sw2.Elapsed.TotalMilliseconds,
                controller.Rows.Count, scroll.Extent.Height));

            Assert.True(sw.Elapsed.TotalSeconds < 5 && sw2.Elapsed.TotalSeconds < 5,
                "Żadna z dwóch dróg nie ma prawa trwać sekundami.\n" + log);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    /// <summary>
    /// ⭐⭐ CZWARTY POMIAR, DOPISANY PO PRZECZYTANIU KODU POD KĄTEM STAŁEJ PROŚBY UŻYTKOWNIKA
    /// o stabilność przewijania — i to on odpowiada zgłoszonemu objawowi najbliżej z całej czwórki.
    ///
    /// <para><b>Ścieżka, która to robi w produkcie.</b> Kliknięcie chevronu kategorii <b>jeszcze
    /// niezaładowanej</b> uruchamia DWIE rzeczy z jednego gestu: (1) kontroler natychmiast splice'uje
    /// wiersze, (2) <c>MetadataNodeViewModel.OnIsExpandedChanged</c> odpala <c>LoadGroupAsync</c> jako
    /// „fire and forget", który po powrocie z katalogu robi <c>BeginUpdate</c>/<c>EndUpdate</c>, a
    /// <c>EndUpdate</c> woła <c>Rebuild</c> → <c>Rows.Clear()</c> + N wstawień. Czyli <b>chwilę po
    /// rozwinięciu lista jest budowana od zera</b>.</para>
    ///
    /// <para>⚠⚠ <b>To jest mechanizm mogący ruszyć pozycję przewijania bez udziału użytkownika</b> —
    /// dokładnie klasa zjawisk, którą użytkownik kazał zgłaszać. <c>Clear()</c> wysyła do panelu
    /// <c>Reset</c>, ekstent zapada się do zera, <c>ScrollViewer</c> przycina offset, a wiersze wracają
    /// dopiero potem. Ten test <b>mierzy skutek</b>, nie zakłada go.</para>
    ///
    /// <para>⛔ Test celowo <b>nie zabrania</b> tego zachowania asercją: jeżeli okaże się realne, jest to
    /// znany, udokumentowany kompromis Layer 1 (<c>metadata-refresh-analysis.md</c> §7) i jego zmiana jest
    /// decyzją produktową, a nie skutkiem ubocznym eksperymentu. Asercja pilnuje tylko, że operacja się
    /// KOŃCZY; liczba idzie do logu jako pomiar.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task FullReprojection_WhileScrolledDeep_IsMeasuredForScrollStability()
    {
        var log = new StringBuilder();

        await _session.Dispatch(() =>
        {
            var (window, list, controller, group) = BuildTree(leaves: 2400, siblingsBelow: 3000);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

            // Kategoria rozwinięta, użytkownik przewinął głęboko w dół — stan, w którym zgłoszono objaw.
            controller.Toggle(controller.Rows.First(r => ReferenceEquals(r.Node, group)));
            Pump();
            scroll.Offset = scroll.Offset.WithY(40000);
            Pump();

            var offsetBefore = scroll.Offset.Y;
            var firstBefore = FirstRealizedIndex(list);

            // Dokładnie to, co robi EndUpdate po powrocie LoadGroupAsync z katalogu.
            var sw = Stopwatch.StartNew();
            controller.Rebuild();
            Pump();
            sw.Stop();

            var offsetAfter = scroll.Offset.Y;
            var firstAfter = FirstRealizedIndex(list);

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PEŁNA RE-PROJEKCJA przy przewinięciu w głąb: czas = {0:0.0} ms · "
                + "offset {1:0.0} → {2:0.0} (delta {3:0.0}) · pierwszy zrealizowany {4} → {5} · "
                + "ekstent {6:0.0} px · wierszy {7}",
                sw.Elapsed.TotalMilliseconds, offsetBefore, offsetAfter, offsetAfter - offsetBefore,
                firstBefore, firstAfter, scroll.Extent.Height, controller.Rows.Count));

            if (Math.Abs(offsetAfter - offsetBefore) >= 0.5)
            {
                log.AppendLine("⚠ POZYCJA PRZEWIJANIA ZMIENIŁA SIĘ SAMA — to jest udokumentowany "
                    + "kompromis Layer 1 (Clear() + N wstawień), nie nowy defekt. Zmiana tego zachowania "
                    + "jest decyzją produktową.");
            }

            Assert.True(sw.Elapsed.TotalSeconds < 5,
                $"Pełna re-projekcja zajęła {sw.Elapsed.TotalSeconds:0.0} s.\n" + log);

            window.Close();
        }, CancellationToken.None);

        _out.WriteLine(log.ToString());
    }

    // ── Uprząż ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Buduje gołe okno z wirtualizującą listą, wpiętą w PRAWDZIWY <see cref="SidebarFlatController"/>.
    /// ⛔ Świadomie nie <c>MainWindow</c> — to jest zmienna A, którą ta klasa ma wykluczyć.
    /// </summary>
    private static (Window Window, ListBox List, SidebarFlatController Controller, ProbeNode Group)
        BuildTree(int leaves, int siblingsBelow)
    {
        var root = new ProbeNode("connection", container: true) { IsExpanded = true };
        var group = new ProbeNode("Tables", container: true);          // zwinięta — ją rozwijamy
        var below = new ProbeNode("Below", container: true) { IsExpanded = true };
        root.Children.Add(group);
        root.Children.Add(below);

        for (var i = 0; i < leaves; i++) group.Children.Add(new ProbeNode("OBJ_" + i, container: false));
        for (var i = 0; i < siblingsBelow; i++) below.Children.Add(new ProbeNode("SIB_" + i, container: false));

        var roots = new ObservableCollection<object> { root };
        var controller = new SidebarFlatController(
            roots,
            childrenSelector: o => ((ProbeNode)o).IsContainer ? ((ProbeNode)o).Children.Cast<object>() : null,
            isContainer: o => ((ProbeNode)o).IsContainer,
            hasChildren: o => ((ProbeNode)o).Children.Count > 0,
            isExpanded: o => ((ProbeNode)o).IsExpanded,
            setExpanded: (o, v) => ((ProbeNode)o).IsExpanded = v);

        var list = new ListBox
        {
            ItemsSource = controller.Rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            // Wiersz o stałej wysokości — jednolitość jest tu własnością nośną (ekstent + kotwiczenie).
            ItemTemplate = new FuncDataTemplate<SidebarRow>((_, _) => new TextBlock
            {
                Height = RowHeight,
                VerticalAlignment = VerticalAlignment.Center,
            }),
        };

        var window = new Window
        {
            Width = 300,
            Height = RowHeight * ViewportRows,
            Content = list,
        };

        window.Show();
        Pump();
        return (window, list, controller, group);
    }

    // Kilka przebiegów: wstawienia → układ → realizacja kontenerów → ewentualna korekta ekstentu.
    // ⚠ Jeden RunJobs mierzyłby stan sprzed układu, czyli nie ten, o który pytamy.
    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static int FirstRealizedIndex(ListBox list)
    {
        var panel = list.GetVisualDescendants().OfType<VirtualizingStackPanel>().FirstOrDefault();
        return panel?.FirstRealizedIndex ?? -1;
    }

    /// <summary>Minimalny węzeł — kontroler pyta o wszystko przez delegaty, więc tyle wystarczy.</summary>
    internal sealed class ProbeNode : INotifyPropertyChanged
    {
        public ProbeNode(string name, bool container)
        {
            Name = name;
            IsContainer = container;
        }

        public string Name { get; }
        public bool IsContainer { get; }
        public ObservableCollection<ProbeNode> Children { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Name;
    }
}
