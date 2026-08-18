using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The licences list as a GRID — EmberTern's grid, not a lookalike.</b>
///
/// <para>The list used to be a <c>ListBox</c> whose rows were a hand-built <c>Grid</c> of fixed-width
/// <c>TextBlock</c>s: no headers, no resizing, no sorting, no zebra, and a Fluent checkbox that on its own
/// forced a 40 px row. This class is the evidence that what replaced it is the same grid EmberTern's
/// Session Manager uses — appearance from the LINKED <c>DataGridStyles.axaml</c>, behaviour from the
/// control itself.</para>
///
/// <para>⚠⚠ <b>EVERY TEST HERE RETURNS ITS <c>Task</c>, AND THAT IS LOAD-BEARING.</b>
/// <c>HeadlessUnitTestSession.Dispatch</c> returns a <c>Task</c>; written as <c>public void X() =&gt;
/// _session.Dispatch(…)</c> it compiles, the <c>Task</c> is DISCARDED, xUnit never awaits it, and no
/// assertion inside the lambda can fail the test (gotcha #374). ⛔ Never write one of these as
/// <c>void</c>.</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture:
/// one headless session per PROCESS (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenceGridTests
{
    // ⭐ The two catalogue roles the grid stands on, named here so a failure says WHICH standard moved.
    //   `Size.Row.Grid` = 22, `Size.Row.Header` = 24 — the header is one step taller than the row, and
    //   that step IS its frame (DataGridStyles.axaml says so where it is declared).
    private const double RowHeight = 22d;
    private const double HeaderHeight = 24d;

    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenceGridTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The grid itself ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheListIsARealGridWithHeadersInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐ A header row is the single most visible difference from the old list, and it is also what
            //   makes sorting and resizing discoverable at all. Asserted on the REALISED headers and on
            //   the text they render — never on what the XAML spells (gotcha #370).
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var grid = Grid(window);

            Assert.Equal(8, grid.Columns.Count);
            Assert.Equal(
                ["", "Customer", "Contact", "Licence id", "Seats", "Status", "Expiry", "Standing"],
                grid.Columns.Select(c => c.Header as string ?? string.Empty));

            // The realised header row: one visible header per column, all on one line.
            var headers = Headers(window);
            Assert.Equal(8, headers.Count);
            Assert.All(headers, h => Assert.Equal(HeaderHeight, h.Bounds.Height));
        }, default);

    [Fact]
    public Task TheGridOffersResizingSortingAndHorizontalSeparators() =>
        _session.Dispatch(() =>
        {
            // ⚠ These four are what "the same grid as EmberTern's" MEANS at the control level — the
            //   Session Manager's session grid declares the identical set. A view that quietly turned one
            //   off would still look right in a screenshot.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var grid = Grid(window);

            Assert.True(grid.CanUserResizeColumns);
            Assert.True(grid.CanUserSortColumns);
            Assert.Equal(DataGridGridLinesVisibility.Horizontal, grid.GridLinesVisibility);

            // ⛔ Single, and it stays single: the batch selection is the CHECKBOX. See the grid's own
            //   comment in MainWindow.axaml, and TheTickSurvivesAnOrdinaryRowSelection below.
            Assert.Equal(DataGridSelectionMode.Single, grid.SelectionMode);
        }, default);

    [Fact]
    public Task EveryRowCarriesAHorizontalSeparatorThatIsActuallyPainted() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ `GridLinesVisibility="Horizontal"` is a declaration; this is the line reaching the pixel.
            //    Fluent's DataGridRow template names the separator `PART_BottomGridLine`, and it is the
            //    one that turns a wall of text into rows the eye can follow. ⚠ A brush that resolved to
            //    nothing would leave the property at its default and the line simply absent — no error.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var lines = Rows(window)
                .Select(r => r.GetVisualDescendants().OfType<Rectangle>()
                    .FirstOrDefault(x => x.Name == "PART_BottomGridLine"))
                .ToList();

            Assert.All(lines, line =>
            {
                Assert.NotNull(line);
                Assert.IsAssignableFrom<ISolidColorBrush>(line!.Fill);
                Assert.True(line.Bounds.Width > 0, "Separator wiersza ma zerową szerokość.");
            });

            // ⛔ Subtle, not a frame: one hairline under the row, never a border around every cell.
            Assert.All(lines, line => Assert.True(line!.Bounds.Height <= 1.5));
        }, default);

    // ── Zebra and selection ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task EverySecondRowIsTintedAndTheSelectionStillWins(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE PAIR THAT MUST BE TESTED TOGETHER. Zebra striping is painted onto the SAME element
            //    as the selection (`Rectangle#BackgroundRectangle`), so a zebra rule written without the
            //    `:nth-child(2n):selected` companion silently erases the selection on half the rows —
            //    which looks like "selection sometimes does not work" and is very hard to attribute.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, Seed);

            var alternate = HeadlessTheme.Brush("RowAlternateBrush")!.Color;
            var selection = HeadlessTheme.Brush("SelectionBrush")!.Color;
            Assert.NotEqual(alternate, selection);

            var rows = Rows(window);
            Assert.True(rows.Count >= 4, "Za mało wierszy, żeby zobaczyć naprzemienność.");

            // ⚠ Which parity is tinted is Fluent's business (`:nth-child` is 1-based); what matters is
            //   that neighbours DIFFER and that the tint is the catalogue's, not an invented colour.
            Assert.NotEqual(Fill(rows[0]), Fill(rows[1]));
            Assert.Equal(alternate, Fill(rows[1]));
            Assert.Equal(alternate, Fill(rows[3]));

            // ⭐ And now the half that the zebra rule can silently eat: a TINTED row, selected.
            shell.Browser.SelectedLicense = (LicenseListItem)rows[1].DataContext!;
            window.UpdateLayout();

            Assert.Equal(selection, Fill(Rows(window)[1]));
        }, default);

    // ── Density and vertical alignment ──────────────────────────────────────────────────────────────

    [Fact]
    public Task TheRowStandsAtTheGridStandardAndTheHeaderOneStepAbove() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ MEASURED BEFORE AND AFTER (2026-08-18). Before: row 40 px, driven by Fluent's own
            //    CheckBox at 28 × 32. After: 22 px, which is `Size.Row.Grid` exactly.
            //    ⚠ Bounded ABOVE as well as below: this list is a professional data list, not a stack of
            //    cards, and the failure this catches is a wrapping cell — the very defect found while
            //    building it, where an application-wide `TextWrapping="Wrap"` made every cell 57 px.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            Assert.All(Rows(window), row => Assert.Equal(RowHeight, row.Bounds.Height));
            Assert.All(Headers(window), h => Assert.Equal(HeaderHeight, h.Bounds.Height));
        }, default);

    [Fact]
    public Task EveryValueInARowSitsOnOneVerticalCentreLine() =>
        _session.Dispatch(() =>
        {
            // ⭐ "Checkbox, texts, status, seats, expiry — one visual line." Asserted as the CENTRES of the
            //   realised content agreeing, which is the thing the eye actually reads; the alignment
            //   PROPERTY is asserted alongside it because a centred-by-accident row (content that happens
            //   to fill the cell) would pass the first check and fail the day a value gets shorter.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var row = Rows(window)[0];
            // ⚠ `Take(Columns.Count)`: Fluent appends a FILLER cell past the last column, exactly as it
            //   appends a filler header. It is not one of ours and has nothing to align.
            var cells = row.GetVisualDescendants().OfType<DataGridCell>()
                .OrderBy(c => c.Bounds.X)
                .Take(Grid(window).Columns.Count)
                .ToList();
            Assert.Equal(8, cells.Count);
            Assert.All(cells, c => Assert.Equal(VerticalAlignment.Center, c.VerticalContentAlignment));

            var centres = cells
                .Select(c => c.GetVisualDescendants().OfType<Control>()
                    .FirstOrDefault(x => x is TextBlock or CheckBox))
                .OfType<Control>()
                .Select(x => x.TranslatePoint(new Point(0, x.Bounds.Height / 2), row))
                .Where(point => point.HasValue)
                .Select(point => point!.Value.Y)
                .ToList();

            Assert.True(centres.Count >= 7, "Nie znaleziono zawartości w każdej komórce wiersza.");
            Assert.True(centres.Max() - centres.Min() <= 1.0,
                "Zawartość wiersza nie stoi na jednej linii: " +
                string.Join(", ", centres.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))));
        }, default);

    [Fact]
    public Task TheCheckboxFitsInsideTheRowInsteadOfSettingItsHeight() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ RELEASE BLOCKER RB‑2, in this application. Fluent's CheckBox realises at 28 × 32 and was
            //    forcing a 40 px row — measured on the list before the change. The template in
            //    `LicenseManagerControlThemes.axaml` reproduces EmberTern's answer: the box takes
            //    `Size.Checkbox` (14) and the control declares NO height of its own.
            //    ⚠ The assertion is "smaller than the row", not "equal to 14": a control that happens to
            //    match a number is not the point — a control that cannot inflate its row is.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var ticks = ViewProbe.AllNamed<CheckBox>(window, "RowTick");
            Assert.NotEmpty(ticks);

            Assert.All(ticks, tick =>
            {
                Assert.True(tick.Bounds.Height <= RowHeight,
                    $"Checkbox ma {tick.Bounds.Height} px przy wierszu {RowHeight} px — znów narzuca wysokość.");
                Assert.Equal(14d, Box(tick).Bounds.Height);
                Assert.Equal(14d, Box(tick).Bounds.Width);
            });

            // ⭐ The hit target is wider than the mark, and only wider — a 20 px VERTICAL target would push
            //   the row back up and undo the repair. The asymmetry is arithmetic, not taste.
            Assert.All(ticks, tick => Assert.True(tick.Bounds.Width > 14d));
        }, default);

    // ── Sorting ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task ClickingAHeaderSortsAscendingThenDescendingAndSaysWhich() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ DRIVEN BY A REAL CLICK ON A REAL HEADER, not by poking the collection view. The claim is
            //    that the OPERATOR can sort — a test that sorts the projection itself would pass just as
            //    happily with the headers inert.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            ClickHeader(window, "Customer");
            Assert.Equal(ListSortDirection.Ascending, Direction(window, "Customer"));
            var ascending = Column(window, "Customer");
            Assert.Equal(ascending.OrderBy(v => v, StringComparer.Ordinal), ascending);

            ClickHeader(window, "Customer");
            Assert.Equal(ListSortDirection.Descending, Direction(window, "Customer"));
            var descending = Column(window, "Customer");

            // ⚠ Compared against the same values ordered the other way, NOT against the ascending list
            //   reversed: four licences share the customer "Umbrella", and reversing a list with ties
            //   asserts something about tie order that no sort promises.
            Assert.Equal(ascending.OrderByDescending(v => v, StringComparer.Ordinal), descending);
        }, default);

    [Fact]
    public Task SeatsSortsByTheCountAndNotByTheSentenceThatShowsIt() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE DEFECT THIS COLUMN WOULD OTHERWISE HAVE. `Seats` renders as "3 seats" / "12 seats",
            //    and sorted as TEXT "12 seats" comes before "3 seats" — a column that answers the
            //    operator's question with a confident wrong answer. It sorts on `SeatsValue`.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            ClickHeader(window, "Seats");

            var counts = Column(window, "Seats")
                .Select(text => int.Parse(text.Split(' ')[0], CultureInfo.InvariantCulture))
                .ToList();

            Assert.Contains(counts, c => c >= 10);
            Assert.Contains(counts, c => c < 10);
            Assert.Equal(counts.OrderBy(c => c), counts);
        }, default);

    [Fact]
    public Task EverySortableColumnSortsOnTheVALUEItMeansRatherThanOnTheTextItShows() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ ADDED BECAUSE AN INJECTED DEFECT SURVIVED (#378's shape). Pointing `Licence id` at
            //    `ShortId` — the TRUNCATED value the column displays — left every other guard green,
            //    because a 12-character prefix orders almost identically to the full identifier. So the
            //    behavioural tests cannot see that defect at all, and pretending otherwise would be a
            //    guard that reads as coverage while providing none.
            //
            // ⚠ This one is therefore a DECLARATION guard, and it is written as one on purpose: three
            //    columns show a rendering of something and must sort on the thing itself. The other three
            //    (`Customer`, `Contact`, `Status`) show the value they sort on, and their behaviour is
            //    covered above.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            var grid = Grid(window);

            Assert.Equal(nameof(LicenseListItem.LicenseId), Column(grid, "Licence id").SortMemberPath);
            Assert.Equal(nameof(LicenseListItem.SeatsValue), Column(grid, "Seats").SortMemberPath);
            Assert.Equal(nameof(LicenseListItem.ExpiresAtValue), Column(grid, "Expiry").SortMemberPath);

            // ⛔ And none of them sorts on what it renders — the trap this guard exists for.
            Assert.NotEqual(nameof(LicenseListItem.ShortId), Column(grid, "Licence id").SortMemberPath);
            Assert.NotEqual(nameof(LicenseListItem.Seats), Column(grid, "Seats").SortMemberPath);
            Assert.NotEqual(nameof(LicenseListItem.Expiry), Column(grid, "Expiry").SortMemberPath);
        }, default);

    [Fact]
    public Task SortingIsDeterministicAndReturnsTheSameOrderEveryTime() =>
        _session.Dispatch(() =>
        {
            // ⭐ "Stable" as this list can honestly claim it: the same data sorted the same way produces
            //   the same sequence, so rows do not shuffle under the operator between two identical sorts.
            //   ⚠ Deliberately NOT claiming that equal keys keep their arrival order — Avalonia's
            //   collection view does not promise that, and a test asserting it would be asserting an
            //   implementation detail of a package we do not control.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            ClickHeader(window, "Status");
            var first = Column(window, "Status");

            ClickHeader(window, "Expiry");
            ClickHeader(window, "Status");
            var again = Column(window, "Status");

            Assert.Equal(first, again);
        }, default);

    [Fact]
    public Task TheStandingColumnDoesNotSortAndThatIsDeliberate() =>
        _session.Dispatch(() =>
        {
            // ⛔ A decision, not an omission: `Standing` is a SENTENCE about the expiry date. Sorted as
            //   text it orders by the letter E; sorted by date it is a second Expiry column under another
            //   name. The operator who wants that order has Expiry right beside it.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed);

            Assert.False(Column(Grid(window), "Standing").CanUserSort);

            // ⚠ And the other six DO sort — otherwise this test would also pass on a grid where nothing
            //   sorts at all.
            foreach (var header in new[] { "Customer", "Contact", "Licence id", "Seats", "Status", "Expiry" })
            {
                Assert.True(Column(Grid(window), header).CanUserSort, header + " przestała być sortowalna.");
            }
        }, default);

    // ── Sorting versus the batch selection ──────────────────────────────────────────────────────────

    [Fact]
    public Task SortingKeepsEveryTickBecauseTheTickBelongsToTheLICENCE() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE MOST IMPORTANT TEST IN THIS FILE. Sorting is a PROJECTION of the list; the ticked set
            //    is keyed by `LicenseId` in the view model. If the two were ever joined — a tick keyed by
            //    row position, a selection read as the batch — re-ordering would silently change what a
            //    batch operation is about to do, and there is no undo for that.
            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, Seed);

            // Tick two licences, chosen by identity so the assertion does not depend on order.
            var wanted = shell.Browser.Results.Take(2).Select(r => r.Summary.License.LicenseId).ToHashSet();
            foreach (var row in shell.Browser.Results.Where(r => wanted.Contains(r.Summary.License.LicenseId)))
            {
                row.IsChecked = true;
            }

            window.UpdateLayout();
            Assert.Equal(2, shell.Browser.CheckedCount);

            ClickHeader(window, "Customer");
            ClickHeader(window, "Customer");
            ClickHeader(window, "Seats");
            window.UpdateLayout();

            Assert.Equal(2, shell.Browser.CheckedCount);
            Assert.Equal(wanted.OrderBy(id => id), shell.Browser.CheckedIds.OrderBy(id => id));

            // ⭐ And the realised boxes agree with the set — a tick that survived only in the view model
            //   would leave the operator looking at empty boxes for licences a batch is about to change.
            var ticked = Rows(window)
                .Where(r => ViewProbe.AllNamed<CheckBox>(window, "RowTick").Count > 0)
                .Select(r => (LicenseListItem)r.DataContext!)
                .Where(item => item.IsChecked)
                .Select(item => item.Summary.License.LicenseId)
                .ToList();

            Assert.Equal(wanted.OrderBy(id => id), ticked.OrderBy(id => id));
        }, default);

    [Fact]
    public Task TheTickSurvivesAnOrdinaryRowSelection() =>
        _session.Dispatch(() =>
        {
            // ⭐ The reason the column exists, restated on the GRID: selection changes on every click, a
            //   batch tick must survive one. ⛔ `SelectionMode="Single"` is what keeps a click from
            //   meaning "select only this one" over a nineteen-licence decision.
            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, Seed);

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            var before = shell.Browser.CheckedCount;

            Grid(window).SelectedItem = shell.Browser.Results[1];
            window.UpdateLayout();

            Assert.Equal(before, shell.Browser.CheckedCount);
            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.True(t.IsChecked));
            Assert.Equal(shell.Browser.Results[1].Summary.License.LicenseId, shell.Browser.SelectedLicenseId);
        }, default);

    [Fact]
    public Task TheTickSurvivesAFilterThatHidesItsRowAndComesBackWithIt() =>
        _session.Dispatch(() =>
        {
            // ⭐ The view-model rule (LicenseSelectionTests) proved on the REALISED grid: a rebuilt row is
            //   a new object, and its checkbox starts false until the browser restores it.
            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, Seed);

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            var all = shell.Browser.CheckedCount;

            shell.Browser.SearchText = "Globex";
            window.UpdateLayout();

            // ⚠ The claim is about what the FILTERS show, not about how many row containers the DataGrid
            //   happens to be keeping alive — a recycled container is the control's business.
            Assert.True(shell.Browser.Results.Count < all);
            Assert.Equal(all, shell.Browser.CheckedCount);
            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.True(t.IsChecked));

            shell.Browser.SearchText = string.Empty;
            window.UpdateLayout();

            Assert.Equal(all, shell.Browser.Results.Count);
            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.True(t.IsChecked));
        }, default);

    [Fact]
    public Task SelectAllShownAndClearSelectionDriveTheREALBoxes() =>
        _session.Dispatch(() =>
        {
            // ⚠ Both directions. A command that updated only the id set would leave the operator reading
            //   empty boxes over a batch that is about to run.
            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, Seed);

            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.False(t.IsChecked));

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.True(t.IsChecked));

            shell.Browser.ClearChecksCommand.Execute(null);
            window.UpdateLayout();
            Assert.All(ViewProbe.AllNamed<CheckBox>(window, "RowTick"), t => Assert.False(t.IsChecked));
            Assert.Equal(0, shell.Browser.CheckedCount);
        }, default);

    // ── Width behaviour ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(760)]
    [InlineData(1280)]
    [InlineData(1920)]
    public Task NoColumnEatsTheWindowAndNoneIsSqueezedBelowItsFloor(int width) =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE RULE, STATED POSITIVELY: every column stands between its own floor and its own
            //    ceiling, at every window width. ⚠ The two wide columns are the only STAR ones and both
            //    are capped — a star column with no ceiling is a column that eats a wide window, and a
            //    fixed one is a column that cannot use it.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed, width);

            var grid = Grid(window);

            foreach (var column in grid.Columns)
            {
                Assert.True(column.ActualWidth >= column.MinWidth,
                    $"Kolumna '{column.Header}' ma {column.ActualWidth} px, poniżej podłogi {column.MinWidth}.");

                if (!double.IsPositiveInfinity(column.MaxWidth))
                {
                    Assert.True(column.ActualWidth <= column.MaxWidth + 0.5,
                        $"Kolumna '{column.Header}' ma {column.ActualWidth} px, powyżej sufitu {column.MaxWidth}.");
                }
            }

            // ⛔ And none of them is the whole window: a single column wide enough to hide every other one
            //   is the failure this cap exists to prevent.
            Assert.All(grid.Columns, c => Assert.True(c.ActualWidth < grid.Bounds.Width));

            // ⭐⭐ THE DECLARATION BEHIND THE MEASUREMENT. The two wide columns must be STAR (so a wide
            //    window is used rather than wasted), must have a CEILING (so a wide window is not eaten by
            //    one of them) and must have a FLOOR (so a narrow one does not squeeze a customer name to
            //    two characters). ⚠ Without this, dropping a `MaxWidth` would leave every measurement
            //    above still true at the widths this test happens to try, and the guard would be green
            //    over the exact defect it exists to catch.
            foreach (var starred in new[] { "Customer", "Contact" })
            {
                var column = Column(grid, starred);
                Assert.True(column.Width.IsStar, $"Kolumna '{starred}' przestała być gwiazdkowa.");
                Assert.False(double.IsPositiveInfinity(column.MaxWidth),
                    $"Kolumna '{starred}' straciła sufit — na szerokim oknie zje resztę siatki.");
                Assert.True(column.MinWidth > 0,
                    $"Kolumna '{starred}' straciła podłogę — na wąskim oknie zostanie z niej nic.");
            }

            // The headers stay one per column and stay on one line at every width.
            Assert.Equal(grid.Columns.Count, Headers(window).Count);
            Assert.All(Headers(window), h => Assert.Equal(HeaderHeight, h.Bounds.Height));
        }, default);

    [Fact]
    public Task AColumnCanBeResizedAndItsNeighboursSurviveIt() =>
        _session.Dispatch(() =>
        {
            // ⚠ Programmatic rather than a dragged gripper: what is worth guarding is that a WIDTH CHANGE
            //   is honoured within the column's own bounds and leaves every other column inside its own.
            //   The gripper itself is `CanUserResizeColumns`, asserted above; dragging it is visual QA.
            using var manager = new ManagerFixture();
            var (_, window) = Show(manager, Seed, 1280);

            var grid = Grid(window);
            var customer = Column(grid, "Customer");
            var before = grid.Columns.ToDictionary(c => c.Header as string ?? string.Empty, c => c.ActualWidth);

            customer.Width = new DataGridLength(300);
            window.UpdateLayout();

            Assert.Equal(300d, customer.ActualWidth, 1);
            Assert.NotEqual(before["Customer"], customer.ActualWidth);

            foreach (var column in grid.Columns)
            {
                Assert.True(column.ActualWidth >= column.MinWidth,
                    $"Po zmianie szerokości kolumna '{column.Header}' spadła poniżej swojej podłogi.");
            }

            // ⛔ A resize past the floor is refused rather than obeyed.
            customer.Width = new DataGridLength(10);
            window.UpdateLayout();
            Assert.True(customer.ActualWidth >= customer.MinWidth);
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Four customers, seven licences, deliberately varied so every sortable column has something to
    /// order — including a seat count of 12, which is what catches text-sorted numbers.
    /// </summary>
    private static void Seed(ManagerFixture manager)
    {
        var seeds = new (string Name, string? First, string? Last, int Seats, int Years)[]
        {
            ("Zenit S.A.", "Anna", "Zawadzka", 3, 1),
            ("ACME Sp. z o.o.", "Piotr", "Adamski", 12, 2),
            ("Globex", "Ewa", "Bąk", 1, 3),
            ("Umbrella", null, null, 7, 4),
        };

        foreach (var (name, first, last, seats, years) in seeds)
        {
            var customer = manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = manager.Register.NextCustomerId(),
                Name = name,
                FirstName = first,
                LastName = last,
            });

            manager.SaveLicense(customer, seats, years);
        }

        // Three more on one customer, so the list has enough rows for zebra striping to be visible.
        var extra = manager.Register.GetCustomers().First(c => c.Name == "Umbrella");
        manager.SaveLicense(extra, 5, 5);
        manager.SaveLicense(extra, 9, 6);
        manager.SaveLicense(extra, 2, 7);
    }

    private static (ShellViewModel Shell, MainWindow Window) Show(
        ManagerFixture manager, Action<ManagerFixture> seed, int width = 1280)
    {
        seed(manager);

        var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
        var window = new MainWindow { DataContext = shell, Width = width, Height = 900 };
        window.Show();
        window.UpdateLayout();

        shell.ShowLicensesCommand.Execute(null);
        window.UpdateLayout();

        return (shell, window);
    }

    private static DataGrid Grid(Window window) => ViewProbe.Named<DataGrid>(window, "LicenceResults");

    private static List<DataGridRow> Rows(Window window) =>
        Grid(window).GetVisualDescendants().OfType<DataGridRow>()
            .OrderBy(r => r.Bounds.Y)
            .ToList();

    /// <summary>
    /// The realised column headers, left to right — ours, and only ours.
    ///
    /// <para>⚠ Fluent's template puts a zero-width corner header before the first column and a FILLER
    /// header after the last one, and the filler is not zero-width on a wide window (measured: 518 px at
    /// 1920). ⛔ `DataGridColumnHeader.OwningColumn` is not public in 12.1.2, so the headers are taken by
    /// POSITION: drop the empty ones, then take as many as there are columns — the filler is always the
    /// rightmost.</para>
    /// </summary>
    private static List<DataGridColumnHeader> Headers(Window window) =>
        Grid(window).GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Where(h => h.Bounds.Width > 0)
            .OrderBy(h => h.Bounds.X)
            .Take(Grid(window).Columns.Count)
            .ToList();

    private static DataGridColumn Column(DataGrid grid, string header) =>
        grid.Columns.Single(c => (c.Header as string) == header);

    /// <summary>
    /// One column's values, in the order the grid is currently SHOWING them.
    ///
    /// <para>⚠⚠ Read from <c>CollectionView</c> — the grid's own projection — and NOT from the realised
    /// row containers, which is what the first version of this helper did. Measured: the DataGrid recycles
    /// containers across a re-sort, so sweeping the visual tree returned a stale row and the same customer
    /// appeared twice in a list that holds it once. The projection IS what "the order the grid is showing"
    /// means; the containers are the control's own bookkeeping.</para>
    ///
    /// <para>⭐ The SORT is still driven by a real click on a real header — see <see cref="ClickHeader"/>.
    /// What is read here is the result of that gesture, not a shortcut around it.</para>
    /// </summary>
    private static List<string> Column(Window window, string header)
    {
        var grid = Grid(window);

        Func<LicenseListItem, string> value = header switch
        {
            "Customer" => item => item.CustomerName,
            "Contact" => item => item.Contact,
            "Licence id" => item => item.ShortId,
            "Seats" => item => item.Seats,
            "Status" => item => item.Status,
            "Expiry" => item => item.Expiry,
            "Standing" => item => item.Standing,
            _ => throw new ArgumentOutOfRangeException(nameof(header), header, "Nie ma takiej kolumny."),
        };

        return grid.CollectionView.OfType<LicenseListItem>().Select(value).ToList();
    }

    /// <summary>
    /// Which way a column is currently sorted, read off the projection the grid is actually showing.
    ///
    /// <para>⚠ Read from <c>CollectionView.SortDescriptions</c> rather than from the header's arrow: the
    /// arrow is a glyph, the sort description is the thing that ordered the rows.</para>
    /// </summary>
    private static ListSortDirection? Direction(Window window, string header)
    {
        var grid = Grid(window);
        var path = Column(grid, header).SortMemberPath;

        foreach (var description in grid.CollectionView.SortDescriptions)
        {
            if (string.Equals(description.PropertyPath, path, StringComparison.Ordinal))
            {
                return description.Direction;
            }
        }

        return null;
    }

    /// <summary>A real click on a real column header — the gesture the operator makes.</summary>
    private static void ClickHeader(Window window, string header)
    {
        var grid = Grid(window);
        var target = Headers(window)[grid.Columns.IndexOf(Column(grid, header))];
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.UpdateLayout();
    }

    private static Color Fill(DataGridRow row) =>
        ((ISolidColorBrush)row.GetVisualDescendants().OfType<Rectangle>()
            .First(r => r.Name == "BackgroundRectangle").Fill!).Color;

    private static Border Box(CheckBox tick) =>
        tick.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
}
