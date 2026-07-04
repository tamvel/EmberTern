using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class TraceMonitorTabView : UserControl
{
    private TraceMonitorTabViewModel? _vm;
    private readonly TextEditor? _detailSql;

    public TraceMonitorTabView()
    {
        InitializeComponent();
        _detailSql = this.FindControl<TextEditor>("DetailSqlEditor");
        // Follow-tail auto-pauses when the user scrolls up, and re-arms at the bottom —
        // standard log/trace-viewer behaviour. The grid's inner ScrollViewer bubbles here.
        EventsGrid.AddHandler(ScrollViewer.ScrollChangedEvent, OnGridScrollChanged);
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnGridScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_vm is null || e.Source is not ScrollViewer sv) return;
        // Only vertical position matters; ignore when there's nothing to scroll.
        if (sv.Extent.Height <= sv.Viewport.Height) return;
        bool nearBottom = sv.Extent.Height - sv.Viewport.Height - sv.Offset.Y <= 24;
        if (_vm.FollowTail)
        {
            if (!nearBottom) _vm.FollowTail = false;   // manual scroll-up → pause
        }
        else if (nearBottom)
        {
            _vm.FollowTail = true;                     // scrolled back to bottom → resume
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollToRowRequested -= OnScrollToRowRequested;
            _vm.DetailSqlChanged -= OnDetailSqlChanged;
        }

        _vm = DataContext as TraceMonitorTabViewModel;

        if (_vm is not null)
        {
            _vm.ScrollToRowRequested += OnScrollToRowRequested;
            _vm.DetailSqlChanged += OnDetailSqlChanged;
            OnDetailSqlChanged(_vm.Detail.Sql);
        }
    }

    // Follow-tail + lens "scroll to first occurrence": bring the requested row into view.
    private void OnScrollToRowRequested(TraceEventRowViewModel row)
        => Dispatcher.UIThread.Post(() => EventsGrid.ScrollIntoView(row, null), DispatcherPriority.Background);

    // The detail SQL is pushed (not bound) into the read-only AvaloniaEdit — same pattern as
    // every other detail view. Text is already separator-cleaned by the VM.
    private void OnDetailSqlChanged(string sql)
    {
        if (_detailSql is not null && _detailSql.Text != sql) _detailSql.Text = sql;
    }

    // Firebird syntax highlighting + themed selection brush, swapped on theme toggle —
    // reuses App.FirebirdSyntax(Light)Name and the SelectionBrush resource (gotcha #19/#20).
    private void ApplyEditorTheme()
    {
        if (_detailSql is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
        _detailSql.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _detailSql.TextArea.SelectionBrush = brush;
        }
    }
}
