using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.Behaviors;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stabilization sprint S-1a (2026-08-05): the ratified UX rule <b>"Enter does what clicking that cell
/// does"</b>, in the only place it can be established — a real <see cref="DataGrid"/> receiving a real key.
///
/// <para>⚠⚠ WHY THIS CANNOT BE A UNIT TEST, and why a source guard is not enough either. Every fact the
/// behaviour rests on is a framework fact: Avalonia's <c>DataGrid</c> claims Enter itself (its
/// <c>ProcessEnterKey</c> commits and moves down a row — the reported symptom), it exposes no public "am I
/// editing" member, and a bubbling handler arrives AFTER the selection has already moved. A test that asserted
/// our code called <c>BeginEdit</c> would pass while the grid still moved down.</para>
///
/// <para>⚠ Deliberately the cheapest headless shape — bare controls in a bare <see cref="Window"/>, never
/// <c>MainWindow</c> (the documented hang-prone construction). It joins <see cref="HeadlessCollection"/> and
/// never adds its own class fixture (gotchas #94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class EditableGridEnterTests
{
    private readonly HeadlessUnitTestSession _session;

    public EditableGridEnterTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    public sealed class Row
    {
        public string Name { get; set; } = "A";
        public string Note { get; set; } = "B";
    }

    /// <summary>A grid shaped like the real definition grids: an ordinary editable text column, plus an
    /// <c>IsReadOnly</c> template column holding an ALWAYS-VISIBLE editor (how the Type combo, the merged
    /// Domain/Column picker and the Size/Scale boxes are built — a DataGridTextColumn supports IsReadOnly only
    /// per COLUMN while their enable gate is per ROW, gotcha #83/#124).</summary>
    private static (Window Window, DataGrid Grid, ObservableCollection<Row> Items) BuildGrid(
        bool embeddedEnabled = true, double? fixedRowHeight = null)
    {
        var items = new ObservableCollection<Row> { new(), new() };
        var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false, IsReadOnly = false };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(Row.Name)) { Mode = BindingMode.TwoWay },
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Always",
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<Row>((_, _) =>
            {
                var box = new TextBox { Name = "Embedded", IsEnabled = embeddedEnabled };
                box.Bind(TextBox.TextProperty, new Binding(nameof(Row.Note)) { Mode = BindingMode.TwoWay });
                return box;
            }),
        });

        // ⚠ Mirrors what TableDetailTabView declares for its DATA grid — a FIXED row Height plus that view's
        // `6 2` cell padding — because the height half of the seam is only meaningful against a row that
        // reserves space for the editor. The relation between those numbers and Size.Control is pinned
        // against the real markup in EditableGridSeamTests; here they only set the scene.
        if (fixedRowHeight is { } h)
        {
            grid.Styles.Add(new Style(x => x.OfType<DataGridRow>())
            {
                Setters = { new Setter(Layoutable.HeightProperty, h) },
            });
            grid.Styles.Add(new Style(x => x.OfType<DataGridCell>())
            {
                Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 2)) },
            });
        }

        var window = new Window { Content = grid, Width = 600, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        EditableGridBehavior.Attach(grid);
        return (window, grid, items);
    }

    private static void FocusCell(Window window, DataGrid grid, int row, int column)
    {
        grid.SelectedIndex = row;
        grid.CurrentColumn = grid.Columns[column];
        grid.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Focused(Window window) => window.FocusManager?.GetFocusedElement();

    private static void PressEnter(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Enter_OnAnEditableCell_BeginsEditing_InsteadOfMovingDown()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid();
            FocusCell(window, grid, row: 0, column: 0);

            PressEnter(window);

            // ⭐ The whole point: the selection must NOT have moved. Avalonia's own ProcessEnterKey moves down
            // one row, which is precisely what the user reported.
            Assert.Equal(0, grid.SelectedIndex);
            // An edit is running — the editing element the grid created now holds focus (there is no public
            // "IsEditing", so the editing element IS the observable).
            var editor = Focused(window) as TextBox;
            Assert.NotNull(editor);
            Assert.NotEqual("Embedded", editor!.Name);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task Enter_OnAnAlwaysVisibleEditor_MovesFocusIntoIt()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid();
            FocusCell(window, grid, row: 0, column: 1);

            PressEnter(window);

            // An IsReadOnly column has no edit MODE to begin, so "Enter does what a click does" means putting
            // keyboard focus in the control that is already there. This is the reported case: the Domain cell.
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Equal("Embedded", (Focused(window) as TextBox)?.Name);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task Enter_WhileAlreadyInsideAnEditor_IsLeftAlone()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid();
            FocusCell(window, grid, row: 0, column: 1);
            PressEnter(window);                       // focus moves into the embedded control
            Assert.Equal("Embedded", (Focused(window) as TextBox)?.Name);

            PressEnter(window);                       // …and now Enter belongs to the grid again, not to us

            // ⚠⚠ MEASURED, AND IT CORRECTED THIS TEST'S FIRST ASSERTION. The embedded box does not accept
            // Return, so Enter bubbles on to the DataGrid, whose own ProcessEnterKey commits and moves down a
            // row — which is the RIGHT behaviour (it is what Enter has always done from inside a cell editor,
            // and what a data grid should do), and it is the pre-existing behaviour, unchanged.
            //
            // ⭐ So what this test pins is that THE SEAM STAYS OUT OF IT: the gate is FOCUS, because DataGrid
            // exposes no public "am I editing", and once something inside a cell owns focus Enter is not ours
            // to claim. Observable as the grid's own Enter applying — not as focus staying put, which was this
            // test's first (wrong) guess about the desired outcome.
            Assert.Equal(1, grid.SelectedIndex);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task Enter_OnADisabledAlwaysVisibleEditor_DoesNothing()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid(embeddedEnabled: false);
            FocusCell(window, grid, row: 0, column: 1);

            PressEnter(window);

            // ⭐ Those boxes are disabled exactly when a domain or TYPE OF governs the type, and clicking one
            // does nothing either — so "Enter does what a click does" means doing nothing here too. Focus
            // stays on the grid; the seam leaves the key unhandled and the grid's own Enter applies.
            Assert.NotEqual("Embedded", (Focused(window) as TextBox)?.Name);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task Enter_InADataGrid_BeginsEditingToo_OneRuleForBothKinds()
    {
        await _session.Dispatch(() =>
        {
            // ⭐ The ratified rule is ONE rule: the user rejected two behaviours for two kinds of editable
            // grid. Only the HEIGHT role differs between the kinds — see the next test.
            var (window, grid, _) = BuildGrid();
            FocusCell(window, grid, row: 0, column: 0);

            PressEnter(window);

            Assert.Equal(0, grid.SelectedIndex);
            Assert.IsType<TextBox>(Focused(window));

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ THE HEIGHT ROLE REACHES EVERY EDITABLE GRID — measured on the element that paints, not on the class
    /// list. Replaces <c>TheHeightRole_ReachesADefinitionGridOnly</c> (2026-08-07), which pinned the opposite
    /// and was the reported defect: "the TextBox while editing is still too low" in Table Data.
    ///
    /// <para>⚠ Asserting <c>Classes.Contains("field-grid")</c> alone would be gotcha #315's shape — green while
    /// the product is broken, because a class proves the marker was added, not that the style resolved and
    /// arrived at the editor the grid creates on entering edit mode. So this measures the editor itself.</para>
    /// </summary>
    [Fact]
    public async Task TheHeightRole_ReachesTheInCellEditor_OfADataShapedGrid()
    {
        await _session.Dispatch(() =>
        {
            // A row shaped like Table Data's: a FIXED 32 px height, so it cannot grow from its content.
            var (window, grid, _) = BuildGrid(fixedRowHeight: 32);
            FocusCell(window, grid, row: 0, column: 0);
            var rowControl = grid.GetVisualDescendants().OfType<DataGridRow>().First();
            var heightBeforeEditing = rowControl.Bounds.Height;

            PressEnter(window);

            var editor = Focused(window) as TextBox;
            Assert.NotNull(editor);
            Assert.NotEqual("Embedded", editor!.Name);

            // The role arrived: the editor asks for the same height as an ordinary control, instead of the
            // MinHeight 0 that made it read as a thin strip inside a much taller row.
            var expected = (double)grid.FindResource("Size.Control")!;
            Assert.Equal(expected, editor.MinHeight);
            Assert.True(editor.Bounds.Height >= expected,
                $"The editor measured {editor.Bounds.Height} px against a role of {expected} px.");

            // …and it cost nothing: §13.3's Zero Layout Shift. This is the half the old comment claimed was
            // impossible, and it holds because the ROW owns the height and 32 − (2 × 2) ≥ 24.
            Assert.Equal(heightBeforeEditing, rowControl.Bounds.Height);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task Attach_IsIdempotent_SoEnterIsNeverHandledTwice()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid();
            EditableGridBehavior.Attach(grid);
            EditableGridBehavior.Attach(grid);

            Assert.Equal(1, grid.Classes.Count(c => c == EditableGridBehavior.AttachedClass));

            FocusCell(window, grid, row: 0, column: 0);
            PressEnter(window);
            Assert.Equal(0, grid.SelectedIndex);

            window.Close();
        }, default);
    }
}
