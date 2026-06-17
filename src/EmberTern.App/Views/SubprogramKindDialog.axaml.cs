using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>Tiny modal asking whether a new local subprogram is a PROCEDURE or a
/// FUNCTION. Returns "PROCEDURE" / "FUNCTION" / null (cancel).</summary>
public partial class SubprogramKindDialog : Window
{
    public SubprogramKindDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnProcedureClick(object? sender, RoutedEventArgs e) => Close("PROCEDURE");
    private void OnFunctionClick(object? sender, RoutedEventArgs e) => Close("FUNCTION");
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    public static Task<string?> ShowAsync(Window owner)
        => new SubprogramKindDialog().ShowDialog<string?>(owner);
}
