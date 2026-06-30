using System;
using Avalonia.Controls;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class UserEditDialog : Window
{
    public UserEditDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is UserEditDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
        => Close((DataContext as UserEditDialogViewModel)?.Result);
}
