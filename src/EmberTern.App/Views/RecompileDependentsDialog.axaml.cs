using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// Post-compile "Recompile dependents?" dialog. Nothing runs automatically — the user picks
/// which dependents (all checked by default) and confirms, or skips. Close → returns the
/// dialog's <c>Result</c> (<see cref="RecompileDependentsResult"/> or null on Skip/Cancel).
/// </summary>
public partial class RecompileDependentsDialog : Window
{
    public RecompileDependentsDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is RecompileDependentsDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as RecompileDependentsDialogViewModel;
        Close(vm?.Result);
    }

    public static Task<RecompileDependentsResult?> ShowAsync(Window owner, RecompileDependentsDialogViewModel viewModel)
    {
        var dlg = new RecompileDependentsDialog { DataContext = viewModel };
        return dlg.ShowDialog<RecompileDependentsResult?>(owner);
    }
}
