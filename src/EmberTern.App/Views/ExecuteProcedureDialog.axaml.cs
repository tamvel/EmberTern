using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Modal that collects Execute Procedure input values. Returns the ordered bound
/// values (null entry = SQL NULL) or null on Cancel — see
/// <see cref="ExecuteProcedureDialogViewModel.Result"/>.
/// </summary>
public partial class ExecuteProcedureDialog : Window
{
    public ExecuteProcedureDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ExecuteProcedureDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ExecuteProcedureDialogViewModel;
        Close(vm?.Result);
    }

    public static Task<IReadOnlyList<object?>?> ShowAsync(Window owner, ExecuteProcedureDialogViewModel viewModel)
    {
        var dlg = new ExecuteProcedureDialog { DataContext = viewModel };
        return dlg.ShowDialog<IReadOnlyList<object?>?>(owner);
    }
}
