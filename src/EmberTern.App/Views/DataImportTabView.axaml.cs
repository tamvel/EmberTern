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
            _bound.TextRequested -= AskForTextAsync;
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
        _bound.TextRequested += AskForTextAsync;
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

    // ⚠ The bottom panel is NOT clamped against a floor under the work area, and that is a user decision
    // (2026-07-27). A clamp existed for one review round: it made the floor in the XAML reachable by taking
    // room back from this panel. Running it showed the cost — the middle of the window grew and this panel
    // stopped being useful — so both halves were reverted together. The stored height wins outright; §3.8/U5
    // is open again and belongs to the app-wide UX sprint.

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
    /// <c>F5</c> imports, <c>Ctrl+F5</c> validates, <c>Esc</c> cancels a run, <c>Ctrl+O</c> picks a file,
    /// <c>Ctrl+R</c> and <c>Ctrl+V</c> refresh.
    /// <para>
    /// Every one of them goes through the very command the button does — the shortcut is a second trigger, never
    /// a second path — and each is guarded by that command's own <c>CanExecute</c>, so a shortcut can never do
    /// what the disabled button refuses to.
    /// </para>
    /// <para>
    /// <c>Ctrl+V</c> is the same command as <c>Ctrl+R</c>: on this surface „paste" and „re-read the clipboard" are
    /// the same request, and the clipboard read lives in the recalculation chain, so there is nothing else for it
    /// to invoke. It steps aside for a text field, where <c>Ctrl+V</c> still has to mean paste.
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

            // Inside a text field Ctrl+V stays paste. Checking the event's SOURCE rather than trusting the
            // TextBox to have marked the key handled means the guard holds either way.
            case Key.V when e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is TextBox:
                break;

            case Key.V when e.KeyModifiers.HasFlag(KeyModifiers.Control):
            case Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (Invoke(_bound.RefreshCommand)) e.Handled = true;
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
        var target = ResolveSectionTarget(section);
        if (target is null) return;

        // Posted, because a chip click that expands a section must let that section be laid out before its
        // first control can take focus — and before it can be scrolled to.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                // Scroll BEFORE focusing. Focus alone moves nothing when the control is off screen, which is
                // most of why the Mapping chip felt like it did nothing on a long column list.
                target.BringIntoView();
                target.Focus();

                // ⭐ Transaction is the one section that is NOT a band of this surface — it lives in the command
                // bar, a few pixels above the chip that points at it. So its landing is the only one where
                // BringIntoView is always a no-op and a ComboBox's focus ring is the entire feedback, which is
                // why the chip read as doing nothing at all. Opening the list is what „take me to this decision"
                // can mean for a picker: it shows the decision itself, and it still changes no setting.
                if (section == EmberTern.Core.Import.ImportSection.Transaction && target is ComboBox picker)
                {
                    picker.IsDropDownOpen = true;
                }
            },
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// The control a section's chip lands on — <b>one rule, and it never resolves to nothing.</b>
    /// <para>
    /// ⚠ This is where two chips used to die silently. <b>Target</b> resolved to the existing-table picker,
    /// which Avalonia reports as not-effectively-enabled whenever the new-table variant is selected, so
    /// <c>FirstFocusable</c> returned null and the click was swallowed; <b>Mapping</b> resolved to "the row
    /// needing attention", which is null the moment everything is mapped — i.e. it worked only while something
    /// was wrong. Both now ask what the section is currently ABOUT and fall back through the section's own
    /// container, so a green chip navigates exactly like a red one.
    /// </para>
    /// </summary>
    private Control? ResolveSectionTarget(EmberTern.Core.Import.ImportSection section) => section switch
    {
        EmberTern.Core.Import.ImportSection.Source =>
            this.FindControl<TextBox>("SourcePathBox") ?? FirstFocusable("SourceTile"),

        EmberTern.Core.Import.ImportSection.Format =>
            FirstFocusable("FormatOptionsPane") ?? FirstFocusable("SourceTile"),

        // The variant the user chose decides — asked of the VM, which owns that decision.
        EmberTern.Core.Import.ImportSection.Target => _bound?.TargetFocus switch
        {
            ImportTargetFocus.NewTableName => this.FindControl<TextBox>("NewTableNameBox"),
            ImportTargetFocus.NewTableColumns =>
                FirstFocusable("NewTableColumnRows") ?? this.FindControl<TextBox>("NewTableNameBox"),
            _ => FirstFocusable("TargetPicker"),
        } ?? FirstFocusable("TargetTile"),

        // The row that needs a decision if there is one, otherwise simply the first row — "take me to the
        // mapping" has to mean something even when the mapping is fine.
        EmberTern.Core.Import.ImportSection.Mapping =>
            MappingRowNeedingAttention() ?? FirstFocusable("MappingRows") ?? FirstFocusable("MappingPanel"),

        EmberTern.Core.Import.ImportSection.Transaction =>
            this.FindControl<ComboBox>("TransactionModePicker"),

        _ => null,
    };

    /// <summary>
    /// The picker of the mapping row that actually needs a decision — scrolled into view on the way.
    /// <para>
    /// ⭐ "Go to the Mapping section" is useless when the section is a forty-row grid and the problem is on
    /// row 31. The chip exists because the strip said something is wrong there, so it lands on the row the
    /// strip meant. Which row that is, is the panel's decision (<c>FirstRowNeedingAttention</c>) — the view
    /// only knows how to reach it.
    /// </para>
    /// </summary>
    private Control? MappingRowNeedingAttention()
    {
        if (_bound is null) return null;
        if (this.FindControl<ItemsControl>("MappingRows") is not { } list) return null;

        var row = _bound.Mapping.FirstRowNeedingAttention();
        if (row is null) return null;

        // A row hidden by the "only unmapped" filter has no container, so nothing could be focused. Ask for
        // the container, and fall back to the first focusable control rather than doing nothing at all.
        if (list.ContainerFromItem(row) is not Control container) return FirstFocusable("MappingRows");

        container.BringIntoView();

        foreach (var descendant in container.GetVisualDescendants())
        {
            if (descendant is ComboBox { IsEffectivelyEnabled: true } picker) return picker;
        }
        return container;
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

    /// <summary>
    /// Every destructive confirmation this module asks (§0): emptying the target, dropping a created table,
    /// overwriting a profile, deleting one.
    /// <para>
    /// ⚠ The whole question now comes from the VM. The heading and the button used to be hard-coded here to
    /// „Empty the table before importing" / „Import", so the drop-a-created-table question was already being
    /// asked under the name of a different action — and profiles would have made a third and a fourth. The view
    /// shows the question; it does not word it.
    /// </para>
    /// </summary>
    private async Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        var dialog = new ConfirmDialog { DataContext = new ConfirmDialogViewModel(request) };

        return TopLevel.GetTopLevel(this) is Window owner && await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>Asks for one line of text (a profile name) through the shared prompt.</summary>
    private Task<string?> AskForTextAsync(TextPromptRequest request) => TextPromptDialog.AskAsync(this, request);

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
