using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class GlobalSearchDialog : Window
{
    public GlobalSearchDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) =>
        {
            var box = this.FindControl<TextBox>("TermBox");
            if (box is not null)
                Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Background);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is GlobalSearchDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
        => Close((DataContext as GlobalSearchDialogViewModel)?.Result);

    private void OnTermKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            (DataContext as GlobalSearchDialogViewModel)?.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
