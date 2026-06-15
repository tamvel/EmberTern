using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

    // The data cell under the last right-click — drives the "Set NULL" context-menu
    // item. Recorded in OnDataGridPointerPressed (before the menu opens).
    private object?[]? _dataNullRow;
    private int _dataNullColumnIndex = -1;

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
            // Avalonia's DataGrid can come back blank after its TabItem is
            // detached and reattached (switch away from Pola, then back) — the
            // row generator doesn't re-run for an unchanged ItemsSource. Nudge
            // the ItemsSource on re-attach to force row regeneration (#7).
            _fieldsGrid.AttachedToVisualTree += OnFieldsGridAttached;
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
            // Per-cell pointer event (fires from inside the DataGridCell, carrying the
            // exact Row + Column) — the reliable way to know which cell was right-clicked
            // for the "Set NULL" context menu. A grid-level PointerPressed + internal
            // DataGridCell.OwningColumn reflection didn't resolve the cell on the
            // editable data grid, leaving the item perpetually disabled.
            _dataPreviewGrid.CellPointerPressed += OnDataCellPointerPressed;
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Re-assign ItemsSource from the VM after a tab-switch reattach. The XAML
    // binding already points at the same EditableFields instance, so reassigning
    // the same reference (after a null) only forces the DataGrid to regenerate
    // its rows — it doesn't break the live binding (EditableFields is never
    // swapped, only mutated). Posted to the dispatcher so it runs after the
    // attach/layout pass completes.
    private void OnFieldsGridAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_fieldsGrid is null) return;
        var vm = _currentVm;
        if (vm is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_fieldsGrid is null) return;
            _fieldsGrid.ItemsSource = null;
            _fieldsGrid.ItemsSource = vm.EditableFields;
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
            _currentVm.AddFieldRequested -= OnAddFieldRequested;
            _currentVm.EditFieldRequested -= OnEditFieldRequested;
            _currentVm.CreateForeignKeyRequested -= OnCreateForeignKeyRequested;
            _currentVm.AddPrimaryKeyRequested -= OnAddPrimaryKeyRequested;
            _currentVm.AddUniqueRequested -= OnAddUniqueRequested;
            _currentVm.AddCheckRequested -= OnAddCheckRequested;
            _currentVm.AddIndexRequested -= OnAddIndexRequested;
        }
        _currentVm = DataContext as TableDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _currentVm.AddFieldRequested += OnAddFieldRequested;
            _currentVm.EditFieldRequested += OnEditFieldRequested;
            _currentVm.CreateForeignKeyRequested += OnCreateForeignKeyRequested;
            _currentVm.AddPrimaryKeyRequested += OnAddPrimaryKeyRequested;
            _currentVm.AddUniqueRequested += OnAddUniqueRequested;
            _currentVm.AddCheckRequested += OnAddCheckRequested;
            _currentVm.AddIndexRequested += OnAddIndexRequested;
            PushDdl();
            PopulateDataGrid(_currentVm.DataResult);
        }
    }

    private async System.Threading.Tasks.Task<FieldDefinition?> OnAddFieldRequested()
        => await OpenAddFieldDialogAsync(originalField: null, canRename: true).ConfigureAwait(true);

    // Edit-mode entry: seeds the same dialog from the existing FieldInfo +
    // canRename gate. Caller (VM.EditFieldAsync) computes canRename via
    // CanRenameField and passes it through; we forward to the dialog VM so
    // the FieldName TextBox can disable and the hint can render.
    private async System.Threading.Tasks.Task<FieldDefinition?> OnEditFieldRequested(FieldInfo original, bool canRename)
        => await OpenAddFieldDialogAsync(original, canRename).ConfigureAwait(true);

    // Shared dialog-open path: fetch domains + generators once, build the VM
    // (Add or Edit mode), open the dialog, return its result. Single code
    // path keeps both modes wiring-identical except for the two extra ctor
    // args.
    private async System.Threading.Tasks.Task<FieldDefinition?> OpenAddFieldDialogAsync(FieldInfo? originalField, bool canRename)
    {
        if (_currentVm is null) return null;
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;
        var mainVm = window.DataContext as MainWindowViewModel;
        if (mainVm is null) return null;

        IReadOnlyList<DomainSpec> domains = System.Array.Empty<DomainSpec>();
        var generators = new List<string>();
        try
        {
            domains = await mainVm.MetadataReader.ListDomainsAsync().ConfigureAwait(true);
        }
        catch { /* best effort */ }
        try
        {
            var generatorObjs = await mainVm.MetadataReader.ListAsync(MetadataObjectKind.Generator).ConfigureAwait(true);
            foreach (var g in generatorObjs) generators.Add(g.Name);
        }
        catch { /* best effort */ }

        var dialogVm = new AddFieldDialogViewModel(_currentVm.TableName, domains, generators, originalField, canRename);
        var dialog = new AddFieldDialog { DataContext = dialogVm };
        return await dialog.ShowDialog<FieldDefinition?>(window);
    }

    // Session 3 — opens the real FK wizard. Resolves the source-table state
    // (Fields, list of all tables in the DB) up-front; ref-table fields + PK
    // are fetched on demand via callbacks when the user picks a target.
    // Returns the dialog's ForeignKeySpec? (null on Cancel) which the VM
    // hands to ExecuteCreateForeignKeyAsync.
    private async System.Threading.Tasks.Task<ForeignKeySpec?> OnCreateForeignKeyRequested()
    {
        if (_currentVm is null) return null;
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;
        var mainVm = window.DataContext as MainWindowViewModel;
        if (mainVm is null) return null;

        // Source-side: snapshot the current table's field names. The wizard
        // doesn't hot-reload them — if structure changes mid-edit the user
        // simply cancels and reopens. Falls back to a fresh reader fetch when
        // the VM's Fields collection is empty (#9).
        var sourceFieldNames = await ResolveFieldNamesAsync(window).ConfigureAwait(true);

        // List of available target tables. Best-effort: failures fall through
        // to an empty list (user can't pick a target → validation will block).
        IReadOnlyList<string> tableNames = System.Array.Empty<string>();
        try
        {
            var tables = await mainVm.MetadataReader.ListAsync(MetadataObjectKind.Table).ConfigureAwait(true);
            var names = new List<string>(tables.Count);
            foreach (var t in tables) names.Add(t.Name);
            tableNames = names;
        }
        catch { /* best effort */ }

        // Callback 1: load columns of a specific referenced table.
        // GetFieldsAsync returns FieldInfo (declaration order via Position);
        // we project to name list for the dialog's checkbox column.
        async System.Threading.Tasks.Task<IReadOnlyList<string>> LoadFields(string tableName)
        {
            try
            {
                var fields = await mainVm.TableDetailReader.GetFieldsAsync(tableName).ConfigureAwait(true);
                var list = new List<string>(fields.Count);
                foreach (var f in fields) list.Add(f.Name);
                return list;
            }
            catch
            {
                return System.Array.Empty<string>();
            }
        }

        // Callback 2: load the referenced table's PK column names. Reuses
        // the same GetFieldsAsync result — IsPrimaryKey is already populated
        // there, so we filter rather than firing a separate Constraints query.
        async System.Threading.Tasks.Task<IReadOnlyList<string>> LoadPrimaryKey(string tableName)
        {
            try
            {
                var fields = await mainVm.TableDetailReader.GetFieldsAsync(tableName).ConfigureAwait(true);
                var list = new List<string>();
                foreach (var f in fields)
                {
                    if (f.IsPrimaryKey) list.Add(f.Name);
                }
                return list;
            }
            catch
            {
                return System.Array.Empty<string>();
            }
        }

        var dialogVm = new ForeignKeyDialogViewModel(
            _currentVm.TableName,
            sourceFieldNames,
            tableNames,
            LoadFields,
            LoadPrimaryKey);
        return await ForeignKeyDialog.ShowAsync(window, dialogVm).ConfigureAwait(true);
    }

    // ─── Constraint management dialogs (Constraint Management Sprint V1) ──

    private Task<ConstraintFieldSpec?> OnAddPrimaryKeyRequested()
        => OpenConstraintFieldDialogAsync(ConstraintFieldKind.PrimaryKey);

    private Task<ConstraintFieldSpec?> OnAddUniqueRequested()
        => OpenConstraintFieldDialogAsync(ConstraintFieldKind.Unique);

    // PK + Unique share the field-picker dialog — only the kind differs.
    // Field names come from the current table's loaded Fields. If that
    // collection is empty (e.g. the user reached this from a sub-tab before the
    // lazy load populated Fields, or after a refresh emptied it), fall back to a
    // fresh reader query so the picker is never empty (#9).
    private async Task<ConstraintFieldSpec?> OpenConstraintFieldDialogAsync(ConstraintFieldKind kind)
    {
        if (_currentVm is null) return null;
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;

        var fieldNames = await ResolveFieldNamesAsync(window).ConfigureAwait(true);

        var dialogVm = new ConstraintFieldDialogViewModel(kind, _currentVm.TableName, fieldNames);
        return await ConstraintFieldDialog.ShowAsync(window, dialogVm).ConfigureAwait(true);
    }

    // Field names for the constraint / FK pickers. Prefers the already-loaded
    // VM Fields; falls back to a fresh reader fetch when that's empty so a
    // timing/refresh gap can't leave the picker blank.
    private async Task<List<string>> ResolveFieldNamesAsync(Window window)
    {
        var names = new List<string>();
        if (_currentVm is not null)
        {
            foreach (var f in _currentVm.Fields) names.Add(f.Name);
        }
        if (names.Count > 0) return names;

        if (_currentVm is null) return names;
        if (window.DataContext is not MainWindowViewModel mainVm) return names;
        try
        {
            var fields = await mainVm.TableDetailReader.GetFieldsAsync(_currentVm.TableName).ConfigureAwait(true);
            foreach (var f in fields) names.Add(f.Name);
        }
        catch { /* best effort — empty picker is still better than a crash */ }
        return names;
    }

    private async Task<CheckConstraintSpec?> OnAddCheckRequested()
    {
        if (_currentVm is null) return null;
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;

        var dialogVm = new CheckConstraintDialogViewModel(_currentVm.TableName);
        return await CheckConstraintDialog.ShowAsync(window, dialogVm).ConfigureAwait(true);
    }

    // Add-Index dialog. Field names come from the current table's loaded Fields,
    // with the same reader fallback the constraint pickers use.
    private async Task<IndexSpec?> OnAddIndexRequested()
    {
        if (_currentVm is null) return null;
        var window = this.FindAncestorOfType<Window>();
        if (window is null) return null;

        var fieldNames = await ResolveFieldNamesAsync(window).ConfigureAwait(true);
        var dialogVm = new IndexDialogViewModel(_currentVm.TableName, fieldNames);
        return await IndexDialog.ShowAsync(window, dialogVm).ConfigureAwait(true);
    }

    // Avalonia DataGrid doesn't select the row under a right-click (gotcha #16),
    // so context-menu Drop would act on a stale selection. Wire right-button
    // PointerPressed on each constraint sub-grid to select the row first; leave
    // Handled=false so the ContextMenu still opens. Each grid binds SelectedItem
    // to its own VM property, so setting grid.SelectedItem propagates.
    private void OnConstraintGridPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Avalonia.Visual visual) return;
        var row = visual.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is ConstraintInfo constraint)
        {
            grid.SelectedItem = constraint;
        }
    }

    // Right-click selects the Pola row first so the context menu (Edit / Drop /
    // Drop Foreign Key) acts on the clicked field, not a stale selection (#16).
    // The grid's SelectedItem binds to SelectedFieldRow → SelectedField.
    private void OnFieldsGridPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Avalonia.Visual visual) return;
        var row = visual.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is FieldRowViewModel fieldRow)
        {
            grid.SelectedItem = fieldRow;
        }
    }

    // Same right-click-selects-row pattern for the Indeksy grid so the context
    // menu's Drop Index acts on the clicked index, not a stale selection.
    private void OnIndexGridPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (e.Source is not Avalonia.Visual visual) return;
        var row = visual.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is IndexInfo index)
        {
            grid.SelectedItem = index;
        }
    }

    // Right-click on a data cell → record the clicked ROW + COLUMN so the context
    // menu's "Set NULL" acts on the exact cell, and enable the item only for nullable
    // columns. Uses Avalonia's dedicated CellPointerPressed event, which is raised by
    // the DataGridCell itself with the Row + Column already resolved — no reflection,
    // and it fires reliably on the editable data grid (a grid-level PointerPressed +
    // internal DataGridCell.OwningColumn reflection did not, which is why the item was
    // perpetually greyed out). Fires before the ContextMenu opens.
    private void OnDataCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;

        _dataNullRow = null;
        _dataNullColumnIndex = -1;

        if (e.Row?.DataContext is object?[] rowData)
        {
            grid.SelectedItem = rowData;
            _dataNullRow = rowData;
        }
        if (e.Column is not null)
        {
            // Column reorder is disabled on this grid, so Columns order == creation
            // order == the VM's column index (DataResult.Columns order).
            _dataNullColumnIndex = grid.Columns.IndexOf(e.Column);
        }

        if (grid.ContextMenu?.Items.Count > 0 && grid.ContextMenu.Items[0] is MenuItem setNull)
        {
            setNull.IsEnabled =
                _currentVm is not null
                && _dataNullRow is not null
                && _dataNullColumnIndex >= 0
                && _currentVm.IsColumnNullable(_dataNullColumnIndex);
        }
    }

    private void OnSetCellNullClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is null) return;
        if (_dataNullRow is null || _dataNullColumnIndex < 0) return;
        // Routes through the existing UpdateCellAsync path — same change-tracking
        // and UPDATE as a manual edit; no separate save path.
        _ = _currentVm.SetCellNullAsync(_dataNullRow, _dataNullColumnIndex);
    }

    // Double-click on a Pola row opens the Edit Field dialog. Filters out
    // double-taps on column headers + empty rows (DataContext is not a
    // FieldRowViewModel). Inline cell-edit takes precedence when the grid
    // is in edit mode (IsFieldsReadOnly=false) — Avalonia's DataGrid
    // intercepts the double-click for cell entry first, so this handler
    // only fires when we're in read-only state. That matches the spec
    // ("dwuklik na wierszu pola = Edytuj pole").
    private void OnFieldsGridDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_currentVm is null) return;
        if (e.Source is not Avalonia.Visual visual) return;
        var row = visual.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row is null) return;
        if (row.DataContext is not FieldRowViewModel) return;
        if (_currentVm.EditFieldCommand.CanExecute(null))
        {
            _currentVm.EditFieldCommand.Execute(null);
            e.Handled = true;
        }
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
    //
    // When the tab can't edit data (CanEditData false — e.g. a system table,
    // which the factory builds without a data editor) an interactive CheckBox
    // would still toggle because DataGrid.IsReadOnly doesn't reach into
    // CellTemplate controls. So in read-only mode we render the same ✓ / blank
    // glyph the Pola / Indeksy / Ograniczenia grids use (BoolToCheckmarkConverter)
    // instead of a live control — truly non-interactive, visually unambiguous.
    // CanEditData is the single source of truth; columns are rebuilt per data
    // load and capability is fixed per VM, so reading it here is stable.
    private IDataTemplate BuildBooleanCellTemplate(int columnIndex, FieldInfo? field)
    {
        var encodeAsSmallint = string.Equals(field?.BaseTypeName, "SMALLINT", StringComparison.OrdinalIgnoreCase);
        var editable = _currentVm?.CanEditData == true;

        if (!editable)
        {
            return new FuncDataTemplate<object?[]>((row, _) =>
            {
                var glyph = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(4, 0),
                };
                if (row is not null && columnIndex < row.Length)
                {
                    // Mirrors BoolToCheckmarkConverter ("✓" / "") after decoding the
                    // underlying encoding (bool / short 0-1 / int / …).
                    glyph.Text = ConvertToNullableBool(row[columnIndex]) == true ? "✓" : string.Empty;
                }
                return glyph;
            });
        }

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
        var initial = current switch
        {
            null => string.Empty,
            string s => s,
            byte[] bytes => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.BlobEditorBinaryPlaceholder,
                bytes.Length),
            _ => current.ToString() ?? string.Empty,
        };
        var readOnly = ResolveBlobReadOnly(current, _currentVm.CanEditData);

        var newText = await BlobEditorWindow.ShowAsync(window, initial, readOnly).ConfigureAwait(true);
        if (newText is null) return; // Cancel
        // For read-only binary BLOBs the dialog returns the placeholder text;
        // we never want to write that back. Skip the UPDATE on read-only path.
        if (readOnly) return;

        await _currentVm.UpdateCellAsync(row, columnIndex, string.IsNullOrEmpty(newText) ? null : newText);
    }

    // BLOB editor open mode. Viewing is always allowed; editing is blocked for
    // binary BLOBs (no faithful text round-trip) and whenever the tab can't edit
    // data (CanEditData false — e.g. a read-only system table). CanEditData is the
    // single source of truth. Internal + pure so it's unit-testable without a UI.
    internal static bool ResolveBlobReadOnly(object? cellValue, bool canEditData)
        => cellValue is byte[] || !canEditData;

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
        if (_fieldsGrid is null || sender is not FieldRowViewModel row) return;
        // Find the realized container for this row VM and re-apply the "pending" class
        // LIVE, so the tint clears the moment IsModified flips false (revert, or the
        // row VM rebuilt clean after Compile) — not only on the next LoadingRow. This
        // is what fixes the stale brown row after an edit completes.
        foreach (var dgr in _fieldsGrid.GetVisualDescendants().OfType<DataGridRow>())
        {
            if (ReferenceEquals(dgr.DataContext, row))
            {
                UpdatePendingClass(dgr, row);
                break;
            }
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

    // Double-click a row in the field-dependencies panel → open the object,
    // exactly like the Zależności tree leaf double-click. Walks to the row,
    // confirms the DataContext is a FieldDependencyItem, and fires its
    // NavigateCommand (gated on CanNavigate). Non-navigable kinds (Field /
    // unknown) silently no-op via the command's CanExecute.
    private void OnFieldDependencyDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual visual) return;
        var row = visual.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is not FieldDependencyItem item) return;
        if (item.NavigateCommand.CanExecute(null))
        {
            item.NavigateCommand.Execute(null);
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
