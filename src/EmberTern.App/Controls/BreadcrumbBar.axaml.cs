using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.Controls;

/// <summary>
/// A reusable horizontal breadcrumb bar: a path of clickable segments separated by "›", the selected one
/// emphasised. Deliberately <b>generic</b> — each item's <see cref="object.ToString"/> is the segment label,
/// and selection is reported through <see cref="SelectedIndex"/> (two-way) — so it carries no domain
/// knowledge. Its first consumer is the debugger's call stack (spec §5.2 — "breadcrumbs mirror the stack"),
/// but any editor surface can reuse it. Colours are theme tokens only (rule: no literals in a view).
/// </summary>
public partial class BreadcrumbBar : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<BreadcrumbBar, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<BreadcrumbBar, int>(
            nameof(SelectedIndex), defaultValue: -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private readonly ObservableCollection<BreadcrumbSegment> _segments = new();
    private INotifyCollectionChanged? _observed;

    public BreadcrumbBar()
    {
        InitializeComponent();
        var items = this.FindControl<ItemsControl>("Items");
        if (items is not null) items.ItemsSource = _segments;
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    /// <summary>The path items; each item's <see cref="object.ToString"/> is its label.</summary>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The selected segment index (two-way). -1 = none.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            HookItems(change.GetOldValue<IEnumerable?>(), change.GetNewValue<IEnumerable?>());
            Rebuild();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            UpdateSelection();
        }
    }

    // Re-subscribe to the source's change notifications so a live collection (the debugger replaces its
    // breadcrumb contents in place) rebuilds the visible segments.
    private void HookItems(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (_observed is not null) _observed.CollectionChanged -= OnSourceChanged;
        _observed = newValue as INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _segments.Clear();
        if (ItemsSource is { } source)
        {
            int index = 0;
            foreach (var item in source)
            {
                _segments.Add(new BreadcrumbSegment(index, item?.ToString() ?? string.Empty, showSeparator: index > 0)
                {
                    IsSelected = index == SelectedIndex,
                });
                index++;
            }
        }
    }

    private void UpdateSelection()
    {
        foreach (var s in _segments) s.IsSelected = s.Index == SelectedIndex;
    }

    private void OnCrumbClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BreadcrumbSegment segment) SelectedIndex = segment.Index;
    }
}

/// <summary>One rendered breadcrumb segment (the bar's internal item view). <see cref="IsSelected"/> is
/// mutable so selection re-highlights without rebuilding the bar.</summary>
public sealed partial class BreadcrumbSegment : ObservableObject
{
    public BreadcrumbSegment(int index, string text, bool showSeparator)
    {
        Index = index;
        Text = text;
        ShowSeparator = showSeparator;
    }

    /// <summary>The segment's position (0 = first / outermost).</summary>
    public int Index { get; }

    /// <summary>The segment label.</summary>
    public string Text { get; }

    /// <summary>True for every segment except the first — draws the "›" separator before it.</summary>
    public bool ShowSeparator { get; }

    /// <summary>True when this is the selected segment (emphasised).</summary>
    [ObservableProperty]
    private bool _isSelected;
}
