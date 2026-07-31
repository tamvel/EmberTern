using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmberTern.App.Behaviors;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Settings.Export;

namespace EmberTern.App.Views;

/// <summary>
/// The export dialog's view: the save picker and the close, nothing else. The decisions (what may be exported,
/// whether the passphrases match, what the outcome was) all live in the view model.
/// </summary>
public partial class SettingsExportDialog : Window
{
    private readonly SettingsExportDialogViewModel _vm = null!;

    // Designer-only.
    public SettingsExportDialog()
    {
        InitializeComponent();
        GrowingDialogBehavior.Attach(this);
    }

    public SettingsExportDialog(SettingsPortability portability)
    {
        InitializeComponent();
        // The section list plus the passphrase group already reaches the bottom of a 768-high screen; the ceiling
        // keeps the dialog on the desktop and the body's ScrollViewer takes the overflow.
        GrowingDialogBehavior.Attach(this);
        _vm = new SettingsExportDialogViewModel(portability);
        DataContext = _vm;
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        await RunExportAsync();
    }

    private async Task RunExportAsync()
    {
        if (_vm is null || !_vm.CanExport)
        {
            return;
        }

        var path = await PickPathAsync();
        if (path is null)
        {
            return;
        }

        _vm.ExportTo(path);
    }

    /// <summary>The save picker, following the app's existing precedents (the data ExportDialog and the Script
    /// Executor). The extension is Core's constant — the file's identity is not a string this view gets to
    /// choose.</summary>
    private async Task<string?> PickPathAsync()
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var extension = SettingsExportFormat.FileExtension;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiStrings.SettingsExportTitle,
            SuggestedFileName = UiStrings.SettingsExportSuggestedName + extension,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(UiStrings.SettingsExportFileFilter)
                {
                    Patterns = new[] { "*" + extension },
                },
                FilePickerFileTypes.All,
            },
        });

        return file?.Path.LocalPath;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
