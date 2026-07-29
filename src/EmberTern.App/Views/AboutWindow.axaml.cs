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
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private async void OnThirdPartyNoticesClick(object? sender, RoutedEventArgs e)
        => await new ThirdPartyNoticesWindow().ShowDialog(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
