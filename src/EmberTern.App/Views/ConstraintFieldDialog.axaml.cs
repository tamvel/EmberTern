using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Add-Primary-Key / Add-Unique dialog window. Form state lives in
/// <see cref="ConstraintFieldDialogViewModel"/>; Close → returns the dialog's
/// <c>Result</c> (<see cref="ConstraintFieldSpec"/> or null). Same shape as
/// <see cref="ForeignKeyDialog"/>.
/// </summary>
public partial class ConstraintFieldDialog : Window
{
    public ConstraintFieldDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ConstraintFieldDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ConstraintFieldDialogViewModel;
        Close(vm?.Result);
    }

    public static System.Threading.Tasks.Task<ConstraintFieldSpec?> ShowAsync(
        Window owner,
        ConstraintFieldDialogViewModel viewModel)
    {
        var dlg = new ConstraintFieldDialog { DataContext = viewModel };
        return dlg.ShowDialog<ConstraintFieldSpec?>(owner);
    }
}
