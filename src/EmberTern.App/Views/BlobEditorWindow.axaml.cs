using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>
/// Modal text editor for BLOB SUB_TYPE 1 (text) cells. For binary BLOBs the
/// caller passes a read-only descriptor (e.g. "Binary BLOB (N bytes)") and
/// <paramref name="readOnly"/> = true so the user can inspect but not commit.
/// </summary>
public partial class BlobEditorWindow : Window
{
    public BlobEditorWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Opens the editor as a modal dialog. Returns the new text on OK, or null
    /// on Cancel / close. Read-only mode hides editing but still surfaces the
    /// content for inspection.
    /// </summary>
    public static async Task<string?> ShowAsync(Window owner, string? currentValue, bool readOnly)
    {
        var win = new BlobEditorWindow();
        var box = win.FindControl<TextBox>("BlobText");
        if (box is not null)
        {
            box.Text = currentValue ?? string.Empty;
            box.IsReadOnly = readOnly;
        }
        var result = await win.ShowDialog<string?>(owner).ConfigureAwait(true);
        return result;
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("BlobText");
        Close(box?.Text ?? string.Empty);
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
