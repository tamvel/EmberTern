using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EmberTern.App.Behaviors;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// The Database Properties window. Presentation only — every decision (what may be edited, what Apply sends,
/// how a partial success reads) lives in <see cref="DatabasePropertiesViewModel"/>.
/// </summary>
public partial class DatabasePropertiesDialog : Window
{
    public DatabasePropertiesDialog()
    {
        InitializeComponent();
    }

    public DatabasePropertiesDialog(DatabasePropertiesViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // ⚠ The window is SizeToContent and its body grows when a message banner appears, so it can push its
        // own footer off the bottom edge — the measured defect GrowingDialogBehavior exists for (§16.7 /
        // gotcha #295). Attached for exactly that reason, not as decoration.
        GrowingDialogBehavior.Attach(this);

        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
