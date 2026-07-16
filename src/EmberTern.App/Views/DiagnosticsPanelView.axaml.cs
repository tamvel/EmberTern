using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

/// <summary>The Diagnostics panel (Stage 7 / S4) — bound to a <c>DiagnosticsPanelViewModel</c>. Lists the
/// DiagnosticsEngine's findings for the SQL editor's document (severity · code · message · location) in
/// engine order, or a readable empty state when there are none. Pure view: no analysis, no filtering, no
/// navigation (S5).</summary>
public partial class DiagnosticsPanelView : UserControl
{
    public DiagnosticsPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
