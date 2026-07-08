using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;

namespace EmberTern.App.Views;

/// <summary>
/// The shared Export dialog. Collects format / scope / options, then streams the export (file or
/// clipboard) with a live counter + Cancel. Owns the Save-file picker + clipboard (StorageProvider /
/// Clipboard stay in the view); the VM stays Avalonia-free and drives everything via delegates.
/// Returns the completed <see cref="ExportOutcome"/> or null on cancel.
/// </summary>
public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ExportDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
            vm.RequestSavePath = OnRequestSavePath;
            vm.WriteClipboard = OnWriteClipboard;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ExportDialogViewModel;
        Close(vm?.Result);
    }

    private async Task<string?> OnRequestSavePath(SaveFileRequest request)
    {
        var picker = StorageProvider;
        if (picker is null) return null;

        var file = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.Extension.TrimStart('.'),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(request.FilterName) { Patterns = new[] { "*" + request.Extension } },
                FilePickerFileTypes.All,
            },
        });

        return file?.Path.LocalPath;
    }

    private async Task OnWriteClipboard(string text)
    {
        var clipboard = Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public static Task<ExportOutcome?> ShowAsync(Window owner, ExportDialogViewModel viewModel)
    {
        var dlg = new ExportDialog { DataContext = viewModel };
        return dlg.ShowDialog<ExportOutcome?>(owner);
    }

    /// <summary>Convenience for host views (Activity Monitor, Table/View Data): resolve the owning
    /// window from any control in the tree and open the shared Export dialog for the given source.
    /// Returns the outcome, or null when the source is null / no owner window / the user cancelled.</summary>
    public static Task<ExportOutcome?> LaunchAsync(Visual host, IExportDataSource? source, ExportScope defaultScope)
    {
        if (source is null || host.FindAncestorOfType<Window>() is not { } owner)
        {
            return Task.FromResult<ExportOutcome?>(null);
        }
        return ShowAsync(owner, new ExportDialogViewModel(source, defaultScope));
    }
}
