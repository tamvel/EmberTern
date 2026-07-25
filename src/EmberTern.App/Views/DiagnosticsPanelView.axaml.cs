using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>The Diagnostics panel (Stage 7 / S4) — bound to a <c>DiagnosticsPanelViewModel</c>. Lists the
/// DiagnosticsEngine's findings for the active SQL document (severity · code · message · location) in
/// engine order, or a readable empty state when there are none. Pure view: no analysis, no filtering, no
/// sorting.
/// <para>
/// S5 adds activation gestures only — the decisions (which document, which diagnostic, where the caret
/// goes) all belong to <see cref="Navigator"/>. This control just reports intent.
/// </para>
/// </summary>
public partial class DiagnosticsPanelView : UserControl
{
    /// <summary>The navigation target, set by the hosting view (S5). Internal because
    /// <see cref="DiagnosticsPanelHost"/> is: the panel is hosted from code-behind, never from XAML.
    /// Null leaves the panel a plain read-only list, exactly as it was in S4.</summary>
    internal DiagnosticsPanelHost? Navigator { get; set; }

    public DiagnosticsPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Double-click activates the row under the pointer (this codebase's established "open this"
    /// gesture). Reading the row from the tapped element rather than the selection makes a double-click on
    /// empty space below the list a no-op instead of yanking the caret to a stale selection.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is DiagnosticRowViewModel row)
        {
            Navigator?.ActivateRow(row);
            e.Handled = true;
        }
    }

    /// <summary>Right-click → "Quick Fix…" (Q5). Jumps to the finding and opens the same menu Ctrl+. and
    /// the bulb open; the item is simply inert when that finding has no fix, which is honest and needs no
    /// extra state to keep in sync.</summary>
    private void OnQuickFixClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DiagnosticRowViewModel row)
        {
            Navigator?.ShowCodeActionsForRow(row);
            e.Handled = true;
        }
    }

    /// <summary>Keyboard while the list has focus: Enter activates the selection (arrow keys only move it,
    /// as in every error list), and F8 / Shift+F8 navigate — the same bindings as in the editor, so the
    /// gesture doesn't change depending on where focus happens to be.</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when DataContext is DiagnosticsPanelViewModel vm:
                Navigator?.ActivateRow(vm.SelectedRow);
                e.Handled = true;
                break;
            case Key.F8 when e.KeyModifiers == KeyModifiers.None:
                Navigator?.Navigate(forward: true);
                e.Handled = true;
                break;
            case Key.F8 when e.KeyModifiers == KeyModifiers.Shift:
                Navigator?.Navigate(forward: false);
                e.Handled = true;
                break;
        }
    }
}
