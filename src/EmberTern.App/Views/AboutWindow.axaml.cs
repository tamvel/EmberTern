using Avalonia.Controls;
using Avalonia.Interactivity;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// The About window — logo, product name, version, author, copyright. Nothing else: it is a product window,
/// not a diagnostic one (design §8), and the version comes from the assembly so a release never touches this
/// code.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>⚠ For Avalonia's runtime XAML loader / previewer. Shows no licence line.</summary>
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    /// <param name="license">The application's one licence service, so About names the licensee (design §17.2).</param>
    internal AboutWindow(EmberTern.App.Licensing.LicenseService? license)
    {
        InitializeComponent();
        DataContext = new AboutViewModel(license);
    }

    private async void OnThirdPartyNoticesClick(object? sender, RoutedEventArgs e)
        => await new ThirdPartyNoticesWindow().ShowDialog(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
