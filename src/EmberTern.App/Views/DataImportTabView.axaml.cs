using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using EmberTern.App.Export;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;

namespace EmberTern.App.Views;

/// <summary>
/// Code-behind for the Data Import surface. It owns the two things a ViewModel cannot: the source preview's
/// columns, whose SHAPE is only known once a source has been read, and the bottom panel's row sizing.
/// <para>
/// The dynamic-column part is the same pattern the SQL results and Table Data grids already use — the VM
/// publishes the fields, the view turns them into <see cref="DataGridColumn"/>s. A column is an Avalonia type,
/// so it cannot live in a ViewModel (rule #1).
/// </para>
/// </summary>
public partial class DataImportTabView : UserControl
{
    private DataImportTabViewModel? _bound;

    private RowDefinition? _workRow;
    private RowDefinition? _bottomRow;

    private const double MinBottomHeight = 80;
    private const double DefaultBottomHeight = 190;

    public DataImportTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        if (this.FindControl<Grid>("SurfaceLayout") is { } layout && layout.RowDefinitions.Count > 7)
        {
            // Row 5 is the work area (the star), row 7 the bottom panel. ApplyBottomPanel sets BOTH.
            _workRow = layout.RowDefinitions[5];
            _bottomRow = layout.RowDefinitions[7];
        }

        if (this.FindControl<GridSplitter>("BottomSplitter") is { } splitter)
        {
            splitter.DragCompleted += OnBottomSplitterDragCompleted;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.PreviewSchemaChanged -= OnPreviewSchemaChanged;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _bound.ConvertedPreview.SchemaChanged -= OnConvertedSchemaChanged;
            _bound.ReportReady -= OnReportReady;
            _bound.ConfirmRequested -= ConfirmAsync;
            _bound.ExportReportRequested -= ExportReportAsync;
            _bound.PreviewRowRevealRequested -= OnPreviewRowRevealRequested;
            _bound.SectionFocusRequested -= OnSectionFocusRequested;
        }

        _bound = DataContext as DataImportTabViewModel;
        if (_bound is null) return;

        _bound.PreviewSchemaChanged += OnPreviewSchemaChanged;
        _bound.PropertyChanged += OnViewModelPropertyChanged;
        _bound.ConvertedPreview.SchemaChanged += OnConvertedSchemaChanged;
        _bound.ReportReady += OnReportReady;
        _bound.ConfirmRequested += ConfirmAsync;
        _bound.ExportReportRequested += ExportReportAsync;
        _bound.PreviewRowRevealRequested += OnPreviewRowRevealRequested;
        _bound.SectionFocusRequested += OnSectionFocusRequested;

