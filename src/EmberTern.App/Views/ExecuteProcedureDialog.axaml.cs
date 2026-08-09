using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmberTern.App.Behaviors;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Modal that collects Execute Procedure input values. Returns the ordered bound
/// values (null entry = SQL NULL) or null on Cancel — see
/// <see cref="ExecuteProcedureDialogViewModel.Result"/>.
/// </summary>
public partial class ExecuteProcedureDialog : Window
{
    public ExecuteProcedureDialog()
    {
        InitializeComponent();

        // ⭐ M4.4 / M‑5. Ten dialog nie ROŚNIE po otwarciu — parametry są znane przed `ShowDialog` — ale ma
        // ten sam objaw z innego powodu: jego świadomy limit 720 stoi POWYŻEJ obszaru roboczego ekranu
        // 1366×768 (zmierzone 696), więc procedura o wielu parametrach daje okno wyższe od ekranu i stopka
        // z przyciskiem Execute wychodzi poza dolną krawędź. ⭐ Sufit liczony z ekranu ogranicza go tylko
        // wtedy, gdy ekran jest mniejszy — na 1080 zostaje 720 (ratyfikowana reguła `min`).
        // ⚠ Ściśnięcie trafia we właściwe miejsce z konstrukcji: wiersz 3 jest gwiazdkowy i to on niesie
        // `ScrollViewer` z listą parametrów, więc nagłówek, baner walidacji i stopka zostają widoczne.
        GrowingDialogBehavior.Attach(this);

        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ExecuteProcedureDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ExecuteProcedureDialogViewModel;
        Close(vm?.Result);
    }

    // Select the whole time value on focus so typing replaces it — matches the
    // "fast keyboard entry" goal. Posted so it wins over the click's caret placement.
    private void OnTimeBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            Dispatcher.UIThread.Post(tb.SelectAll, DispatcherPriority.Background);
        }
    }

    // Parse + normalize the typed time (e.g. "8:30" → "08:30:00"); an invalid entry
    // flags the row (red border) and is re-checked when Execute is pressed.
    private void OnTimeBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ExecuteProcedureParamRowViewModel row })
        {
            row.CommitTime();
        }
    }

    public static Task<IReadOnlyList<object?>?> ShowAsync(Window owner, ExecuteProcedureDialogViewModel viewModel)
    {
        var dlg = new ExecuteProcedureDialog { DataContext = viewModel };
        return dlg.ShowDialog<IReadOnlyList<object?>?>(owner);
    }
}
