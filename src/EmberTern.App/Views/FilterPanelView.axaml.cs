using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>Shared filter panel view — bound to a <c>FilterPanelViewModel</c>.
/// Self-hides via <c>IsPanelOpen</c>; the host supplies the toggle + apply wiring.</summary>
public partial class FilterPanelView : UserControl
{
    public FilterPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
