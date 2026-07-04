using Avalonia.Controls;
using Avalonia.Threading;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class TraceMonitorTabView : UserControl
{
    private TraceMonitorTabViewModel? _vm;

    public TraceMonitorTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.ScrollToRowRequested -= OnScrollToRowRequested;

        _vm = DataContext as TraceMonitorTabViewModel;

        if (_vm is not null)
            _vm.ScrollToRowRequested += OnScrollToRowRequested;
    }

    // Follow-tail + lens "scroll to first occurrence": bring the requested row into view.
    private void OnScrollToRowRequested(TraceEventRowViewModel row)
        => Dispatcher.UIThread.Post(() => EventsGrid.ScrollIntoView(row, null), DispatcherPriority.Background);
}
