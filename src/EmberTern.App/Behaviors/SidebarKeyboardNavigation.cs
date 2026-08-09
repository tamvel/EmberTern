using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Behaviors;

/// <summary>
/// Nawigacja ←/→ dla każdej listy będącej spłaszczonym drzewem — <b>jedno wpięcie, jedna reguła</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ Powstało w M4.2b razem z <see cref="SidebarFlatController.Navigate"/>, na wprost sformułowany wymóg
/// użytkownika: skoro oba drzewa (połączenia i „Zależności") stoją na wspólnym mechanizmie, to nawigacja ma
/// być <b>wspólna</b>, a nie dopisana osobno do jednego z nich. Podział odpowiedzialności: <b>kontroler
/// decyduje</b> (zwija / rozwija / wskazuje wiersz), <b>to wpięcie tylko wykonuje</b> — zaznacza i przewija.
/// </para>
/// <para>
/// ⚠ <c>ListBox</c> obsługuje strzałki góra/dół natywnie i tego NIE ruszamy; ←/→ nie ma i to jest cała
/// luka, którą to zamyka. ⛔ Klawisz jest oznaczany jako obsłużony wyłącznie wtedy, gdy coś faktycznie
/// zrobił — inaczej odebralibyśmy ←/→ kontrolce, która mogłaby chcieć go użyć (np. przewijanie poziome).
/// </para>
/// <para>
/// ⚠⚠ Przewinięcie jest <b>odłożone na Dispatcher</b>, bo po rozwinięciu węzła projekcja dopiero się
/// przebudowuje — kontener docelowego wiersza w chwili obsługi klawisza jeszcze nie istnieje. To ten sam
/// kształt co gotcha #221; ⭐ ale zaznaczenie ustawiamy SYNCHRONICZNIE, bo przytrzymana strzałka czytałaby
/// wtedy stan sprzed skoku i „gubiła" kolejne naciśnięcia.
/// </para>
/// </remarks>
public static class SidebarKeyboardNavigation
{
    /// <param name="list">Lista renderująca <see cref="SidebarFlatController.Rows"/>.</param>
    /// <param name="navigate">
    /// Delegat do <see cref="SidebarFlatController.Navigate"/>. ⚠ DELEGAT, a nie sam kontroler, i to
    /// z dwóch niezależnych powodów: pasek boczny kapsułkuje swój kontroler za ViewModelem (wystawia
    /// <c>ToggleSidebarRow</c>, nie instancję), a drzewo zależności WYMIENIA go przy każdej podmianie
    /// kolekcji — przechwycona instancja byłaby tam po pierwszym przeładowaniu martwa. Jeden kształt
    /// obsługuje oba przypadki, więc nie powstaje drugie wpięcie.
    /// </param>
    public static void Attach(ListBox list, Func<SidebarRow, bool, SidebarRow?> navigate)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(navigate);

        // ⛔⛔ TUNEL, NIE BĄBELKOWANIE — i to jest cała naprawa defektu zgłoszonego w QA M4.2b.
        // `ListBox` obsługuje STRZAŁKI we własnym CLASS HANDLERZE (nawigacja po pozycjach) i oznacza je
        // jako obsłużone. Handler instancyjny (`list.KeyDown += …`) domyślnie NIE jest wtedy w ogóle
        // wołany, więc ←/→ nie docierało nigdzie — mimo że reguła w `SidebarFlatController.Navigate` była
        // poprawna, a wpięcie istniało w obu drzewach.
        // ⭐ To jest gotcha #224 na kontrolce listy zamiast na polu tekstowym: „handler jest wpięty" i „handler
        // jest wołany" to dwa różne fakty, a build i testy jednostkowe reguły są na tę różnicę ślepe.
        // ⚠ Tunel jest tu UZASADNIONY, a nie wygodny: `ListBox` genuinely claims te klawisze, czyli dokładnie
        // warunek, pod którym #224 dopuszcza tunel. ⛔ Nie zamieniać z powrotem na `+=`.
        list.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key is not (Key.Left or Key.Right)) return;
            if (list.SelectedItem is not SidebarRow row) return;

            var target = navigate(row, e.Key == Key.Right);
            if (target is not null)
            {
                list.SelectedItem = target;
                Dispatcher.UIThread.Post(() => list.ScrollIntoView(target), DispatcherPriority.Background);
            }

            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
}
