using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.Core.Settings;

namespace EmberTern.App.Behaviors;

/// <summary>
/// Attached behavior that gives any <see cref="DataGrid"/> persistent column layout —
/// order, widths, and an auto-fit toggle — backed by <see cref="GridProfileStore"/>.
/// Opt-in per grid via <c>behaviors:GridLayoutBehavior.GridId="..."</c>; no per-view
/// code-behind or XAML beyond that one attribute.
///
/// Behaviour:
///  • Order is always remembered (saved on reorder / detach / window close, restored on
///    attach). New columns append at the end; removed columns are dropped from the order.
///  • AutoFitColumns (default true): columns size to content and manual widths are NOT
///    remembered. When toggled off, manual widths are captured (from ActualWidth) and
///    restored on the next launch.
///  • The "Auto-fit columns" toggle is appended programmatically to each grid's
///    ContextMenu (one is created if the grid has none) — keeping the UX consistent
///    across every supported grid with zero duplicated markup.
///
/// The shared <see cref="Store"/> is assigned once by the host window from the VM's
/// settings location (so tests never touch the real %AppData%). Null Store = no-op.
/// </summary>
public static class GridLayoutBehavior
{
    // Set by MainWindow once the VM attaches (settings dir + protector). Static because
    // the app is single-window; null in design/headless contexts → the behavior no-ops.
    public static GridProfileStore? Store { get; set; }

    public static readonly AttachedProperty<string?> GridIdProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, string?>("GridId", typeof(GridLayoutBehavior));

    // Live per-grid state, so the window can flush every attached grid on close (the
    // reliable moment to capture final ActualWidth before the tree is torn down).
    private static readonly List<GridLayoutState> Live = new();

    static GridLayoutBehavior()
    {
        GridIdProperty.Changed.AddClassHandler<DataGrid>(OnGridIdChanged);
    }

    public static void SetGridId(DataGrid grid, string? value) => grid.SetValue(GridIdProperty, value);

    public static string? GetGridId(DataGrid grid) => grid.GetValue(GridIdProperty);

    // Saves every currently-attached grid. Called from the window's Closing handler.
    public static void FlushAll()
    {
        foreach (var state in Live.ToArray())
        {
            state.Save();
        }
    }

    private static void OnGridIdChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        // Detach any prior state for this grid (GridId reassignment is not expected, but
        // be defensive — don't leak event subscriptions).
        var existing = Live.FirstOrDefault(s => ReferenceEquals(s.Grid, grid));
        existing?.Dispose();

