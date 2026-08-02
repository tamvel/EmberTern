using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pasek zakładek — jedyna cecha, której nie da się ustalić czytaniem kodu ani buildem:
/// <b>czy wskaźnik aktywnej zakładki faktycznie MALUJE akcent.</b>
///
/// <para>⚠⚠ Ten plik istnieje z powodu konkretnej regresji (M3.1a, naprawionej w §19.2). Wskaźnik stał
/// w szablonie zakładki z lokalnym <c>Background="Transparent"</c>, a akcent nadawał mu setter stylu.
/// <b>Wartość lokalna bije setter stylu</b>, więc akcent nie malował się nigdy — a przeszło to przez
/// zielony build, 7088 zielonych testów, czysty smoke ORAZ render sondy wizualnej. Ostatni punkt jest
/// najważniejszy: sonda budowała wskaźnik, wiążąc mu tło BEZPOŚREDNIO dla zakładki aktywnej, czyli
/// <i>mierzyła inny mechanizm niż produkt</i> (pułapka 12 — „test potrafi mierzyć nie ten podmiot").
/// Obraz wychodził poprawny, bo sonda omijała dokładnie tę ścieżkę, którą zmieniła iteracja.</para>
///
/// <para>⭐ Dlatego asercja jest zrobiona przeciw MECHANIZMOWI, a nie przeciw zamiarowi: budujemy dwie
/// klasy CSS-owe dokładnie tak, jak robi to szablon (klasa <c>active-tab</c> na RODZICU, klasa
/// <c>tab-indicator</c> na dziecku) i sprawdzamy, co kontrolka ma po zastosowaniu stylów.</para>
///
/// <para>⚠ Najtańsza kontrolka, która może to unieść — gołe <see cref="Window"/>, nigdy <c>MainWindow</c>
/// (udokumentowany kształt zawieszający suite). To również mocniejsza asercja: styl docierający do
/// kontrolki bez XAML-a i bez code-behind mógł przyjść wyłącznie ze stylów aplikacji.</para>
///
/// <para>⚠ Dołącza do <see cref="HeadlessCollection"/> i nigdy nie zakłada własnego class fixture
/// (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TabStripPresentationTests
{
    private readonly HeadlessUnitTestSession _session;

    public TabStripPresentationTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async Task ActiveTabIndicator_PaintsTheAccent_AndAnInactiveOneDoesNot()
    {
        await _session.Dispatch(() =>
        {
            var activeIndicator = new Border { Classes = { "tab-indicator" } };
            var activeTab = new Border { Classes = { "active-tab" }, Child = activeIndicator };

            var idleIndicator = new Border { Classes = { "tab-indicator" } };
            var idleTab = new Border { Child = idleIndicator };

            var window = new Window
            {
                Content = new StackPanel { Children = { activeTab, idleTab } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Porównanie z katalogiem, nie z literałem — wartość akcentu wolno zmienić, wiązanie nie.
            // ⚠ Pędzla NIGDY nie szuka się bez wariantu motywu: `FindResource(key)` zwraca `UnsetValue`
            //   i test wywraca się na rzutowaniu zamiast powiedzieć, co jest nie tak (gotcha #250).
            Assert.True(
                window.TryFindResource("AccentBrush", window.ActualThemeVariant, out var accentValue),
                "Token `AccentBrush` nie jest w zasobach aplikacji.");
            var accent = Assert.IsAssignableFrom<IBrush>(accentValue);

            // ⭐ Asercja, która łapie regresję §19.2. Przed poprawką wskaźnik zakładki aktywnej był
            //   przezroczysty, bo wartość lokalna w szablonie biła ten setter.
            Assert.Equal(accent, activeIndicator.Background);

            // Druga połowa: stan spoczynkowy też pochodzi ze stylu, więc wolno go sprawdzić.
            // Nieaktywny wskaźnik nie może nieść akcentu — inaczej „aktywna" nic nie znaczy.
            Assert.NotEqual(accent, idleIndicator.Background);

            window.Close();
        }, default);
    }
}
