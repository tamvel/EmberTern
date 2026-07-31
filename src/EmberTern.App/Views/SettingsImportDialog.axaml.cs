using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmberTern.App.Behaviors;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Settings.Export;

namespace EmberTern.App.Views;

/// <summary>
/// The import dialog's view: the open picker and three button clicks that forward to the view model's three
/// phases. It makes no decision about the file — phase one's verdict is what shows or hides the passphrase.
/// </summary>
public partial class SettingsImportDialog : Window
{
    private readonly SettingsImportDialogViewModel _vm = null!;

    // Designer-only.
    public SettingsImportDialog()
    {
        InitializeComponent();
        GrowingDialogBehavior.Attach(this);
    }

    public SettingsImportDialog(SettingsPortability portability)
    {
        InitializeComponent();
        // ⚠ This dialog GROWS: opening a file reveals the section list, and Avalonia extends a SizeToContent
        // window downwards from where it already is — which is how the Import button ended up under the bottom
        // edge of the screen (QA, etap 5b). See GrowingDialogBehavior.
        GrowingDialogBehavior.Attach(this);
        _vm = new SettingsImportDialogViewModel(portability);
        DataContext = _vm;
    }

    private async void OnPickFileClick(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        if (storage is null || _vm is null)
        {
            return;
        }

        var extension = SettingsExportFormat.FileExtension;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiStrings.SettingsImportTitle,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(UiStrings.SettingsExportFileFilter)
                {
                    Patterns = new[] { "*" + extension },
                },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        // ⭐ Phase one runs here, immediately, with no passphrase — so whatever the user picked has already been
        // identified and judged before the dialog offers to take a credential.
        _vm.PickFile(files[0].Path.LocalPath);
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e) => _vm?.Open();

    private void OnImportClick(object? sender, RoutedEventArgs e) => _vm?.ApplySelected();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