        RebuildPreviewColumns();
        RebuildConvertedColumns();
        ApplyBottomPanel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataImportTabViewModel.IsBottomPanelCollapsed)) ApplyBottomPanel();
    }

    // ── Bottom panel sizing ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ONE re-normalization point for the bottom panel, and it sets <b>both</b> rows every time.
    /// <para>
    /// ⚠ This is gotcha #240, learned the expensive way in the debugger: a <c>GridSplitter</c> converts the
    /// star row into an ABSOLUTE pixel height as soon as it is dragged, so a toggle that only touches the
    /// bottom row leaves the grid with no star row to reclaim the space — the panel then "glues" to whatever
    /// the last drag left behind. Setting the work row back to star here is what makes collapse and expand
    /// symmetrical no matter what happened before.
    /// </para>
    /// </summary>
    private void ApplyBottomPanel()
    {
        if (_workRow is null || _bottomRow is null || _bound is null) return;

        _workRow.Height = new GridLength(1, GridUnitType.Star);
        _bottomRow.Height = _bound.IsBottomPanelCollapsed
            ? GridLength.Auto
            : new GridLength(ResolveBottomHeight(_bound.BottomPanelHeight));
    }

    private static double ResolveBottomHeight(double stored)
        => stored >= MinBottomHeight ? stored : DefaultBottomHeight;

    /// <summary>
    /// Captures the dragged height onto the VM, which is where it outlives this view — the import tab is
    /// transient, so a height remembered in a field here would be gone before the workspace is written.
    /// </summary>
    private void OnBottomSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (_bound is null || _bottomRow is null) return;
        if (_bound.IsBottomPanelCollapsed) return;
        if (!_bottomRow.Height.IsAbsolute) return;

        var height = _bottomRow.Height.Value;
        if (height >= MinBottomHeight) _bound.BottomPanelHeight = height;
    }

    /// <summary>Double-clicking the tab strip toggles the panel — the same gesture the SQL editor and the
    /// debugger use, routed through the SAME command as the chevron so there is one collapse path.</summary>
    private void OnBottomPanelDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A click on the chevron button is its own command; don't toggle twice.
        if (e.Source is Visual visual && visual.FindAncestorOfType<Button>() is not null) return;

        _bound?.ToggleBottomPanelCommand.Execute(null);
        e.Handled = true;
    }

    // ── Keyboard (§9.2) ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>F5</c> imports, <c>Ctrl+F5</c> validates, <c>Esc</c> cancels a run, <c>Ctrl+O</c> picks a file.
    /// <para>
    /// Every one of them goes through the very command the button does — the shortcut is a second trigger, never
    /// a second path — and each is guarded by that command's own <c>CanExecute</c>, so a shortcut can never do
    /// what the disabled button refuses to.
    /// </para>
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_bound is null)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.F5 when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (Invoke(_bound.ValidateCommand)) e.Handled = true;
                return;

            case Key.F5:
                if (Invoke(_bound.ImportCommand)) e.Handled = true;
                return;

            // Esc only when there is a run to stop; otherwise it stays the ordinary "dismiss" key.
            case Key.Escape:
                if (Invoke(_bound.CancelRunCommand)) e.Handled = true;
                return;

            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _bound.BrowseCommand.Execute(null);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    private static bool Invoke(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    // ── The readiness strip's navigation (§3.2) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the caret in the control the clicked section owns.
    /// <para>
    /// ⚠ The VM raised this event from the start and <b>nothing listened</b> — which is why four of the five
    /// chips appeared to do nothing while Format (whose VM-side expand was visible on its own) appeared to
    /// work. Exactly gotcha #233's shape: a mechanism that is built, correct and simply never called looks
    /// from the outside like a broken feature, and the green suite is what hides it.
    /// </para>
    /// <para>
    /// Focus is the whole gesture on purpose. §3.2 makes the strip a navigation aid — "every gap is visible
    /// AND reachable in one click" — so a chip answers "where do I fix this", and nothing else: it changes no
    /// setting the user did not ask to change.
    /// </para>
    /// </summary>
    private void OnSectionFocusRequested(object? sender, EmberTern.Core.Import.ImportSection section)
    {
        var target = section switch
        {
            EmberTern.Core.Import.ImportSection.Source => this.FindControl<TextBox>("SourcePathBox"),
            EmberTern.Core.Import.ImportSection.Format => FirstFocusable("FormatOptionsPane"),
            EmberTern.Core.Import.ImportSection.Target => FirstFocusable("TargetPicker"),
            EmberTern.Core.Import.ImportSection.Mapping => FirstFocusable("MappingRows"),
            EmberTern.Core.Import.ImportSection.Transaction => this.FindControl<ComboBox>("TransactionModePicker"),
            _ => null,
        };

        // Posted, because a chip click that expands a section must let that section be laid out before its
        // first control can take focus.
        if (target is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => target.Focus(), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>The first control inside <paramref name="hostName"/> that can actually take focus — a section
    /// is a container, and "focus the section" only means anything as "focus what the user would type into".</summary>
    private Control? FirstFocusable(string hostName)
    {
        if (this.FindControl<Control>(hostName) is not { } host) return null;

        foreach (var descendant in host.GetVisualDescendants())
        {
            if (descendant is Control { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } control
                && control is not Panel)
            {
                return control;
            }
        }
        return null;
    }

    // ── The run's own surfaces ──────────────────────────────────────────────────────────────────────────

    private const int ReportTabIndex = 2;

    /// <summary>A finished run brings its own tab forward (§3.7) — the answer arrives where the user is
    /// looking, instead of behind a tab they have to think to open.</summary>
    private void OnReportReady(object? sender, EventArgs e)
    {
        if (this.FindControl<TabControl>("BottomTabs") is { } tabs) tabs.SelectedIndex = ReportTabIndex;
        if (_bound is not null) _bound.IsBottomPanelCollapsed = false;
    }

    /// <summary>The one destructive confirmation this module asks (§0): emptying the target table.</summary>
    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ConfirmDialog
        {
            DataContext = new ConfirmDialogViewModel(new ConfirmRequest
            {
                Title = UiStrings.ImportTargetEmptyFirst,
                Message = message,
                ConfirmLabel = UiStrings.ImportRun,
                IsDestructive = true,
            }),
        };

        return TopLevel.GetTopLevel(this) is Window owner && await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// Hands the problem list to the SHARED export framework (§4.6) — CSV / TXT / XLSX / clipboard for free,
    /// without one line of new serialization. <c>RowBufferExportSource</c> is the existing adapter for a grid
    /// whose rows are already in memory; the error list is exactly that.
    /// </summary>
    private async Task ExportReportAsync(IReadOnlyList<ImportProblemRowViewModel> problems)
    {
        var columns = new[]
        {
            new ExportColumn(UiStrings.ImportReportColumnRow, typeof(int)),
            new ExportColumn(UiStrings.ImportReportColumnColumn, typeof(string)),
            new ExportColumn(UiStrings.ImportReportColumnValue, typeof(string)),
            new ExportColumn(UiStrings.ImportReportColumnReason, typeof(string)),
        };

        var rows = new List<object?[]>(problems.Count);
        foreach (var problem in problems)
        {
            rows.Add(new object?[] { problem.SourceRowNumber, problem.ColumnName, problem.RawValue, problem.Reason });
        }

        var source = new RowBufferExportSource(columns, rows, rows, null, "import-report");
        await ExportDialog.LaunchAsync(this, source, ExportScope.AllRows);
    }

    /// <summary>Double-clicking a problem shows that row in the converted preview — the report names a row, so
    /// the surface can take the user to it.</summary>
    private void OnProblemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ImportProblemRowViewModel problem })
        {
            _bound?.RevealProblemCommand.Execute(problem);
            e.Handled = true;
        }
    }

    private void OnPreviewRowRevealRequested(object? sender, int sourceRowNumber)
    {
        if (_bound is null) return;
        if (this.FindControl<DataGrid>("ConvertedPreviewGrid") is not { } grid) return;

        foreach (var row in _bound.ConvertedPreview.Rows)
        {
            if (row.SourceRowNumber != sourceRowNumber) continue;

            grid.SelectedItem = row;
            grid.ScrollIntoView(row, null);
            return;
        }
    }

    // ── Converted-preview columns ───────────────────────────────────────────────────────────────────────

    private void OnConvertedSchemaChanged(object? sender, EventArgs e) => RebuildConvertedColumns();

    /// <summary>
    /// Rebuilds the converted grid to match the mapped columns.
    /// <para>
    /// The row number comes first and carries the failure marker, because the row it names is the one the user
    /// has to find in their file. A failed row shows its RAW values — it has no converted ones, by construction
    /// (the pipeline stops the row at its first bad value) — and the tooltip says so rather than leaving the
    /// grid to imply the raw text is what would be written.
    /// </para>
    /// </summary>
    private void RebuildConvertedColumns()
    {
        if (this.FindControl<DataGrid>("ConvertedPreviewGrid") is not { } grid || _bound is null) return;

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiStrings.ImportRowNumberColumn,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<ImportConvertedRowViewModel>((_, _) =>
            {
                var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };

                var marker = new TextBlock
                {
                    Text = UiStrings.ImportRaggedMarker,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    [!TextBlock.IsVisibleProperty] = new Binding(nameof(ImportConvertedRowViewModel.IsFailed)),
                    [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ErrorBrush"),
                };
                ToolTip.SetTip(marker, UiStrings.ImportPreviewFailedTooltip);

                var number = new TextBlock
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = new Binding(nameof(ImportConvertedRowViewModel.SourceRowNumber)),
                };

                panel.Children.Add(marker);
                panel.Children.Add(number);
                return panel;
            }, supportsRecycling: true),
        });

        for (var i = 0; i < _bound.ConvertedPreview.Columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = _bound.ConvertedPreview.Columns[i],
                Binding = new Binding($"Values[{i.ToString(CultureInfo.InvariantCulture)}]"),
                IsReadOnly = true,
            });
        }
    }

    // ── Source preview columns ──────────────────────────────────────────────────────────────────────────

    private void OnPreviewSchemaChanged(object? sender, EventArgs e) => RebuildPreviewColumns();

    /// <summary>
    /// Rebuilds the preview grid to match the source's current shape.
    /// <para>
    /// The row number comes first and is the SOURCE's own numbering — the number the user can find in their
    /// file — because that is the number every error message will quote (§0.6). It doubles as the ragged-row
    /// marker's home (§3.6): a record whose field count disagrees with the rest of the file is the instant
    /// tell for a wrong separator, and it is meant to be SEEN rather than counted.
    /// </para>
    /// </summary>
    private void RebuildPreviewColumns()
    {
        if (this.FindControl<DataGrid>("SourcePreviewGrid") is not { } grid || _bound is null) return;

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiStrings.ImportRowNumberColumn,
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<ImportSourceRecordRowViewModel>((_, _) =>
            {
                var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };

                var marker = new TextBlock
                {
                    Text = UiStrings.ImportRaggedMarker,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    [!TextBlock.IsVisibleProperty] = new Binding(nameof(ImportSourceRecordRowViewModel.IsRagged)),
                    [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("WarningBrush"),
                };
                ToolTip.SetTip(marker, UiStrings.ImportSourcePreviewRaggedTooltip);

                var number = new TextBlock
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    [!TextBlock.TextProperty] = new Binding(nameof(ImportSourceRecordRowViewModel.SourceRowNumber)),
                };

                panel.Children.Add(marker);
                panel.Children.Add(number);
                return panel;
            }, supportsRecycling: true),
        });

        for (var i = 0; i < _bound.PreviewFields.Count; i++)
        {
            var field = _bound.PreviewFields[i];
            grid.Columns.Add(new DataGridTextColumn
            {
                // A generated positional label is shown as-is; a real header keeps the source's own spelling,
                // because that is what the user will look for when mapping.
                Header = field.Name,
                Binding = new Binding($"Values[{i.ToString(CultureInfo.InvariantCulture)}]"),
                IsReadOnly = true,
            });
        }
    }
}
