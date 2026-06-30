using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class NewRoleDialog : Window
{
    public NewRoleDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) =>
        {
            var box = this.FindControl<TextBox>("NameBox");
            if (box is not null)
                Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is NewRoleDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
        => Close((DataContext as NewRoleDialogViewModel)?.Result);

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            (DataContext as NewRoleDialogViewModel)?.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
