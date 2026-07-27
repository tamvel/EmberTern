using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>Code-behind for the one-line text prompt — <c>ConfirmDialog</c>'s shape, with a box.</summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Focus and select, so the pre-filled name is replaced by typing and kept by pressing Enter — the same
        // opening gesture NewFolderDialog uses.
        Opened += (_, _) =>
        {
            if (this.FindControl<TextBox>("TextBox") is not { } box) return;

            Dispatcher.UIThread.Post(
                () => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Background);
        };
    }

    /// <summary>Shows the prompt over <paramref name="owner"/> and returns the trimmed text, or <c>null</c> when
    /// cancelled. One place builds this dialog, so every caller gets the same behaviour.</summary>
    public static async Task<string?> AskAsync(Visual anchor, TextPromptRequest request)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner) return null;

        var dialog = new TextPromptDialog { DataContext = new TextPromptDialogViewModel(request) };
        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not TextPromptDialogViewModel vm) return;

        vm.RequestClose -= OnRequestClose;
        vm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose() => Close((DataContext as TextPromptDialogViewModel)?.Result);

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        (DataContext as TextPromptDialogViewModel)?.CancelCommand.Execute(null);
        e.Handled = true;
    }
}
