using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Add-Check-Constraint dialog window. Form state lives in
/// <see cref="CheckConstraintDialogViewModel"/>; Close → returns the dialog's
/// <c>Result</c> (<see cref="CheckConstraintSpec"/> or null).
/// </summary>
public partial class CheckConstraintDialog : Window
{
    public CheckConstraintDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CheckConstraintDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as CheckConstraintDialogViewModel;
        Close(vm?.Result);
    }

    public static System.Threading.Tasks.Task<CheckConstraintSpec?> ShowAsync(
        Window owner,
        CheckConstraintDialogViewModel viewModel)
    {
        var dlg = new CheckConstraintDialog { DataContext = viewModel };
        return dlg.ShowDialog<CheckConstraintSpec?>(owner);
    }
}
