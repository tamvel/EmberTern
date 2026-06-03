using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace EmberTern.App.Views;

public partial class NewFolderDialog : Window
{
    public NewFolderDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            var box = this.FindControl<TextBox>("NameBox");
            if (box is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.SelectAll();
                }, DispatcherPriority.Background);
            }
        };
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("NameBox");
        var name = (box?.Text ?? string.Empty).Trim();
        Close(string.IsNullOrEmpty(name) ? null : name);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
