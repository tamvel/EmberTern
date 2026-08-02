using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EmberTern.App;
using EmberTern.App.Controls;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stabilność układu paska narzędzi dokumentu (M3.2a / H‑3, <c>product-polish.md</c> §19.10).
///
/// <para>⭐ Pinowana jest tu JEDNA rzecz, której nie da się ustalić czytaniem kodu: <b>czy para
/// Execute / Cancel renderuje się na tę samą szerokość</b>. Oba przyciski wykluczają się wzajemnie
/// (<c>ShowExecuteButton</c> = <c>IsQueryTabActive &amp;&amp; !IsExecuting</c>, <c>ShowCancelButton</c>
/// = ten sam warunek zanegowany), więc gdy ich szerokości się różnią, naciśnięcie F5 przesuwa całą
/// resztę paska — w trakcie patrzenia na wykonanie zapytania. To był drugi z dwóch defektów H‑3
/// i jedyny widoczny <i>w obrębie jednej zakładki</i>.</para>
///
/// <para>⚠ Asercja jest zrobiona przeciw RELACJI, nie przeciw wyglądowi: Execute jest z natury
/// wariantem szerszym (ikona + etykieta + <b>chip skrótu</b>), Cancel węższym (ikona + etykieta),
/// więc wspólna podłoga równa naturalnej szerokości Execute wyrównuje parę. Gdyby kiedykolwiek
/// urosła etykieta Cancel — albo skurczyła się etykieta Execute — założenie przestaje obowiązywać
/// i podłogę trzeba przeliczyć; wtedy ten test pada i mówi o ile.</para>
///
/// <para>⚠⚠ <see cref="ExecuteCancelFloor"/> jest DRUGĄ KOPIĄ liczby wpisanej w
/// <c>MainWindow.axaml</c> i to jest świadomy koszt. Podłogi nie da się odczytać z produktu bez
/// konstruowania <c>MainWindow</c>, a to jest udokumentowany kształt zawieszający suite (pułapka 4).
/// Kopia bez strażnika starzeje się CICHO (#284) — dlatego istnieje
/// <see cref="ExecuteCancelFloor_CoversBothVariants"/>, który zamienia to w awarię głośną: liczba
/// w teście musi pokryć zmierzoną szerokość obu wariantów, a komunikat podaje wartość do wpisania.</para>
///
/// <para>⚠ Najtańsza kontrolka, która to uniesie — gołe <see cref="Window"/>, nigdy <c>MainWindow</c>.
/// Dołącza do <see cref="HeadlessCollection"/> i nigdy nie zakłada własnego class fixture
/// (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ToolbarStabilityTests
{
    /// <summary>
    /// Wspólna podłoga szerokości pary Execute / Cancel. ⚠ Musi być zgodna z <c>MinWidth</c>
    /// obu przycisków w <c>MainWindow.axaml</c> (sekcja 2 paska narzędzi dokumentu).
    /// </summary>
    private const double ExecuteCancelFloor = 156;

    /// <summary>
    /// Rezerwacja slotu sekcji 1 (tryb + jego separator). ⚠ Musi być zgodna z <c>MinWidth</c>
    /// kontenera sekcji 1 w <c>MainWindow.axaml</c>: 28 (przycisk ikonowy) + 6 (spacing) + 9 (separator).
    /// </summary>
    private const double ModeSectionSlot = 43;

    private readonly HeadlessUnitTestSession _session;

    public ToolbarStabilityTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async Task ExecuteAndCancel_RenderToTheSameWidth_SoPressingF5NeverShiftsTheToolbar()
    {
        await _session.Dispatch(() =>
        {
            var (window, execute, cancel) = BuildPair(ExecuteCancelFloor);

            Assert.Equal(execute.Bounds.Width, cancel.Bounds.Width, precision: 3);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task ExecuteCancelFloor_CoversBothVariants()
    {
        await _session.Dispatch(() =>
        {
            // Bez podłogi każdy przycisk mierzy się na własną treść — to są liczby, które podłoga
            // musi pokryć, i to one decydują, czy stała powyżej jest jeszcze aktualna.
            var (window, execute, cancel) = BuildPair(floor: 0);
            var natural = System.Math.Max(execute.Bounds.Width, cancel.Bounds.Width);

            Assert.True(
                ExecuteCancelFloor >= natural,
                $"Podłoga pary Execute / Cancel jest za mała: Execute mierzy {execute.Bounds.Width}, "
                + $"Cancel {cancel.Bounds.Width}, a podłoga wynosi {ExecuteCancelFloor}. Ustaw ją na "
                + $"co najmniej {natural} TU ORAZ w MainWindow.axaml (oba przyciski sekcji 2).");

            // Druga połowa — założenie, na którym stoi cały mechanizm: Execute jest wariantem
            // szerszym, bo niesie chip skrótu. Gdy przestanie, podłoga przestaje wyrównywać parę
            // „od szerszego" i decyzja o jej wartości wymaga ponownego namysłu, a nie podbicia liczby.
            Assert.True(
                execute.Bounds.Width >= cancel.Bounds.Width,
                $"Cancel ({cancel.Bounds.Width}) jest szerszy niż Execute ({execute.Bounds.Width}) — "
                + "podłoga pary nie może już być wyprowadzona z szerokości Execute.");

            window.Close();
        }, default);
    }

    /// <summary>
    /// Kotwica sekcji 1: rezerwacja slotu musi działać RÓWNIEŻ wtedy, gdy sekcja jest pusta — bo to
    /// jest jedyny przypadek, dla którego w ogóle istnieje. Zakładki bez trybu (Generator, Domain,
    /// Package, Exception, Index, New Table, SQL) mają wszystkie pięć bramek fałszywe.
    ///
    /// <para>⚠ Test istnieje, bo „<c>MinWidth</c> jest w API" nie znaczy „<c>MinWidth</c> rezerwuje
    /// miejsce na kontenerze bez widocznych dzieci" (pułapka 10 — sprawdzenie obecności API daje
    /// fałszywe potwierdzenie). Gdyby nie działało, kotwica byłaby martwym zapisem przy zielonym
    /// buildzie, a pasek przesuwałby się dalej — czyli iteracja wyglądałaby na wykonaną.</para>
    /// </summary>
    [Fact]
    public async Task ModeSectionSlot_ReservesItsWidth_EvenWhenEveryToggleIsHidden()
    {
        await _session.Dispatch(() =>
        {
            // Dokładnie kształt z MainWindow.axaml: kontener sekcji 1 z ukrytymi dziećmi.
            var emptySlot = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                MinWidth = ModeSectionSlot,
                Children =
                {
                    new ToggleButton { Classes = { "icon" }, IsVisible = false },
                    new Border { Width = 1, IsVisible = false },
                },
            };

            // ⚠ Slot MUSI stać w poziomym StackPanelu, tak jak w produkcie. Wstawiony wprost do okna
            //   rozciąga się na całą jego szerokość (zmierzone: 1024 px) i test mierzyłby rozciąganie
            //   zamiast rezerwacji — pułapka 12, „test potrafi mierzyć nie ten podmiot".
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { emptySlot, new Button { Classes = { "icon" } } },
            };

            var window = new Window { Content = new Border { Classes = { "chrome" }, Child = strip } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ModeSectionSlot, emptySlot.Bounds.Width, precision: 3);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Buduje obie odmiany przycisku dokładnie tak, jak robi to sekcja 2 paska narzędzi: wewnątrz
    /// pasma <c>Border.chrome</c> (które rozstrzyga ich wysokość i zdejmuje podłogę szerokości akcji
    /// dialogowej) i z tymi samymi <c>UiStrings</c>, których używa produkt.
    /// </summary>
    private static (Window Window, Button Execute, Button Cancel) BuildPair(double floor)
    {
        var execute = new Button
        {
            Classes = { "primary" },
            MinWidth = floor,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SvgIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = UiStrings.ToolbarExecute, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Classes = { "shortcut-chip" }, Text = UiStrings.ToolbarExecuteHint },
                },
            },
        };

        var cancel = new Button
        {
            Classes = { "flat" },
            MinWidth = floor,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SvgIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = UiStrings.ToolbarCancel, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        // Pasmo chromy — ten sam kontener, w którym oba przyciski stoją w produkcie. Bez niego
        // obowiązywałaby geometria akcji dialogowej i pomiar dotyczyłby innego podmiotu (pułapka 12).
        var strip = new Border
        {
            Classes = { "chrome" },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { execute, cancel },
            },
        };

        var window = new Window { Content = strip };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, execute, cancel);
    }
}
