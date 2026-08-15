using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmberTern.App.Behaviors;
using EmberTern.App.Licensing;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// The activation window (design §5) — the customer's first and, for a year, only contact with licensing.
///
/// <para>⚠ The view owns exactly what needs a window: the file picker and the drop target. Everything that
/// can be decided without one lives in <see cref="LicenseActivationViewModel"/>, which is what lets the whole
/// flow be proven without a shell.</para>
/// </summary>
public partial class LicenseActivationWindow : Window
{
    private readonly LicenseActivationViewModel? _vm;

    /// <summary>
    /// ⚠ For Avalonia's runtime XAML loader / previewer only (AVLN3001). It leaves the <c>DataContext</c>
    /// unset deliberately: a parameterless path that built its own <c>LicenseService</c> would read — and one
    /// day write — the real licence file from a designer.
    /// </summary>
    public LicenseActivationWindow()
    {
        InitializeComponent();

        // ⚠ MEASURED, not assumed by group membership: this window genuinely grows AFTER opening — the
        //   MessageBanner appears on the first failed attempt, and Replace appears when a different licence
        //   id is offered. Avalonia extends a SizeToContent window downwards from where it already is, which
        //   is how a dialog's buttons end up under the bottom edge of the screen.
        GrowingDialogBehavior.Attach(this);

        DragDrop.SetAllowDrop(DropTarget, true);
        DropTarget.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropTarget.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    internal LicenseActivationWindow(LicenseService license)
        : this()
    {
        _vm = new LicenseActivationViewModel(license);
        DataContext = _vm;

        // ⭐ The window closes once the licence is installed AND re-verified from disk. Closing on the button
        //   press instead would hide a failed write behind a dismissed dialog.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicenseActivationViewModel.IsActivated) && _vm.IsActivated)
            {
                Close();
            }
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
        => e.DragEffects = e.DataTransfer?.TryGetFiles() is { Length: > 0 }
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null || e.DataTransfer?.TryGetFiles() is not { Length: > 0 } files) return;

        e.Handled = true;

        // ⚠ The FIRST file only: activation installs one licence, and silently choosing among several dropped
        //   files would be a guess. A folder has no readable local file path, so it falls through.
        if (files[0].TryGetLocalPath() is { } path)
        {
            _vm.OfferFile(path);
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        if (storage is null || _vm is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiStrings.LicenseActivationPickerTitle,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(UiStrings.LicenseActivationFileTypeName)
                {
                    Patterns = new[] { "*" + EmberTern.Licensing.LicenseConstants.FileExtension },
                },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 0) return;

        _vm.OfferFile(files[0].Path.LocalPath);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
