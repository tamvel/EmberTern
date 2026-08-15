using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using EmberTern.Licensing;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// Customers, their licences, and issuing.
///
/// <para>⭐ The two things that live in code-behind rather than in the view model are the two that are
/// pure platform: the theme toggle (a single button routing no value through a view model — the same
/// decision EmberTern records in Architecture rule 1) and the Save-As dialog, which is handed to the
/// view model as a delegate so that <c>ShellViewModel</c> keeps no Avalonia types.</para>
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireSaveFilePicker();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void WireSaveFilePicker()
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.SaveFilePicker = SuggestedName => PickSavePathAsync(SuggestedName);
        }
    }

    private async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the licence",
            SuggestedFileName = suggestedName,
            DefaultExtension = LicenseConstants.FileExtension.TrimStart('.'),
            FileTypeChoices =
            [
                new FilePickerFileType("EmberTern licence")
                {
                    Patterns = ["*" + LicenseConstants.FileExtension],
                },
            ],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    private void OnToggleTheme(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // ⭐ A single button switching one value, with nothing to route through a view model — and it is
        //    what makes "renders correctly in BOTH themes" something the operator can check in one click
        //    rather than something a screenshot has to promise.
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant =
                application.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
