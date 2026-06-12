using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmberTern.App.Views;

public partial class NewTableTabView : UserControl
{
    public NewTableTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
