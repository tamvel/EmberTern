using System;
using Avalonia.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Views;

public partial class AddFieldDialog : Window
{
    public AddFieldDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AddFieldDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as AddFieldDialogViewModel;
        Close(vm?.Result);
    }

    public static FieldDefinition? ResultFromClose(object? closeArg)
        => closeArg as FieldDefinition;
}
