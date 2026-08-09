using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;

namespace EmberTern.App.Views;

/// <summary>
/// Foreign Key wizard window (Session 3 replacement for the Session 2 stub).
/// View-level entry point is the static <see cref="ShowAsync"/> overload;
/// the actual form state lives in <see cref="ForeignKeyDialogViewModel"/>.
/// Close → returns the dialog's <c>Result</c> (ForeignKeySpec? or null).
/// </summary>
public partial class ForeignKeyDialog : Window
{
    public ForeignKeyDialog()
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
        if (DataContext is ForeignKeyDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as ForeignKeyDialogViewModel;
        Close(vm?.Result);
    }

    /// <summary>
    /// Convenience static entry point used by
    /// <c>TableDetailTabView.OnCreateForeignKeyRequested</c>. Wires the VM
    /// with the supplied lookups, opens the dialog modally, returns the
    /// resulting <see cref="ForeignKeySpec"/> (or null on Cancel).
    /// </summary>
    public static System.Threading.Tasks.Task<ForeignKeySpec?> ShowAsync(
        Window owner,
        ForeignKeyDialogViewModel viewModel)
    {
        var dlg = new ForeignKeyDialog { DataContext = viewModel };
        return dlg.ShowDialog<ForeignKeySpec?>(owner);
    }
}
