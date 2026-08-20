using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// A confirmation, answered with a boolean.
///
/// <para>⭐ The same arrangement EmberTern's <c>ConfirmDialog</c> uses: the view model raises
/// <see cref="ConfirmViewModel.RequestClose"/> and the window turns it into <c>Close(result)</c>, so the
/// view model never touches a window and the caller simply awaits
/// <c>ShowDialog&lt;bool&gt;</c>.</para>
///
/// <para>⚠ Closing by the title bar's ✕ returns <see langword="false"/> — Avalonia's own default for a
/// dialog closed without a result — which is the right answer for a destructive confirmation: anything
/// that is not an explicit yes is a no.</para>
/// </summary>
public sealed partial class ConfirmDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public ConfirmDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ConfirmViewModel model)
        {
            // ⚠ Unsubscribe first: DataContextChanged can fire more than once, and a second subscription
            //   would close the window twice.
            model.RequestClose -= OnRequestClose;
            model.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose() => Close((DataContext as ConfirmViewModel)?.Result ?? false);
}
