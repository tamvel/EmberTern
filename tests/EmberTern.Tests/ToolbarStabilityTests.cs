using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stabilność układu paska narzędzi dokumentu (M3.2a / H‑3, <c>product-polish.md</c> §19.10 + §19.11).
///
/// <para>⭐ Pinowana jest tu <b>kotwica sekcji 1</b> — jedyny z czterech ruchów M3.2a, który przetrwał
/// odbiór na żywo. Sekcja trybu rezerwuje swój slot ZAWSZE, dzięki czemu akcja główna dokumentu
/// (Execute / Compile) startuje pod tym samym x we wszystkich 12 rodzajach zakładek.</para>
///
/// <para>⛔ Dwa piny, które tu były — wspólna podłoga szerokości Execute/Cancel i jej strażnik przed
/// cichym zestarzeniem się liczby — <b>zostały usunięte razem z mechanizmem, który opisywały</b>.
/// Użytkownik wycofał podłogę po obejrzeniu w działającej aplikacji: rozdymała akcję główną ponad jej
/// treść (§19.11). ⚠ Zmierzone przy tej okazji i wciąż aktualne jako opis stanu, nie jako defekt do
/// naprawienia: <b>Execute 156 px, Cancel 118 px</b>, więc F5 przesuwa pasek o 38 px i oddaje go po
/// zakończeniu zapytania. To jest ŚWIADOMIE ZAAKCEPTOWANY kompromis — nie pisz pinu, który by go
/// „naprawiał".</para>
///
/// <para>⚠ Najtańsza kontrolka, która to uniesie — gołe <see cref="Window"/>, nigdy <c>MainWindow</c>
/// (udokumentowany kształt zawieszający suite). Dołącza do <see cref="HeadlessCollection"/> i nigdy nie
/// zakłada własnego class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ToolbarStabilityTests
{
    /// <summary>
    /// Rezerwacja slotu sekcji 1 (tryb + jego separator). ⚠ Musi być zgodna z <c>MinWidth</c>
    /// kontenera sekcji 1 w <c>MainWindow.axaml</c>: 28 (przycisk ikonowy) + 6 (spacing) + 9 (separator).
    /// </summary>
    private const double ModeSectionSlot = 43;

    private readonly HeadlessUnitTestSession _session;

    public ToolbarStabilityTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

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

            // Pasmo chromy — ten sam kontener, w którym sekcja 1 stoi w produkcie.
            var window = new Window { Content = new Border { Classes = { "chrome" }, Child = strip } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ModeSectionSlot, emptySlot.Bounds.Width, precision: 3);

            window.Close();
        }, default);
    }
}
