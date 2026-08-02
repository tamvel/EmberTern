using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EmberTern.App.Controls;

/// <summary>
/// A general searchable, filtering dropdown selector for large dictionaries
/// (domains, tables, views, procedures, generators, functions, …) — EmberTern's
/// reusable "ComboBox + search" control. Behaviour: a closed combo with a chevron
/// and a ✕ clear button; clicking the chevron (or the box) opens a top-level popup
/// (never clipped by the editor) with an in-popup filter box and a rich,
/// multi-column item list with an optional column header. The selection commits
/// ONLY on an explicit pick (click a row or Enter) — never on focus loss — so a
/// partial-typed filter can't change the value. Empty = empty field (no synthetic
/// "(none)"). Designed for one or more <see cref="Sections"/> (e.g. Domain |
/// Table column) rendered as tabs; with no sections it renders a single list from
/// <see cref="ItemsSource"/>.
/// </summary>
[TemplatePart("PART_Display", typeof(TextBlock))]
[TemplatePart("PART_Toggle", typeof(ToggleButton))]
[TemplatePart("PART_Clear", typeof(Button))]
[TemplatePart("PART_Popup", typeof(Popup))]
[TemplatePart("PART_PopupHost", typeof(ContentControl))]
public sealed class SearchableComboBox : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableComboBox, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchableComboBox, object?>(
            nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The committed item. Changes only on an explicit pick (click/Enter)
    /// or clear — never while typing the filter.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchableComboBox, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>Rich row template for the dropdown list (single-list mode).</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<SearchableComboBox, IDataTemplate?>(nameof(HeaderTemplate));

    /// <summary>Optional column-header row above the list (single-list mode). A
    /// template so each popup builds its own header instance (a shared Control can't
    /// be parented into multiple popups). Align columns with the rows via
    /// <c>Grid.SharedSizeGroup</c> (the popup root is a shared-size scope).</summary>
    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(DisplayMemberPath));

    /// <summary>Property used for the Contains filter + closed-box text (e.g. "Name").</summary>
    public string? DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public static readonly StyledProperty<string?> SelectionBoxTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(SelectionBoxText));

    /// <summary>Explicit closed-box text override (used when the committed item type
    /// varies by section, e.g. Domain vs Table column). When null, the display is
    /// derived from <see cref="DisplayMemberPath"/> on <see cref="SelectedItem"/>.</summary>
    public string? SelectionBoxText
    {
        get => GetValue(SelectionBoxTextProperty);
        set => SetValue(SelectionBoxTextProperty, value);
    }

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(Watermark));

    /// <summary>Placeholder shown when nothing is selected.</summary>
    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public static readonly StyledProperty<string?> FilterWatermarkProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(FilterWatermark), "Filter…");

    /// <summary>Placeholder shown in the in-popup filter box.</summary>
    public string? FilterWatermark
    {
        get => GetValue(FilterWatermarkProperty);
        set => SetValue(FilterWatermarkProperty, value);
    }

    public static readonly StyledProperty<double> MaxDropDownHeightProperty =
        AvaloniaProperty.Register<SearchableComboBox, double>(nameof(MaxDropDownHeight), 360);

    /// <summary>Max popup list height. Easy to raise for very large dictionaries.</summary>
    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<SearchableComboBox, bool>(nameof(IsDropDownOpen));

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Tabs/sources. Empty → single list from <see cref="ItemsSource"/>.</summary>
    public AvaloniaList<SearchableComboBoxSection> Sections { get; } = new();

    private TextBlock? _display;
    private ToggleButton? _toggle;
    private Button? _clear;
    private Popup? _popup;
    private ContentControl? _popupHost;
    private TextBox? _filterBox;
    private TabControl? _tabs;
    private readonly List<ListEntry> _lists = new();
    private bool _contentDirty = true;
    private bool _syncingToggle;

    private sealed record ListEntry(SearchableComboBoxSection? Section, ListBox List, IEnumerable? Source, string? DisplayPath);

    static SearchableComboBox()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c._contentDirty = true);
        ItemTemplateProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c._contentDirty = true);
        HeaderTemplateProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c._contentDirty = true);
        SelectedItemProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.UpdateDisplay());
        SelectionBoxTextProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.UpdateDisplay());
        WatermarkProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.UpdateDisplay());
        IsDropDownOpenProperty.Changed.AddClassHandler<SearchableComboBox>((c, e) => c.OnDropDownOpenChanged((bool)e.NewValue!));
    }

    public SearchableComboBox()
    {
        Sections.CollectionChanged += (_, _) => _contentDirty = true;
        Focusable = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _display = e.NameScope.Find<TextBlock>("PART_Display");
        _toggle = e.NameScope.Find<ToggleButton>("PART_Toggle");
        _clear = e.NameScope.Find<Button>("PART_Clear");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _popupHost = e.NameScope.Find<ContentControl>("PART_PopupHost");

        if (_toggle is not null)
            _toggle.IsCheckedChanged += (_, _) =>
            {
                if (_syncingToggle) return;
                IsDropDownOpen = _toggle.IsChecked == true;
            };
        if (_clear is not null)
            _clear.Click += (_, _) => { SelectedItem = null; };
        if (_popup is not null)
            _popup.Closed += (_, _) => { if (IsDropDownOpen) IsDropDownOpen = false; };

        UpdateDisplay();
    }

    // Open the dropdown on a click anywhere on the closed box (not just the chevron).
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;
        // Ignore clicks on the clear button (handled there).
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is { Name: "PART_Clear" })
            return;
        if (!IsDropDownOpen) IsDropDownOpen = true;
    }

    private void OnDropDownOpenChanged(bool open)
    {
        if (_toggle is not null)
        {
            _syncingToggle = true;
            _toggle.IsChecked = open;
            _syncingToggle = false;
        }
        if (_popup is null) return;

        if (open)
        {
            EnsurePopupContent();
            if (_filterBox is not null) _filterBox.Text = string.Empty;
            ApplyFilter(string.Empty);
            UpdateFilterVisibility();
            _popup.IsOpen = true;
            Dispatcher.UIThread.Post(() =>
            {
                _filterBox?.Focus();
                ScrollSelectedIntoView();
            }, DispatcherPriority.Loaded);
        }
        else
        {
            _popup.IsOpen = false;
        }
    }

    private void EnsurePopupContent()
    {
        if (!_contentDirty || _popupHost is null) return;
        _lists.Clear();
        _tabs = null;

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetIsSharedSizeScope(root, true);

        _filterBox = new TextBox
        {
            PlaceholderText = FilterWatermark,
            Margin = new Thickness(4),
            [Grid.RowProperty] = 0,
        };
        // ⚠ A filter IS a search field, and takes the same role (M2b step 11, QA: "the domain filter box
        // is too low"). Three consumers now — Settings, Global Search, every picker — which is what makes
        // `Size.ControlProminent` a role rather than one control's value.
        _filterBox.Classes.Add("search");
        _filterBox.AddHandler(TextBox.TextChangedEvent, (_, _) => ApplyFilter(_filterBox.Text ?? string.Empty));
        _filterBox.AddHandler(InputElement.KeyDownEvent, OnPopupKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        root.Children.Add(_filterBox);

        Control body;
        if (Sections.Count == 0)
        {
            body = BuildSectionBody(null, ItemsSource, ItemTemplate, HeaderTemplate, DisplayMemberPath);
        }
        else
        {
            var tabs = new TabControl { Padding = new Thickness(0) };
            foreach (var s in Sections)
            {
                Control tabContent;
                if (s.Content is { } custom)
                {
                    // Custom tab (e.g. two-pane Table-column picker) — self-filters and
                    // commits via ISearchableComboBoxContent; the shared filter box hides.
                    if (custom is ISearchableComboBoxContent c) c.CommitRequested = Commit;
                    tabContent = custom;
                }
                else
                {
                    tabContent = BuildSectionBody(s, s.ItemsSource, s.ItemTemplate, s.HeaderTemplate, s.DisplayMemberPath);
                }
                tabs.Items.Add(new TabItem { Header = s.Header, Content = tabContent, Tag = s });
            }
            // SelectionChanged BUBBLES — react ONLY to the TabControl's own tab switch, not to
            // an inner ListBox's selection bubbling up (that re-applied the filter → reset the
            // ListBox source → re-fired its SelectionChanged → ∞ loop / flicker). And defer the
            // tab-switch re-filter: changing a ListBox.ItemsSource synchronously inside the
            // TabControl's selection-model update throws ("Cannot change source while update is
            // in progress" → unhandled → silent crash).
            tabs.SelectionChanged += (_, e) =>
            {
                if (ReferenceEquals(e.Source, tabs))
                    Dispatcher.UIThread.Post(UpdateFilterVisibility);
            };
            _tabs = tabs;
            body = tabs;
        }
        body[Grid.RowProperty] = 1;
        root.Children.Add(body);

        _popupHost.Content = root;
        _contentDirty = false;
    }

    // The shared filter box only applies to built-in list tabs; a custom-content tab
    // (Table column) self-filters, so hide the shared box when it's active.
    private void UpdateFilterVisibility()
    {
        if (_filterBox is null) return;
        var activeSection = (_tabs?.SelectedItem as TabItem)?.Tag as SearchableComboBoxSection;
        var custom = activeSection?.Content is not null;
        _filterBox.IsVisible = !custom;
        if (!custom) ApplyFilter(_filterBox.Text ?? string.Empty);
    }

    private Control BuildSectionBody(SearchableComboBoxSection? section, IEnumerable? source, IDataTemplate? template, IDataTemplate? headerTemplate, string? path)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        if (headerTemplate?.Build(null) is { } header)
        {
            header[Grid.RowProperty] = 0;
            grid.Children.Add(header);
        }

        var list = new ListBox
        {
            ItemTemplate = template,
            MaxHeight = MaxDropDownHeight,
            [Grid.RowProperty] = 1,
        };
        list.AddHandler(InputElement.PointerReleasedEvent, OnListPointerReleased, handledEventsToo: true);
        grid.Children.Add(list);

        _lists.Add(new ListEntry(section, list, source, path));
        return grid;
    }

    private void ApplyFilter(string text)
    {
        foreach (var entry in _lists)
            entry.List.ItemsSource = FilterItems(entry.Source, entry.DisplayPath, text);
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item)
            Commit(item.DataContext);
    }

    private void OnPopupKeyDown(object? sender, KeyEventArgs e)
    {
        var list = ActiveList();
        switch (e.Key)
        {
            case Key.Escape:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Enter:
                if (list?.SelectedItem is { } sel) { Commit(sel); e.Handled = true; }
                break;
            case Key.Down:
                MoveSelection(list, +1); e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(list, -1); e.Handled = true;
                break;
        }
    }

    private static void MoveSelection(ListBox? list, int delta)
    {
        if (list?.ItemsSource is not IEnumerable src) return;
        var items = src.Cast<object>().ToList();
        if (items.Count == 0) return;
        var i = list.SelectedItem is null ? -1 : items.IndexOf(list.SelectedItem);
        i = Math.Clamp(i + delta, 0, items.Count - 1);
        list.SelectedItem = items[i];
        list.ScrollIntoView(items[i]);
    }

    private ListBox? ActiveList()
    {
        if (_lists.Count == 0) return null;
        if (_lists.Count == 1) return _lists[0].List;
        // Tabbed: the visible list is the one currently in the visual tree + visible.
        return _lists.FirstOrDefault(l => l.List.IsEffectivelyVisible)?.List ?? _lists[0].List;
    }

    private void Commit(object? item)
    {
        SelectedItem = item;
        IsDropDownOpen = false;
    }

    private void ScrollSelectedIntoView()
    {
        if (SelectedItem is null) return;
        var entry = _lists.FirstOrDefault(l => l.Source?.Cast<object>().Contains(SelectedItem) == true);
        if (entry is null) return;
        entry.List.SelectedItem = SelectedItem;
        entry.List.ScrollIntoView(SelectedItem);
    }

    private void UpdateDisplay()
    {
        if (_display is not null)
        {
            var text = SelectionBoxText;
            if (string.IsNullOrEmpty(text))
                text = SelectedItem is null ? null : DisplayText(SelectedItem, DisplayMemberPath);

            if (string.IsNullOrEmpty(text))
            {
                _display.Text = Watermark ?? string.Empty;
                _display.Opacity = 0.55;
            }
            else
            {
                _display.Text = text;
                _display.Opacity = 1.0;
            }
        }
        if (_clear is not null)
            _clear.IsVisible = SelectedItem is not null;
    }

    // ─── Pure helpers (unit-tested) ───────────────────────────────────────

    /// <summary>Case-insensitive Contains filter on the display member. Empty/blank
    /// text returns the full list. Null source → empty.</summary>
    internal static IReadOnlyList<object> FilterItems(IEnumerable? source, string? displayPath, string? text)
    {
        if (source is null) return Array.Empty<object>();
        var items = source.Cast<object>().ToList();
        if (string.IsNullOrWhiteSpace(text)) return items;
        return items.Where(i => DisplayText(i, displayPath).Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Reads <paramref name="path"/> off <paramref name="item"/> (or
    /// <c>ToString()</c> when no path) for filtering/display.</summary>
    internal static string DisplayText(object? item, string? path)
    {
        if (item is null) return string.Empty;
        if (string.IsNullOrEmpty(path)) return item.ToString() ?? string.Empty;
        var pi = item.GetType().GetProperty(path, BindingFlags.Public | BindingFlags.Instance);
        return pi?.GetValue(item)?.ToString() ?? string.Empty;
    }
}
