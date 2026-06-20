using System;
using Avalonia.Controls;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class ChoiceDialog : Window
{
    public ChoiceDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ChoiceDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ChoiceDialogViewModel;
        // Result is the chosen option Id, or null when dismissed (X) — callers
        // treat null as cancel.
        Close(vm?.Result);
    }
}
