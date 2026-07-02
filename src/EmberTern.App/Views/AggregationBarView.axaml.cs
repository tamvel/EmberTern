using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>Shared aggregation bar view — bound to an <c>AggregationBarViewModel</c>.
/// Self-hides via <c>IsBarOpen</c>; each line computes via the host-supplied delegate.</summary>
public partial class AggregationBarView : UserControl
{
    public AggregationBarView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
