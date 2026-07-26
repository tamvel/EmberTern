using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;

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
        }

        _bound = DataContext as DataImportTabViewModel;
        if (_bound is null) return;

        _bound.PreviewSchemaChanged += OnPreviewSchemaChanged;
        _bound.PropertyChanged += OnViewModelPropertyChanged;
        RebuildPreviewColumns();
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
    /// Only the shortcuts that have something to drive exist yet: <c>Ctrl+O</c> picks a file. <c>F5</c>,
    /// <c>Ctrl+F5</c> and <c>Esc</c> wait for the commands they would invoke (etap I7) — a shortcut bound to
    /// a command that does not exist is a dead wiring, the same reason the command bar was not built in I5.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _bound is not null)
        {
            _bound.BrowseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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