        if (e.NewValue is string id && !string.IsNullOrWhiteSpace(id))
        {
            var state = new GridLayoutState(grid, id);
            Live.Add(state);
        }
    }

    private sealed class GridLayoutState : IDisposable
    {
        public DataGrid Grid { get; }

        private readonly string _id;
        private bool _applying;
        private bool _applyScheduled;
        private bool _menuAdded;
        private MenuItem? _autoFitItem;

        public GridLayoutState(DataGrid grid, string id)
        {
            Grid = grid;
            _id = id;

            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
            grid.ColumnReordered += OnColumnReordered;
            grid.Columns.CollectionChanged += OnColumnsChanged;

            // GridId is set during XAML parse (before the grid attaches), so OnAttached
            // drives the first menu-build + apply. If it's set after attach, OnAttached
            // simply won't have fired yet — IsLoaded covers that late-binding case.
            if (grid.IsLoaded)
            {
                EnsureMenuItem();
                ScheduleApply();
            }
        }

        public void Dispose()
        {
            Grid.AttachedToVisualTree -= OnAttached;
            Grid.DetachedFromVisualTree -= OnDetached;
            Grid.ColumnReordered -= OnColumnReordered;
            Grid.Columns.CollectionChanged -= OnColumnsChanged;
            Live.Remove(this);
        }

        private void OnAttached(object? sender, EventArgs e)
        {
            EnsureMenuItem();
            ScheduleApply();
        }

        private void OnDetached(object? sender, EventArgs e)
        {
            Save();
        }

        private void OnColumnReordered(object? sender, EventArgs e)
        {
            if (_applying) return;
            Save();
        }

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Dynamic grids (query results, data preview) rebuild their columns in
            // code-behind. Re-apply the saved layout once the rebuild settles. Our own
            // DisplayIndex/Width writes don't mutate the Columns collection, so this
            // won't loop — but guard with _applying anyway.
            if (_applying) return;
            ScheduleApply();
        }

        // Coalesce multiple triggers (e.g. a Clear+Add column rebuild) into one apply
        // pass at Background priority, after layout/code-behind has finished.
        private void ScheduleApply()
        {
            if (_applyScheduled) return;
            _applyScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _applyScheduled = false;
                ApplyProfile();
            }, DispatcherPriority.Background);
        }

        private GridProfile LoadProfile()
            => Store?.Get(_id) ?? new GridProfile { GridId = _id, AutoFitColumns = true };

        private static string HeaderText(DataGridColumn column) => column.Header?.ToString() ?? string.Empty;

        private DataGridColumn? FindColumn(string header)
            => Grid.Columns.FirstOrDefault(c => string.Equals(HeaderText(c), header, StringComparison.Ordinal));

        private void ApplyProfile()
        {
            if (Store is null || Grid.Columns.Count == 0) return;

            var profile = LoadProfile();
            _applying = true;
            try
            {
                ApplyOrder(profile);
                ApplyWidths(profile);
                if (_autoFitItem is not null)
                {
                    _autoFitItem.IsChecked = profile.AutoFitColumns;
                }
            }
            catch (Exception)
            {
                // Defensive: a malformed profile or a DisplayIndex edge case must never
                // crash the UI. Worst case the grid keeps its default layout.
            }
            finally
            {
                _applying = false;
            }
        }

        private void ApplyOrder(GridProfile profile)
        {
            var current = Grid.Columns.Select(HeaderText).ToList();
            var ordered = GridLayoutOrdering.OrderedNames(current, profile.ColumnOrder);
            for (int target = 0; target < ordered.Count; target++)
            {
                var column = FindColumn(ordered[target]);
                if (column is not null && column.DisplayIndex != target)
                {
                    column.DisplayIndex = target;
                }
            }
        }

        private void ApplyWidths(GridProfile profile)
        {
            if (profile.AutoFitColumns)
            {
                // Reset to content sizing; don't honour any stale saved widths.
                foreach (var column in Grid.Columns)
                {
                    if (!column.Width.IsAuto)
                    {
                        column.Width = DataGridLength.Auto;
                    }
                }
                return;
            }

            foreach (var column in Grid.Columns)
            {
                if (profile.ColumnWidths.TryGetValue(HeaderText(column), out var px) && px > 0)
                {
                    column.Width = new DataGridLength(px);
                }
            }
        }

        public void Save()
        {
            if (Store is null || Grid.Columns.Count == 0) return;

            var profile = LoadProfile();

            profile.ColumnOrder = Grid.Columns
                .OrderBy(c => c.DisplayIndex)
                .Select(HeaderText)
                .Where(h => !string.IsNullOrEmpty(h))
                .ToList();

            if (!profile.AutoFitColumns)
            {
                var widths = new Dictionary<string, double>();
                foreach (var column in Grid.Columns)
                {
                    var header = HeaderText(column);
                    if (!string.IsNullOrEmpty(header) && column.ActualWidth > 0)
                    {
                        widths[header] = column.ActualWidth;
                    }
                }
                profile.ColumnWidths = widths;
            }

            try
            {
                Store.Save(profile);
            }
            catch (Exception)
            {
                // Persistence is best-effort — never block a reorder / tab-close / shutdown
                // on a transient settings.dat I/O hiccup.
            }
        }

        private void EnsureMenuItem()
        {
            if (_menuAdded) return;

            Grid.ContextMenu ??= new ContextMenu();
            var menu = Grid.ContextMenu;

            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            _autoFitItem = new MenuItem
            {
                Header = UiStrings.GridAutoFitColumns,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = LoadProfile().AutoFitColumns,
            };
            _autoFitItem.Click += (_, _) => ToggleAutoFit();
            menu.Items.Add(_autoFitItem);
            _menuAdded = true;
        }

        private void ToggleAutoFit()
        {
            if (Store is null) return;

            var profile = LoadProfile();
            profile.AutoFitColumns = !profile.AutoFitColumns;
            if (profile.AutoFitColumns)
            {
                // Auto-fit on → forget the manual widths so a later off-toggle starts fresh.
                profile.ColumnWidths = new Dictionary<string, double>();
            }
            else
            {
                // Auto-fit off → seed widths from what's on screen right now, so the very
                // first manual layout is captured even before the user resizes anything.
                var widths = new Dictionary<string, double>();
                foreach (var column in Grid.Columns)
                {
                    var header = HeaderText(column);
                    if (!string.IsNullOrEmpty(header) && column.ActualWidth > 0)
                    {
                        widths[header] = column.ActualWidth;
                    }
                }
                profile.ColumnWidths = widths;
            }

            try
            {
                Store.Save(profile);
            }
            catch (Exception)
            {
            }

            // Re-load + re-apply so the checkmark and column sizing reflect the new state
            // from the single source of truth (the persisted profile).
            ApplyProfile();
        }
    }
}
