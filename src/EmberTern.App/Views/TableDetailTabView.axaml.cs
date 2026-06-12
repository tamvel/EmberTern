using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Converters;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;

namespace EmberTern.App.Views;

public partial class TableDetailTabView : UserControl
{
    private TextEditor? _ddlEditor;
    private DataGrid? _dataPreviewGrid;
    private DataGrid? _fieldsGrid;
    private TableDetailTabViewModel? _currentVm;
    private readonly List<string> _dataPreviewColumnNames = new();
    // Resolved once per column rebuild — used by CellEditEnding to extract the
    // new value from the right kind of editing element (TextBox / picker / etc).
    private readonly List<CellEditorKind> _dataPreviewEditorKinds = new();

    private enum CellEditorKind
    {
        Text,
        Date,
        Boolean,
        // BLOB cells aren't standard-editable — the cell template carries a
        // button that opens a modal text editor. Kept in the enum so the
        // column build can choose the right CellTemplate.
        Blob,
    }

    public TableDetailTabView()
    {
        InitializeComponent();
        _ddlEditor = this.FindControl<TextEditor>("TableDetailDdlEditor");
        _dataPreviewGrid = this.FindControl<DataGrid>("DataPreviewGrid");
        _fieldsGrid = this.FindControl<DataGrid>("FieldsGrid");
        if (_fieldsGrid is not null)
        {
            // Inline structure-edit on the Pola grid: every row-commit (Tab/Enter
            // out of the editing element, or focus moves off the row) inspects
            // edited values vs. original and queues ALTER statements via the VM.
            _fieldsGrid.RowEditEnding += OnFieldsRowEditEnding;
            // Toggle the "pending" row class when IsModified changes so the row
            // gets a subtle background tint until Compile drains the queue.
            _fieldsGrid.LoadingRow += OnFieldsLoadingRow;
        }
        if (_dataPreviewGrid is not null)
        {
            // Avalonia paints the column-header arrow itself when (a) the
            // DataGrid has CanUserSortColumns=True and (b) the clicked column
            // has a non-empty SortMemberPath. Without (b) the Sorting event
            // doesn't even fire. We set SortMemberPath in PopulateDataGrid for
            // each dynamically generated column, leave Handled=false so
            // Avalonia drives the indicator + in-memory sort on the local 200
            // rows, and use the Sorting event to mirror the click into VM state
            // and trigger a server-side ORDER BY reload.
            _dataPreviewGrid.Sorting += OnDataPreviewSorting;
            _dataPreviewGrid.CellEditEnding += OnCellEditEnding;
            _dataPreviewGrid.RowEditEnding += OnRowEditEnding;
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
            _currentVm.AddFieldRequested -= OnAddFieldRequested;
        }
        _currentVm = DataContext as TableDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.AddFieldRequested += OnAddFieldRequested;
            PushDdl();
            PopulateDataGrid(_currentVm.DataResult);
        }
    }

    private async System.Threading.Tasks.Task<FieldDefinition?> OnAddFieldRequested()
    {
        if (_currentVm is null) return null;
        // Walk up to the host Window so we can resolve the MainWindowViewModel
        // (carrier of MetadataReader). Also serves as the dialog's owner.
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;
        var mainVm = window.DataContext as MainWindowViewModel;
        if (mainVm is null) return null;

        // Fetch domains + generators from the live metadata reader. Failures fall
        // through to empty lists — the dialog still works against the basic-type
        // tab and a manually-entered generator name. Domains carry their SQL type
        // (e.g. T_ID — INTEGER) so the ComboBox shows it inline.
        IReadOnlyList<DomainSpec> domains = System.Array.Empty<DomainSpec>();
        var generators = new List<string>();
        try
        {
            domains = await mainVm.MetadataReader.ListDomainsAsync().ConfigureAwait(true);
        }
        catch { /* best effort — empty list lets the dialog still open */ }
        try
        {
            var generatorObjs = await mainVm.MetadataReader.ListAsync(MetadataObjectKind.Generator).ConfigureAwait(true);
            foreach (var g in generatorObjs) generators.Add(g.Name);
        }
        catch { /* best effort */ }

        var dialogVm = new AddFieldDialogViewModel(_currentVm.TableName, domains, generators);
        var dialog = new AddFieldDialog { DataContext = dialogVm };
        var result = await dialog.ShowDialog<FieldDefinition?>(window);
        return result;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.DdlText)
            || e.PropertyName == nameof(TableDetailTabViewModel.DdlWithPendingPreview))
        {
            PushDdl();
        }
        else if (e.PropertyName == nameof(TableDetailTabViewModel.DataResultVersionTag))
        {
            PopulateDataGrid(_currentVm?.DataResult);
        }
    }

    // DataGrid columns are imperative — we can't bind them from XAML.
    //
    // Sort plumbing for a dynamically-generated DataGridTemplateColumn over
    // object?[] rows:
    //   1. CanUserSortColumns="True" on the grid (in XAML).
    //   2. CanUserSort = true on each column (default — kept explicit for
    //      symmetry with SortMemberPath / CustomSortComparer).
    //   3. SortMemberPath = column name (any non-empty identifier — it's only
    //      used as a key in the DataGrid's SortDescriptions collection).
    //   4. CustomSortComparer = a column-index-aware IComparer over object?[].
    //      Without this, DataGridTemplateColumn's default GetSortDescription
    //      tries to evaluate SortMemberPath as a property *path* against each
    //      row — for an object?[] row no property of any name resolves, so the
    //      sort descriptor is invalid, the column is treated as unsortable,
    //      and the Sorting event silently never fires. (DataGridTextColumn
    //      doesn't hit this because DataGridBoundColumn.GetSortDescription
    //      uses its Binding to evaluate values — which DataGridTemplateColumn
    //      doesn't have.)
    //
    // Columns are preserved across reloads when the structure (column count +
    // names + order) matches the previous result — only the ItemsSource is
    // reassigned. That keeps Avalonia's internal CurrentSortingState (the
    // arrow indicator) alive after a server-side ORDER BY reload.
    private void PopulateDataGrid(QueryResult? result)
    {
        if (_dataPreviewGrid is null) return;

        if (result is null || !result.HasResultSet)
        {
            _dataPreviewGrid.Columns.Clear();
            _dataPreviewGrid.ItemsSource = null;
            _dataPreviewColumnNames.Clear();
            return;
        }

        bool sameStructure = _dataPreviewColumnNames.Count == result.Columns.Count;
        if (sameStructure)
        {
            for (int i = 0; i < result.Columns.Count; i++)
            {
                if (!string.Equals(result.Columns[i].Name, _dataPreviewColumnNames[i], StringComparison.Ordinal))
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        if (!sameStructure)
        {
            _dataPreviewGrid.Columns.Clear();
            _dataPreviewColumnNames.Clear();
            _dataPreviewEditorKinds.Clear();

            // Field metadata lookup — once per build. The VM's Fields collection
            // is populated by LoadAsync; ReloadDataPreviewAsync now awaits
            // EnsureLoadedAsync first so by this point we have it.
            var fieldByName = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            if (_currentVm is not null)
            {
                foreach (var f in _currentVm.Fields)
                {
                    fieldByName[f.Name] = f;
                }
            }

            for (int i = 0; i < result.Columns.Count; i++)
            {
                int columnIndex = i; // closure capture
                var columnName = result.Columns[i].Name;
                _dataPreviewColumnNames.Add(columnName);

                fieldByName.TryGetValue(columnName, out var field);
                var kind = DetermineEditorKind(field);
                _dataPreviewEditorKinds.Add(kind);

                var column = new DataGridTemplateColumn
                {
                    Header = columnName,
                    SortMemberPath = columnName,
                    CustomSortComparer = new RowIndexComparer(columnIndex),
                    CanUserSort = true,
                    CellTemplate = BuildCellTemplate(columnIndex, kind, field),
                    CellEditingTemplate = BuildCellEditingTemplate(columnIndex, kind),
                };
                // Boolean toggling and BLOB editing don't use the standard
                // cell-edit flow — the cell template itself handles the click,
                // and BeginEdit on those columns would just surface a stray
                // editor element. IsReadOnly=true keeps DataGrid out of that.
                if (kind is CellEditorKind.Boolean or CellEditorKind.Blob)
                {
                    column.IsReadOnly = true;
                }
                _dataPreviewGrid.Columns.Add(column);
            }
        }

        // EditableRows is the writable mirror — DataGrid is already bound to it
        // via XAML; we only need to clear ItemsSource when there's no result.
        // Bind explicitly for the no-VM (design-mode) defensive path.
        if (_currentVm is not null)
        {
            _dataPreviewGrid.ItemsSource = _currentVm.EditableRows;
        }
    }

    // ─── Smart cell editor helpers ──────────────────────────────────────
    //
    // Per-column editor kind is decided once per (re)build of the columns
    // from the matching FieldInfo's Type + Domain. The kind drives template
    // construction (CellTemplate / CellEditingTemplate) and value extraction
    // in CellEditEnding.

    private static CellEditorKind DetermineEditorKind(FieldInfo? field)
    {
        if (field is null) return CellEditorKind.Text;
        var typeName = field.BaseTypeName?.ToUpperInvariant() ?? string.Empty;
        if (typeName.StartsWith("DATE", StringComparison.Ordinal)) return CellEditorKind.Date;
        if (typeName.StartsWith("TIMESTAMP", StringComparison.Ordinal)) return CellEditorKind.Date;
        if (typeName == "BOOLEAN") return CellEditorKind.Boolean;
        if (typeName == "SMALLINT"
            && string.Equals(field.Domain, "T_BOOLEANN", StringComparison.OrdinalIgnoreCase))
        {
            return CellEditorKind.Boolean;
        }
        if (typeName == "BLOB") return CellEditorKind.Blob;
        return CellEditorKind.Text;
    }

    private IDataTemplate BuildCellTemplate(int columnIndex, CellEditorKind kind, FieldInfo? field) => kind switch
    {
        CellEditorKind.Boolean => BuildBooleanCellTemplate(columnIndex, field),
        CellEditorKind.Blob => BuildBlobCellTemplate(columnIndex),
        _ => BuildTextCellTemplate(columnIndex),
    };

    private IDataTemplate BuildCellEditingTemplate(int columnIndex, CellEditorKind kind) => kind switch
    {
        CellEditorKind.Date => BuildDateEditingTemplate(columnIndex),
        // Boolean / BLOB columns are IsReadOnly=true; the editing template is
        // never instantiated but Avalonia wants a non-null reference, so a
        // throwaway TextBlock is sufficient.
        CellEditorKind.Boolean or CellEditorKind.Blob => new FuncDataTemplate<object?[]>((_, _) => new TextBlock()),
        _ => BuildTextEditingTemplate(columnIndex),
    };

    private static IDataTemplate BuildTextCellTemplate(int columnIndex)
        => new FuncDataTemplate<object?[]>((row, _) =>
        {
            var tb = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0),
            };
            if (row is null || columnIndex >= row.Length) return tb;
            var value = row[columnIndex];
            if (value is null)
            {
                tb.Text = UiStrings.TableDetailDataPreviewNullPlaceholder;
                tb.Classes.Add("null-cell");
            }
            else
            {
                tb.Text = value.ToString();
            }
            return tb;
        });

    private static IDataTemplate BuildTextEditingTemplate(int columnIndex)
        => new FuncDataTemplate<object?[]>((row, _) =>
        {
            var tb = new TextBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
                FontSize = 11,
            };
            if (row is not null && columnIndex < row.Length)
            {
                tb.Text = row[columnIndex]?.ToString() ?? string.Empty;
            }
            return tb;
        });

    // Avalonia 12.0.3's CalendarDatePicker.SelectedDate is DateTime?. The
    // DateTimeToDateTimeOffsetConverter exists for future controls that use
    // the DateTimeOffset shape (e.g. CalendarDatePicker in newer Avalonia
    // releases, or third-party pickers) — here we route through plain
    // DateTime? since that's what the live control surface accepts.
    private static IDataTemplate BuildDateEditingTemplate(int columnIndex)
        => new FuncDataTemplate<object?[]>((row, _) =>
        {
            var picker = new CalendarDatePicker
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
                FontSize = 11,
                MinWidth = 120,
            };
            if (row is not null && columnIndex < row.Length)
            {
                picker.SelectedDate = row[columnIndex] is DateTime dt ? dt : (DateTime?)null;
            }
            return picker;
        });

    // BOOLEAN cells render and toggle through a CheckBox in the CellTemplate
    // itself — IsReadOnly=true keeps DataGrid out of the way. Click fires
    // UpdateCellAsync directly. The Field tells us how to encode the value
    // back: BOOLEAN → bool; SMALLINT-with-T_BOOLEANN → short 0/1.
    private IDataTemplate BuildBooleanCellTemplate(int columnIndex, FieldInfo? field)
    {
        var encodeAsSmallint = string.Equals(field?.BaseTypeName, "SMALLINT", StringComparison.OrdinalIgnoreCase);
        return new FuncDataTemplate<object?[]>((row, _) =>
        {
            var cb = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 0),
            };
            if (row is not null && columnIndex < row.Length)
            {
                cb.IsChecked = ConvertToNullableBool(row[columnIndex]);
            }
            cb.Click += async (s, _) =>
            {
                if (s is CheckBox c && c.DataContext is object?[] r && _currentVm is not null)
                {
                    object? newValue;
                    if (encodeAsSmallint)
                    {
                        newValue = c.IsChecked == true ? (short)1 : (short)0;
                    }
                    else
                    {
                        newValue = c.IsChecked;
                    }
                    await _currentVm.UpdateCellAsync(r, columnIndex, newValue);
                }
            };
            return cb;
        });
    }

    private IDataTemplate BuildBlobCellTemplate(int columnIndex)
        => new FuncDataTemplate<object?[]>((row, _) =>
        {
            var btn = new Button
            {
                Content = UiStrings.BlobEditorButtonIcon,
                Padding = new Thickness(8, 0),
                Margin = new Thickness(2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(btn, UiStrings.BlobEditorButtonTooltip);
            btn.Click += async (s, _) =>
            {
                if (s is not Button b || b.DataContext is not object?[] r) return;
                await OpenBlobEditorAsync(b, r, columnIndex);
            };
            return btn;
        });

    private async System.Threading.Tasks.Task OpenBlobEditorAsync(Control source, object?[] row, int columnIndex)
    {
        if (_currentVm is null) return;
        var window = TopLevel.GetTopLevel(source) as Window;
        if (window is null) return;

        var current = columnIndex < row.Length ? row[columnIndex] : null;
        string? initial;
        bool readOnly;
        switch (current)
        {
            case null:
                initial = string.Empty;
                readOnly = false;
                break;
            case string s:
                initial = s;
                readOnly = false;
                break;
            case byte[] bytes:
                initial = string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.BlobEditorBinaryPlaceholder,
                    bytes.Length);
                readOnly = true;
                break;
            default:
                initial = current.ToString() ?? string.Empty;
                readOnly = false;
                break;
        }

        var newText = await BlobEditorWindow.ShowAsync(window, initial, readOnly).ConfigureAwait(true);
        if (newText is null) return; // Cancel
        // For read-only binary BLOBs the dialog returns the placeholder text;
        // we never want to write that back. Skip the UPDATE on read-only path.
        if (readOnly) return;

        await _currentVm.UpdateCellAsync(row, columnIndex, string.IsNullOrEmpty(newText) ? null : newText);
    }

    private static bool? ConvertToNullableBool(object? value) => value switch
    {
        null => null,
        bool b => b,
        short s => s != 0,
        int i => i != 0,
        long l => l != 0,
        string str => str switch
        {
            "1" or "true" or "TRUE" or "True" => true,
            "0" or "false" or "FALSE" or "False" => false,
            _ => null,
        },
        _ => null,
    };

    // Compares two object?[] rows by a captured column index. Avalonia's
    // built-in sort uses this when DataGridColumn.CustomSortComparer is set,
    // bypassing the (broken-for-array-rows) property-path resolution.
    private sealed class RowIndexComparer : IComparer
    {
        private readonly int _index;
        public RowIndexComparer(int index) => _index = index;

        public int Compare(object? x, object? y)
        {
            var xv = (x as object?[]) is { } xa && _index < xa.Length ? xa[_index] : null;
            var yv = (y as object?[]) is { } ya && _index < ya.Length ? ya[_index] : null;
            if (xv is null && yv is null) return 0;
            if (xv is null) return -1;
            if (yv is null) return 1;
            if (xv is IComparable xcmp && xv.GetType() == yv.GetType()) return xcmp.CompareTo(yv);
            return string.Compare(xv.ToString(), yv.ToString(), StringComparison.CurrentCulture);
        }
    }

    private void PushDdl()
    {
        if (_ddlEditor is null || _currentVm is null) return;
        // DdlWithPendingPreview prepends the live DDL with a "-- Pending changes:"
        // block whenever the user has queued add/drop/move actions, so the DDL
        // sub-tab reflects what Compile would send to the server.
        var text = _currentVm.DdlWithPendingPreview ?? string.Empty;
        if (_ddlEditor.Text != text)
        {
            _ddlEditor.Text = text;
        }
    }

    // RowEditEnding fires when the user commits the row (Tab/Enter out of the
    // last editing element, or focus moves to a different row). All edited cells
    // are already written through to the bound FieldRowViewModel by that point;
    // we ask the VM to inspect-and-queue.
    private void OnFieldsRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.DataContext is not FieldRowViewModel row) return;
        if (_currentVm is null) return;
        _currentVm.EnqueueRowEdits(row);
        // Refresh the row class so the tint reflects the new IsModified state.
        UpdatePendingClass(e.Row, row);
    }

    private void OnFieldsLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is FieldRowViewModel row)
        {
            UpdatePendingClass(e.Row, row);
            row.PropertyChanged -= OnFieldRowPropertyChanged;
            row.PropertyChanged += OnFieldRowPropertyChanged;
        }
    }

    private void OnFieldRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FieldRowViewModel.IsModified)) return;
        if (_fieldsGrid is null) return;
        // Walk visible rows to find the one matching the VM and re-apply class.
        // We don't track containers explicitly; LoadingRow re-fires on virtualization
        // recycle, so this path only matters for currently realized rows.
        if (sender is not FieldRowViewModel row) return;
        foreach (var item in _fieldsGrid.ItemsSource!)
        {
            if (!ReferenceEquals(item, row)) continue;
            // No public API for "get container for item"; LoadingRow has done the
            // initial pass. Class changes via .Classes[] would require the row;
            // skipping mid-edit toggle is acceptable — RowEditEnding's
            // UpdatePendingClass covers the post-commit redraw.
            break;
        }
    }

    private static void UpdatePendingClass(DataGridRow row, FieldRowViewModel vm)
    {
        if (vm.IsModified)
        {
            if (!row.Classes.Contains("pending")) row.Classes.Add("pending");
        }
        else
        {
            row.Classes.Remove("pending");
        }
    }

    private void OnDependencyNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DependencyLeafNode leaf } && _currentVm is not null)
        {
            _currentVm.RequestOpen(leaf);
            e.Handled = true;
        }
    }

    // CellEditEnding fires when a single cell's edit is committed (Tab / Enter
    // moves focus out of the editing element). We resolve the column index,
    // pull the new value out of the right kind of editing element based on the
    // resolved CellEditorKind, and ask the VM to fire UPDATE — for newly-added
    // rows the VM defers until RowEditEnding (INSERT path). Boolean and BLOB
    // columns use IsReadOnly=true so this handler never fires for them; their
    // cell template handles the commit directly.
    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_currentVm is null || _dataPreviewGrid is null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row?.DataContext is not object?[] row) return;

        var columnIndex = _dataPreviewGrid.Columns.IndexOf(e.Column);
        if (columnIndex < 0) return;

        var kind = columnIndex < _dataPreviewEditorKinds.Count
            ? _dataPreviewEditorKinds[columnIndex]
            : CellEditorKind.Text;

        object? newValue = ExtractNewValue(kind, e.EditingElement);
        _ = _currentVm.UpdateCellAsync(row, columnIndex, newValue);
    }

    private static object? ExtractNewValue(CellEditorKind kind, Control? editingElement) => kind switch
    {
        CellEditorKind.Date => (editingElement as CalendarDatePicker)?.SelectedDate is { } dt
            ? (object?)dt
            : null,
        _ => (editingElement as TextBox)?.Text is { } t && t.Length > 0 ? t : null,
    };

    // RowEditEnding fires after a full row is "confirmed" (Enter on the row,
    // or focus moves to a different row). For an in-grid AddRow we use this
    // as the INSERT trigger. CellEditEnding has already pushed the per-cell
    // text into the row array, so CommitNewRowAsync sees the full set.
    private void OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (_currentVm is null) return;
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row?.DataContext is not object?[] row) return;
        if (!_currentVm.IsNewRow(row)) return;

        _ = _currentVm.CommitNewRowAsync(row);
    }

    private void OnDataPreviewSorting(object? sender, DataGridColumnEventArgs e)
    {
        // Diagnostic — confirms Avalonia is firing the Sorting event for our
        // dynamically-generated columns. Visible in Debug Output (VS) /
        // `dotnet run`'s console. Cheap; safe to keep in production.
        Debug.WriteLine(
            $"[DataPreview] Sorting fired: column='{e.Column?.Header}', "
            + $"SortMemberPath='{e.Column?.SortMemberPath}'");

        if (_currentVm is null || _dataPreviewGrid is null) return;

        var index = _dataPreviewGrid.Columns.IndexOf(e.Column);
        if (index < 0 || index >= _dataPreviewColumnNames.Count) return;

        var name = _dataPreviewColumnNames[index];

        // Don't set e.Handled. Avalonia paints the column-header arrow via its
        // internal CurrentSortingState (not publicly settable in 12.0.0) only
        // when the Sorting event runs to completion uncancelled. Avalonia also
        // does a local in-memory sort over the 200 rows via the column's
        // CustomSortComparer — same ordering as the DB ORDER BY we kick off
        // below, so the row swap when our reload completes is visually a
        // no-op. PopulateDataGrid preserves Columns across reloads so the
        // arrow indicator state survives.
        _ = _currentVm.ApplyColumnSortAsync(name);
    }

    private void ApplyEditorTheme()
    {
        if (_ddlEditor is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light
            ? App.FirebirdSyntaxLightName
            : App.FirebirdSyntaxName;
        var syntax = HighlightingManager.Instance.GetDefinition(name);
        _ddlEditor.SyntaxHighlighting = syntax;

        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _ddlEditor.TextArea.SelectionBrush = brush;
        }
    }
}
