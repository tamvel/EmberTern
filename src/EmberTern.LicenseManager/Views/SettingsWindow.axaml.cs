using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// Where the License Manager sends from.
///
/// <para>⭐ No code-behind beyond loading the markup. Everything this window does is a command on
/// <see cref="ViewModels.SettingsViewModel"/> — there is no file picker, no clipboard and no theme
/// toggle here, which are the three things this application deliberately keeps in code-behind because
/// they are pure platform.</para>
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>Creates the window.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ClampToWorkingArea();
        DataContextChanged += (_, _) => WireConfirm();
    }

    /// <summary>
    /// Hands the view model a way to ask a question.
    ///
    /// <para>⭐ The same arrangement <c>MainWindow</c> uses for the Save-As picker: the dialog is pure
    /// platform, so it arrives as a delegate and <see cref="SettingsViewModel"/> keeps no Avalonia types.
    /// ⚠ Wired on every <c>DataContextChanged</c> rather than in the constructor, because the context is
    /// assigned after construction.</para>
    /// </summary>
    private void WireConfirm()
    {
        if (DataContext is SettingsViewModel model)
        {
            model.Confirm = request =>
                new ConfirmDialog { DataContext = new ConfirmViewModel(request) }.ShowDialog<bool>(this);
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Keeps the window inside the screen it opens on.
    ///
    /// <para>⭐⭐ <b>It only ever SHRINKS, and it never grows.</b> The declared 620×760 is the one stable
    /// size — the whole point of this window's layout — and this exists solely so that a small or heavily
    /// scaled display cannot end up with a footer below the taskbar. On any screen with room, this changes
    /// nothing at all.</para>
    ///
    /// <para>⛔ It is NOT a fit-to-content mechanism. The window's height never depends on which page is
    /// showing; when a page is taller than the space available, the CONTENT scrolls (the
    /// <c>PageScroll</c> viewer), which is what keeps the navigation, the heading and the footer
    /// still.</para>
    ///
    /// <para>⚠ <see cref="Screens"/> has nothing to answer in a headless session, so every step is
    /// guarded and the method simply does nothing there — which is also why the layout tests measure the
    /// DECLARED size rather than a clamped one.</para>
    /// </summary>
    private void ClampToWorkingArea()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;

        // ⚠ WorkingArea is in PHYSICAL pixels and Height is in DIPs; comparing them directly would clamp
        //   to the wrong number on any display that is not at 100%.
        var availableHeight = (screen.WorkingArea.Height / scaling) - WindowChrome;
        var availableWidth = (screen.WorkingArea.Width / scaling) - WindowChrome;

        if (availableHeight > 0 && Height > availableHeight)
        {
            Height = Math.Max(MinHeight, availableHeight);
        }

        if (availableWidth > 0 && Width > availableWidth)
        {
            Width = Math.Max(MinWidth, availableWidth);
        }
    }

    /// <summary>A little air so the window does not sit flush against the working area's edges.</summary>
    private const double WindowChrome = 48;
}
