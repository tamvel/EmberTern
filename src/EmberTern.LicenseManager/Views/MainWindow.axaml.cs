using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Copies a generated value — a customer identifier, a licence id — to the clipboard.
    ///
    /// <para>⭐ Code-behind for the same reason the theme toggle is: the clipboard is pure platform, and
    /// routing a string through a view model would buy nothing. The value travels on the button's
    /// <see cref="Control.Tag"/>, so ONE handler serves every such action and a new one is a button
    /// rather than a method.</para>
    /// </summary>
    private async void OnCopyValueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || string.IsNullOrEmpty(value))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(value).ConfigureAwait(true);
        }

        if (DataContext is ShellViewModel shell)
        {
            shell.Message = StatusMessage.Success($"Copied {value} to the clipboard.");
        }
    }

    /// <summary>
    /// Dragging the window by its own title bar.
    ///
    /// <para>⚠ Buttons consume their own clicks, so this only fires on the bar's background.</para>
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Double-tapping the bar maximises or restores.
    ///
    /// <para>⚠ The theme toggle and the caption buttons live INSIDE the bar, so the event bubbles up from
    /// them too — double-clicking the theme icon must not also maximise the window. EmberTern's own
    /// titlebar carries the identical guard for the identical reason.</para>
    /// </summary>
    private void OnTitleBarDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is Visual source &&
            source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        ToggleMaximised();
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ToggleMaximised();

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// ⭐ The maximise glyph shows what the click will DO, so it becomes "restore" once the window is
    /// maximised — the same rule the theme toggle follows. ⚠ Driven by the window's own state rather than
    /// by the click, so it is right however the state changed (a system menu, a Windows snap gesture).
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty)
        {
            return;
        }

        if (this.FindControl<Avalonia.Controls.Shapes.Path>("MaxRestoreGlyph") is { } glyph &&
            Application.Current?.Resources.TryGetResource(
                WindowState == WindowState.Maximized ? "Icon.WindowRestore" : "Icon.WindowMaximize",
                null,
                out var geometry) == true &&
            geometry is Geometry data)
        {
            glyph.Data = data;
        }
    }

    /// <summary>
    /// P1-c · double-clicking a licence opens the preview of its newest artifact.
    ///
    /// <para>⭐⭐ It runs <see cref="ShellViewModel.InspectLatestCommand"/> — the SAME command the
    /// "Inspect latest" button runs, not a second path to the same screen. So a licence that was never
    /// issued still explains itself ("This licence has never been issued."), and anything that command
    /// learns later, the gesture learns with it. ⛔ There is no second implementation of Inspect, and
    /// there must not be one.</para>
    ///
    /// <para>⚠ Guarded on the row rather than on the list: <c>DoubleTapped</c> bubbles from the empty
    /// space below the last item as well, and a double-click on nothing opening the previously selected
    /// licence is a preview the operator did not ask for.</para>
    /// </summary>
    private void OnLicenseDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is not Visual source ||
            source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
        {
            return;
        }

        if (DataContext is ShellViewModel { InspectLatestCommand: { } inspect } &&
            inspect.CanExecute(null))
        {
            inspect.Execute(null);
        }
    }

    /// <summary>
    /// Opens the Storage window.
    ///
    /// <para>⭐ A window rather than a view (D‑4), owned by this one so it stays in front and closes with
    /// it. ⚠ Only one is ever open: a second Storage window would mean two views of the same folder and
    /// two half-typed passphrases.</para>
    /// </summary>
    private void OnOpenStorage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (_storage is { } existing)
        {
            existing.Activate();
            return;
        }

        _storage = new StorageWindow { DataContext = shell.Storage };
        _storage.Closed += (_, _) => _storage = null;
        _storage.Show(this);
    }

    private StorageWindow? _storage;

    /// <summary>
    /// Opens the Send licence window for the selected licence.
    ///
    /// <para>⭐⭐ <b>Every refusal is decided in the view model</b> (<see cref="ShellViewModel.PrepareSendLicence"/>)
    /// — no licence selected, e-mail not configured, the customer has no address, the licence was never
    /// issued. This handler opens a window or opens nothing; when nothing opens, the reason is already on
    /// the main window's message strip. ⛔ A window that opens and then says "actually, no" is a window the
    /// operator has to close to learn nothing.</para>
    ///
    /// <para>⚠ Only one at a time, for the reason Storage and Settings are: two send windows would be two
    /// previews of two compositions, and the operator could confirm the one they were not looking at.</para>
    /// </summary>
    private void OnSendLicence(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (_send is { } existing)
        {
            existing.Activate();
            return;
        }

        if (shell.PrepareSendLicence() is not { } model)
        {
            return;
        }

        _send = new SendLicenceWindow { DataContext = model };
        _send.Closed += (_, _) => _send = null;
        _send.Show(this);
    }

    private SendLicenceWindow? _send;

    /// <summary>
    /// Opens the application menu under the hamburger.
    ///
    /// <para>⭐ A mirror of EmberTern's own handler, including the half that is easy to leave out: a
    /// SECOND click on the button CLOSES the menu rather than re-opening it underneath itself. Without
    /// that, the button reads as broken the moment anyone clicks it twice.</para>
    /// </summary>
    private void OnAppMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        if (menu.IsOpen)
        {
            menu.Close();
            return;
        }

        menu.Open(button);
    }

    /// <summary>
    /// Opens the Settings window.
    ///
    /// <para>⭐ Owned by this window so it stays in front and closes with it, and only ever ONE — a
    /// second would mean two half-typed passwords racing to be the one that is saved.</para>
    ///
    /// <para>⚠ Does nothing when the view model has no settings, which happens only off Windows. ⛔ Not a
    /// disabled row: the PLATFORM decides whether the feature exists at all, and a control that is
    /// present but permanently dead teaches the operator nothing. ⚠ Deliberately unlike the `About` row,
    /// which IS a disabled placeholder — that one is disabled because nothing is behind it YET.</para>
    /// </summary>
    private void OnAppMenuSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Settings: { } settings })
        {
            return;
        }

        if (_settings is { } existing)
        {
            existing.Activate();
            return;
        }

        _settings = new SettingsWindow { DataContext = settings };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(this);
    }

    private SettingsWindow? _settings;

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
