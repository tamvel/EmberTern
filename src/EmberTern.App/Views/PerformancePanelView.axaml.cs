using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>The Performance panel — bound to a <c>PerformancePanelViewModel</c>. Phase 1
/// shows the verdict, the execution plan tree and the details drawer; findings and
/// table-access areas are placeholders filled in later phases.</summary>
public partial class PerformancePanelView : UserControl
{
    public PerformancePanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
