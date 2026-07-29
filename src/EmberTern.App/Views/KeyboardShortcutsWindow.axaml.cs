using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Browse every keyboard shortcut EmberTern declares — a read-only view of <c>CommandCatalog</c>. No editing;
/// re-binding a gesture may come one day and would start in the catalog, not here.
/// </summary>
public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        DataContext = new KeyboardShortcutsViewModel();

        // The search box is what a user reaches for first in a window whose only job is finding something.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }

    /// <summary>
    /// Back to <c>Global → Tab → Tree → Grid → Editor → alphabetical</c> — the order the user ratified, and the
    /// one the view model always holds underneath whatever the grid is currently showing.
    ///
    /// <para>⚠ Clearing each column's sort is not enough on its own: the grid keeps its own notion of the sorted
    /// column, so <c>ItemsSource</c> is re-assigned to make it rebuild its view from the ordered collection.</para>
    ///
    /// <para>⭐ <b>The button is ALWAYS visible, and that is the second attempt.</b> It first appeared only while
    /// a sort was active, driven by the grid's <c>Sorting</c> event — but that event also fires while the reset
    /// itself clears the columns, and it arrives late enough that a "we are resetting" flag does not cover it, so
    /// the button re-armed the moment it had done its job. Rather than chase the event's timing, the affordance
    /// became stateless: a small flat button in the footer costs nothing when unused, and it cannot lie about
    /// the grid's state. Same lesson as gotcha #240 — do not tie a control's visibility to the state its own
    /// action changes.</para>
    /// </summary>
    private void OnResetOrderClick(object? sender, RoutedEventArgs e)
    {
        foreach (var column in ShortcutsGrid.Columns)
        {
            column.ClearSort();
        }

        var items = ShortcutsGrid.ItemsSource;
        ShortcutsGrid.ItemsSource = null;
        ShortcutsGrid.ItemsSource = items;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
