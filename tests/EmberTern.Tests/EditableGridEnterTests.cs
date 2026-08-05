using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
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
        EditableGridKind kind, bool embeddedEnabled = true)
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

        var window = new Window { Content = grid, Width = 600, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        EditableGridBehavior.Attach(grid, kind);
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
            var (window, grid, _) = BuildGrid(EditableGridKind.Definition);
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
            var (window, grid, _) = BuildGrid(EditableGridKind.Definition);
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
            var (window, grid, _) = BuildGrid(EditableGridKind.Definition);
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
            var (window, grid, _) = BuildGrid(EditableGridKind.Definition, embeddedEnabled: false);
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
            var (window, grid, _) = BuildGrid(EditableGridKind.Data);
            FocusCell(window, grid, row: 0, column: 0);

            PressEnter(window);

            Assert.Equal(0, grid.SelectedIndex);
            Assert.IsType<TextBox>(Focused(window));

            window.Close();
        }, default);
    }

    [Fact]
    public async Task TheHeightRole_ReachesADefinitionGridOnly()
    {
        await _session.Dispatch(() =>
        {
            var (definitionWindow, definitionGrid, _) = BuildGrid(EditableGridKind.Definition);
            var (dataWindow, dataGrid, _) = BuildGrid(EditableGridKind.Data);

            // ⚠⚠ MEASURED, not cautious: a 24 px minimum on a data grid's in-cell editor grows every row the
            // moment editing starts, because those rows have no ComboBox holding them open (M2b step 7's
            // regression, and the layout shift §13.3 forbids). A definition grid already measures ≥30 px.
            Assert.Contains(EditableGridBehavior.FieldGridClass, definitionGrid.Classes);
            Assert.DoesNotContain(EditableGridBehavior.FieldGridClass, dataGrid.Classes);

            // Both went through the seam, so both have the Enter gesture.
            Assert.Contains(EditableGridBehavior.AttachedClass, definitionGrid.Classes);
            Assert.Contains(EditableGridBehavior.AttachedClass, dataGrid.Classes);

            definitionWindow.Close();
            dataWindow.Close();
        }, default);
    }

    [Fact]
    public async Task Attach_IsIdempotent_SoEnterIsNeverHandledTwice()
    {
        await _session.Dispatch(() =>
        {
            var (window, grid, _) = BuildGrid(EditableGridKind.Definition);
            EditableGridBehavior.Attach(grid, EditableGridKind.Definition);
            EditableGridBehavior.Attach(grid, EditableGridKind.Definition);

            Assert.Equal(1, grid.Classes.Count(c => c == EditableGridBehavior.AttachedClass));

            FocusCell(window, grid, row: 0, column: 0);
            PressEnter(window);
            Assert.Equal(0, grid.SelectedIndex);

            window.Close();
        }, default);
    }
}
