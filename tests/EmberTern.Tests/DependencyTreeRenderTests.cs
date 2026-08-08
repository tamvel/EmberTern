using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Jedyny fakt o wspólnym drzewie zależności, którego NIE da się ustalić czytaniem źródła: <b>czy ta kontrolka
/// w ogóle rysuje wiersze</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Ten plik powstał, bo jego brak przepuścił defekt do QA użytkownika.</b> M4.2b dostarczyło najpierw
/// pięciu strażników deduplikacji — i wszyscy pięciu czytają ŹRÓDŁO: „czy szablon jest jeden", „czy handler
/// nie wrócił", „czy ViewModel implementuje interfejs". Każdy był zielony, a zakładki „Zależności" były
/// PUSTE. To jest R16 w najczystszej postaci: <b>test zielony na złym ekranie jest gorszy niż brak testu</b>,
/// bo kupuje pewność, której nie pokrywa.
/// </para>
/// <para>
/// ⚠ Dlatego asercją jest <b>zrealizowany kontener wiersza</b>, a nie <c>ItemCount</c> ani sam
/// <c>ItemsSource</c>. To rozróżnienie jest całą różnicą między testem, który łapie defekt, a testem, który
/// go przepuszcza: w chwili tamtej awarii model był w porządku, a zniknęła PREZENTACJA. Odczyt
/// <c>ItemsSource</c> zwróciłby z powrotem własne wejście (#235).
/// </para>
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class DependencyTreeRenderTests
{
    private readonly HeadlessUnitTestSession _session;

    public DependencyTreeRenderTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    private static ObservableCollection<DependencyGroupNode> SampleTree() =>
    [
        new DependencyGroupNode
        {
            ObjectType = "Table",
            Children =
            [
                new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "ORDERS", ObjectType = "Table" } },
                new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "CUSTOMERS", ObjectType = "Table" } },
            ],
        },
    ];

    private static T Realize<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 400, Height = 300 };
        window.Show();
        Pump(window);
        return control;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(400, 300));
        window.Arrange(new Avalonia.Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// ⭐⭐ Asercja obejmuje CAŁY łańcuch, którego żaden strażnik źródłowy nie widzi: kolekcja → spłaszczenie
    /// przez <c>SidebarFlatController</c> → wiersz na ekranie → rozwinięcie ujawniające dzieci.
    /// <para>⚠ Liczby są POCHODNE od danych (1 grupa, potem 1 + 2 liście), a nie wpisane — dopisanie
    /// pozycji do <c>SampleTree</c> nie wymaga „poprawiania" testu, co jest różnicą między asercją
    /// o zachowaniu a asercją o bieżącym kształcie fikstury.</para>
    /// </summary>
    [Fact]
    public async Task TheSharedControl_RealizesRows_AndRevealsChildrenOnExpand()
    {
        await _session.Dispatch(() =>
        {
            var roots = SampleTree();
            var tree = new DependencyTreeView { ItemsSource = roots };
            var window = new Window { Content = tree, Width = 400, Height = 300 };
            window.Show();
            Pump(window);

            var collapsed = tree.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.True(collapsed == roots.Count,
                $"Zwinięte drzewo pokazuje {collapsed} wierszy zamiast {roots.Count} (po jednym na kategorię). "
                + "⚠ Zero oznacza, że kontrolka ma dane, ale ich nie renderuje — dokładnie ten defekt "
                + "M4.2b wypuściło raz do QA użytkownika przy pięciu zielonych strażnikach źródłowych.");

            roots[0].IsExpanded = true;
            Pump(window);

            var expanded = tree.GetVisualDescendants().OfType<ListBoxItem>().Count();
            var wanted = roots.Count + roots[0].Children.Count;
            Assert.True(expanded == wanted,
                $"Po rozwinięciu kategorii drzewo pokazuje {expanded} wierszy zamiast {wanted}. "
                + "⚠ To znaczy, że spłaszczanie nie reaguje na `IsExpanded` — kontroler obserwuje "
                + "`PropertyChanged` WĘZŁA, więc rozwinięcie musi być obserwowalną własnością węzła, "
                + "a nie stanem trzymanym obok niego.");

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ Wariant motywu dla barwy ikony bierze się z TEJ kontrolki. W 17 zmigrowanych kopiach stało tu
    /// <c>ElementName="RootControl"</c> wskazujące korzeń WIDOKU — nazwa elementu rozwiązuje się w zasięgu
    /// nazw widoku, którego wspólna kontrolka nie zna.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>To był największy pojedynczy element ryzyka migracji, i jego objaw byłby OPÓŹNIONY:</b> gdyby
    /// binding się nie rozwiązywał, ikony miałyby poprawną barwę przy starcie i traciłyby ją dopiero
    /// PO PRZEŁĄCZENIU MOTYWU — czyli defekt nie pokazałby się na pierwszym obejrzanym ekranie. Dlatego
    /// asercją jest ZMIANA barwy między wariantami, a nie sama nie-nullowość: niezerowy pędzel dostalibyśmy
    /// również wtedy, gdyby konwerter zwracał wartość domyślną i ignorował wariant.
    /// </remarks>
    [Fact]
    public async Task TheSharedControl_RecoloursItsIcons_WhenTheThemeVariantChanges()
    {
        await _session.Dispatch(() =>
        {
            var tree = new DependencyTreeView { ItemsSource = SampleTree() };
            var window = new Window { Content = tree, Width = 400, Height = 300 };
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Show();
            Pump(window);

            var icon = tree.GetVisualDescendants().OfType<SvgIcon>()
                .FirstOrDefault(i => i.Foreground is Avalonia.Media.ISolidColorBrush);
            Assert.True(icon is not null,
                "W zrealizowanym wierszu nie ma ikony z rozwiązaną barwą — szablon węzła nie został dopasowany "
                + "albo MultiBinding z wariantem motywu nie zadziałał.");

            var dark = (icon!.Foreground as Avalonia.Media.ISolidColorBrush)!.Color;

            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Pump(window);

            var light = (icon.Foreground as Avalonia.Media.ISolidColorBrush)?.Color;
            Assert.True(light is not null, "Barwa ikony przestała się rozwiązywać po przełączeniu motywu.");

            Assert.True(dark != light,
                $"Barwa ikony nie zmieniła się po przełączeniu motywu (obie {dark}). ⚠ To znaczy, że binding "
                + "wariantu motywu nie jest podpięty: ikona MA barwę, ale nie ŚLEDZI motywu — defekt, który "
                + "przy starcie wygląda poprawnie i ujawnia się dopiero po przełączeniu.");

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ REALNE WEJŚCIE KLAWIATUROWE przez pełny pipeline zdarzeń — nie wywołanie metody kontrolera.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠⚠ <b>Ten test istnieje, bo jego brak przepuścił defekt do QA użytkownika drugi raz w tym etapie.</b>
    /// Siedem testów reguły w <c>SidebarKeyboardNavigationTests</c> było zielonych i pozostaje poprawnych —
    /// sprawdzały <see cref="SidebarFlatController.Navigate"/>, czyli DECYZJĘ. Zielone były też strażniki
    /// źródłowe: wpięcie istnieje w obu drzewach. A klawisze w aplikacji nie działały, bo między jednym
    /// a drugim leży pytanie, którego nie zadawał żaden z nich: <b>czy zdarzenie w ogóle dociera do
    /// handlera</b>.
    /// </para>
    /// <para>
    /// ⭐ To jest R16 w trzeciej odsłonie tego etapu: „reguła jest poprawna" i „wpięcie istnieje" nie sumują
    /// się do „działa". Dlatego asercją jest skutek NACIŚNIĘCIA KLAWISZA, a nie wynik wywołania metody.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RightAndLeftArrow_ReachTheHandler_AndExpandOrCollapse()
    {
        await _session.Dispatch(() =>
        {
            var roots = SampleTree();
            var tree = new DependencyTreeView { ItemsSource = roots };
            var window = new Window { Content = tree, Width = 400, Height = 300 };
            window.Show();
            Pump(window);

            var list = tree.GetVisualDescendants().OfType<ListBox>().First();
            list.SelectedItem = tree.Rows![0];
            Pump(window);
            // ⚠ Fokus musi trafić na KONTENER WIERSZA, dokładnie jak po kliknięciu w aplikacji.
            //   `list.Focus()` w sesji headless nie ustawia fokusu wcale (zmierzone: listFocused=False,
            //   focusWithin=False, brak elementu z fokusem) — test wysyłałby wtedy klawisz donikąd
            //   i „nie działa" znaczyłoby „nie dostarczono", a nie „handler nie zadziałał".
            var container = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            container.Focus();
            Pump(window);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Pump(window);
            Assert.True(roots[0].IsExpanded,
                "→ nie rozwinęło węzła. ⚠ Reguła i wpięcie są sprawdzane osobno i oba bywają zielone — "
                + "tu pytanie brzmi, czy zdarzenie DOCIERA do handlera. Najczęstsza przyczyna: `ListBox` "
                + "obsługuje strzałki we własnym class handlerze i oznacza je jako obsłużone, a instancyjny "
                + "handler na bąbelkowaniu nie jest wtedy w ogóle wołany (#224).");

            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Pump(window);
            Assert.False(roots[0].IsExpanded, "← nie zwinęło rozwiniętego węzła.");

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⚠ Pytanie osobne od pierwszego: wiersz może się zrealizować i być PUSTY, jeżeli szablon nie zostanie
    /// dopasowany do typu węzła. Wtedy kontener istnieje, a użytkownik i tak nie widzi nic.
    /// </summary>
    [Fact]
    public async Task TheSharedControl_RendersTheNodeLabel()
    {
        await _session.Dispatch(() =>
        {
            var tree = Realize(new DependencyTreeView { ItemsSource = SampleTree() });

            var texts = tree.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToList();

            Assert.True(texts.Any(t => t.Contains("Table", StringComparison.Ordinal)),
                "Wiersz kategorii zrealizował się, ale nie wyrenderował etykiety — szablon nie został dopasowany "
                + "do `DependencyGroupNode`.\nZnalezione teksty: [" + string.Join(" | ", texts) + "]");
        }, default);
    }
}
