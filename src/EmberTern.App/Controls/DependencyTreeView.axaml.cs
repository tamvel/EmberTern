using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Controls;

/// <summary>
/// Drzewo „Zależności" — jedna kontrolka dla wszystkich 17 wystąpień w dziewięciu edytorach obiektów,
/// na tym samym mechanizmie co drzewo połączenia w Metadata Explorerze.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Płaska <see cref="ListBox"/> nad <see cref="SidebarFlatController"/>, nie <c>TreeView</c></b> —
/// decyzja użytkownika podjęta po obejrzeniu obu wariantów w działającej aplikacji. Pełne uzasadnienie
/// i pomiar stoją w komentarzu na górze <c>DependencyTreeView.axaml</c>.
/// </para>
/// <para>
/// ⭐ Kontroler jest REUŻYTY, nie skopiowany: bierze wyłącznie delegaty, więc drzewo zależności podaje mu
/// swoje trzy odpowiedzi (co jest kontenerem, jakie ma dzieci, jak czytać i pisać rozwinięcie) i dostaje
/// gotową, spłaszczoną projekcję — razem z obserwowaniem zmian kolekcji, którą edytory podmieniają przy
/// każdym przeładowaniu zależności.
/// </para>
/// </remarks>
public partial class DependencyTreeView : UserControl
{
    /// <summary>Korzenie drzewa — kolekcja <see cref="DependencyGroupNode"/> z ViewModelu zakładki.</summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DependencyTreeView, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Spłaszczona projekcja, którą renderuje lista — <b>kolekcja kontrolera, nie jej kopia</b>.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>Pierwsza wersja trzymała tu WŁASNĄ kolekcję i odwzorowywała ją przez <c>Clear()</c> +
    /// ponowne dodanie — i to był defekt, nie szczegół.</b> <c>Clear()</c> kasuje <c>SelectedItem</c>
    /// listy, więc po każdym rozwinięciu węzła znikało zaznaczenie: mysz działała (klik zaznacza od nowa),
    /// ale <b>klawiatura przestawała</b>, bo kolejna strzałka nie miała już na czym pracować.
    /// <para>⚠ Objaw był mylący — wyglądał na „←/→ nie działa", a naprawdę znaczył „nie ma zaznaczenia".
    /// Złapał go dopiero test wysyłający DWA klawisze po sobie; pojedynczy klawisz przechodził.</para>
    /// ⭐ Lista wiąże się teraz wprost z kolekcją kontrolera, który rozwija węzeł SPLICE'em (wstawia dzieci),
    /// zamiast przebudowywać wszystko — więc instancje wierszy przeżywają, a z nimi zaznaczenie.
    /// </remarks>
    public static readonly DirectProperty<DependencyTreeView, ObservableCollection<SidebarRow>?> RowsProperty =
        AvaloniaProperty.RegisterDirect<DependencyTreeView, ObservableCollection<SidebarRow>?>(
            nameof(Rows), o => o.Rows);

    private ObservableCollection<SidebarRow>? _rows;

    public ObservableCollection<SidebarRow>? Rows
    {
        get => _rows;
        private set => SetAndRaise(RowsProperty, ref _rows, value);
    }

    private SidebarFlatController? _controller;

    public DependencyTreeView()
    {
        InitializeComponent();

        // ⭐ Nawigacja ←/→ z tego samego wpięcia, z którego korzysta drzewo połączenia — reguła żyje
        // w `SidebarFlatController.Navigate`, więc oba drzewa nie mogą się rozjechać.
        // ⚠ Przekazany jest DOSTAWCA kontrolera, nie kontroler: ta kontrolka wymienia go przy każdej
        // podmianie kolekcji, więc przechwycona instancja byłaby po pierwszym przeładowaniu martwa.
        if (this.FindControl<ListBox>("RowsList") is { } list)
        {
            Behaviors.SidebarKeyboardNavigation.Attach(list, (row, forward) => _controller?.Navigate(row, forward));
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            RebuildController();
        }
    }

    /// <summary>
    /// ⚠ Kontroler jest budowany OD NOWA przy każdej podmianie kolekcji, a stary — zwalniany.
    /// Edytory nie mutują listy w miejscu przy przeładowaniu zależności tylko czasem: część z nich
    /// czyści i wypełnia tę samą <c>ObservableCollection</c>, część podstawia nową. Kontroler radzi
    /// sobie z pierwszym przypadkiem sam (obserwuje <c>CollectionChanged</c>), ale drugiego nie
    /// zobaczy — dlatego przepięcie źródła musi go wymienić. ⛔ Bez <see cref="IDisposable.Dispose"/>
    /// stary kontroler zostałby zapisany na zdarzenia poprzedniej kolekcji.
    /// </summary>
    private void RebuildController()
    {
        _controller?.Dispose();
        _controller = null;
        Rows = null;

        if (ItemsSource is null) return;

        _controller = new SidebarFlatController(
            ItemsSource,
            childrenSelector: node => node is DependencyGroupNode g ? g.Children.Cast<object>() : null,
            isContainer: node => node is DependencyGroupNode,
            // ⚠ Kategoria PUSTA nie dostaje chevronu, ale nadal jest wierszem: `CategoryOrder` wypisuje
            //   każdą kategorię również wtedy, gdy nie ma zależności (parytet z IBExpertem), więc
            //   „jest kontenerem" i „ma dzieci" to tutaj naprawdę dwa różne pytania.
            hasChildren: node => node is DependencyGroupNode { Count: > 0 },
            isExpanded: node => node is DependencyGroupNode { IsExpanded: true },
            setExpanded: (node, value) =>
            {
                if (node is DependencyGroupNode g) g.IsExpanded = value;
            });

        // ⭐ Lista dostaje kolekcję kontrolera WPROST — bez kopii i bez odwzorowywania. Powód stoi przy
        // `RowsProperty`: każde odwzorowanie przez `Clear()` kasowało zaznaczenie przy rozwinięciu węzła.
        Rows = _controller.Rows;
    }

    private void OnChevronClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarRow row })
        {
            _controller?.Toggle(row);
            e.Handled = true;
        }
    }

    /// <summary>
    /// ⚠ Nawigator rozwiązywany jest z <see cref="StyledElement.DataContext"/> kontrolki, czyli z ViewModelu
    /// zakładki — dokładnie tam, gdzie sięgało dawne <c>_currentVm</c> w dziewięciu kopiach code-behind.
    /// Gdy DataContext nie implementuje interfejsu, dwuklik jest ignorowany; ⛔ bez wyjątku, bo kontrolka
    /// jest elementem PREZENTACJI i nie ma prawa przewrócić widoku z powodu niepodpiętej nawigacji.
    /// </summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source) return;

        // Wiersz spod kursora, a nie zaznaczenie listy: dwuklik może paść na wiersz, który nie jest
        // zaznaczony (kształt gotchy #16/#99 — cel to element pod kursorem, nigdy `SelectedItem`).
        var container = source.FindAncestorOfType<ListBoxItem>();
        if (container?.DataContext is SidebarRow { Node: DependencyLeafNode leaf }
            && DataContext is IDependencyNavigator navigator)
        {
            navigator.RequestOpen(leaf);
            e.Handled = true;
        }
    }
}
