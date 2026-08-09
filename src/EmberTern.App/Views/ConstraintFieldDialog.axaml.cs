using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using EmberTern.App.Completion;
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

        // The Live DDL preview is the app's shared read-only SQL surface: one call wires the semantic +
        // lexical highlighting layers, keeps them following the theme, and pushes the VM's DdlPreview in.
        if (this.FindControl<TextEditor>("DdlEditor") is { } ddl)
        {
            SqlEditorBehavior.AttachDdlPreview(ddl, this);
        }

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
