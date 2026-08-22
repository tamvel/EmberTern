using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// The About window: what this application is, which version, and who wrote it.
///
/// <para>⭐ There is nothing to wire. Every value it shows is a projection of the built assembly through
/// <see cref="ViewModels.AboutViewModel"/>, so this window needs no platform delegate at all — unlike
/// Storage, Settings and Send licence, each of which has to be handed a picker or a clipboard.</para>
///
/// <para>⚠ The close handler exists rather than a command because closing a window is the window's own
/// business: a view model that could close itself would need a <c>RequestClose</c> event for one button —
/// the shape <c>SendLicenceViewModel</c> needs (it closes after an action) and this one does not.</para>
/// </summary>
public sealed partial class AboutWindow : Window
{
    /// <summary>Creates the window.</summary>
    public AboutWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
