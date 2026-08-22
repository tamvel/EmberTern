using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.LicenseManager.Views;

/// <summary>The License Manager's first run: create the signing key, or unlock it.</summary>
public sealed partial class UnlockWindow : Window
{
    /// <summary>Creates the window.</summary>
    public UnlockWindow()
    {
        InitializeComponent();

        // The operator's first action is always to type the passphrase, so the caret starts there.
        Opened += (_, _) => this.FindControl<TextBox>("PassphraseBox")?.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
