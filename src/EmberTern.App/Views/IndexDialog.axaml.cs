using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Add-Index dialog window. Form state lives in
/// <see cref="IndexDialogViewModel"/>; Close → returns the dialog's
/// <c>Result</c> (<see cref="IndexSpec"/> or null). Same shape as the
/// constraint dialogs.
/// </summary>
public partial class IndexDialog : Window
{
    public IndexDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is IndexDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as IndexDialogViewModel;
        Close(vm?.Result);
    }

    public static System.Threading.Tasks.Task<IndexSpec?> ShowAsync(
        Window owner,
        IndexDialogViewModel viewModel)
    {
        var dlg = new IndexDialog { DataContext = viewModel };
        return dlg.ShowDialog<IndexSpec?>(owner);
    }
}
