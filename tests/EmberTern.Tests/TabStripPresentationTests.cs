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

    /// <summary>
    /// ⭐⭐ M3.3a — pozostałe dwa stany zakładki aktywnej (tło kafelka, waga + kontrast etykiety) przeniosły
    /// się z <c>Border.Styles</c> szablonu do <c>ControlStyles.axaml</c>, więc dopiero teraz DA SIĘ je zapiąć.
    ///
    /// <para>⚠⚠ I ten test odpowiada na pytanie, którego nie da się rozstrzygnąć czytaniem kodu: kafelek
    /// zakładki niesie <b>lokalne</b> <c>Background="{DynamicResource PanelBrush}"</c>, a §19.2 udowodniła,
    /// że wartość lokalna potrafi zabić setter stylu — bezgłośnie. Dlatego atrapa odtwarza tę wartość lokalną
    /// <b>wiernie</b>; bez niej test mierzyłby łatwiejszy przypadek niż produkt (pułapka 12).</para>
    ///
    /// <para>⭐⭐ I NAPRAWDĘ TO ZŁAPAŁ. Pierwsza wersja przeniesienia zostawiła w szablonie lokalne
    /// <c>Background="{DynamicResource PanelBrush}"</c> i sam setter <c>.active-tab</c> — test zawiódł
    /// natychmiast (<c>#ff252526</c> zamiast <c>#ff1e1e1e</c>), czyli podmiana tła przestałaby działać
    /// dokładnie tak, jak wcześniej przestał działać wskaźnik. Rozwiązaniem jest recepta z §19.2:
    /// <b>oba stany jako setter</b>, zakotwiczone na klasie komponentu <c>workspace-tab</c>.</para>
    /// </summary>
    [Fact]
    public async Task ActiveTab_SwapsItsBackground_AndBoldensItsLabel_WithoutAnyLocalValueInTheTemplate()
    {
        await _session.Dispatch(() =>
        {
            // Atrapa wierna szablonowi: klasa komponentu + klasa stanu + etykieta w środku.
            // ⚠ Żadnego `Background` w kodzie — bo w szablonie też go już nie ma, i to jest cały punkt.
            var activeLabel = new TextBlock { Text = "PROC_X" };
            var activeTab = new Border { Classes = { "workspace-tab", "active-tab" }, Child = activeLabel };

            var idleLabel = new TextBlock { Text = "PROC_Y" };
            var idleTab = new Border { Classes = { "workspace-tab" }, Child = idleLabel };

            var window = new Window
            {
                Content = new StackPanel { Children = { activeTab, idleTab } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                window.TryFindResource("PanelBrush", window.ActualThemeVariant, out var panelValue),
                "Token `PanelBrush` nie jest w zasobach aplikacji.");
            Assert.True(
                window.TryFindResource("BackgroundBrush", window.ActualThemeVariant, out var docValue),
                "Token `BackgroundBrush` nie jest w zasobach aplikacji.");
            var panel = Assert.IsAssignableFrom<IBrush>(panelValue);
            var document = Assert.IsAssignableFrom<IBrush>(docValue);

            // Sam warunek testu ma sens tylko wtedy, gdy oba tokeny się różnią — inaczej asercja niżej
            // przechodziłaby bez względu na to, czy styl w ogóle zadziałał (R16: test zielony przy złym
            // wyglądzie jest gorszy niż brak testu).
            Assert.NotEqual(panel, document);

            // ⭐ Obie strony pochodzą ze stylu. Druga asercja jest równie ważna jak pierwsza: gdyby stan
            //   spoczynkowy wrócił do szablonu jako atrybut, to właśnie ona przestałaby cokolwiek znaczyć.
            Assert.Equal(document, activeTab.Background);
            Assert.Equal(panel, idleTab.Background);

            // Etykieta zakładki aktywnej — SemiBold i pełny kontrast; nieaktywna nietknięta.
            Assert.Equal(FontWeight.SemiBold, activeLabel.FontWeight);
            Assert.NotEqual(FontWeight.SemiBold, idleLabel.FontWeight);

            window.Close();
        }, default);
    }
}
