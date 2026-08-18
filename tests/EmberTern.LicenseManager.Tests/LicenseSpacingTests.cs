using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// P1-a — the horizontal rhythm, asserted as REALISED DISTANCE.
///
/// <para>⭐⭐ Every assertion here reads <see cref="Visual.Bounds"/> off a laid-out window and subtracts:
/// the gap between two neighbours is <c>next.Bounds.X - previous.Bounds.Right</c>. ⛔ Nothing in this file
/// asserts that a property was SET. The defect P1-a fixed would have passed such a test perfectly —
/// `Margin.InlineGap` was set, on every row, and produced a gap of ZERO between the pair it was meant to
/// separate, because the token is right-handed and it was hung on the left-hand element's neighbour.
/// A test that checks the setter checks the thing that was already true.</para>
///
/// <para>⚠ Measurement is taken against the CONTAINER that actually positions the element (its parent
/// grid's other children), never against the element's own DesiredSize — a control compared with itself
/// agrees with itself.</para>
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenseSpacingTests
{
    /// <summary>Two independent things side by side — <c>Space.Md</c>.</summary>
    private const double Independent = 8;

    /// <summary>Parts of one compound element — <c>Space.Sm</c>.</summary>
    private const double Compound = 6;

    /// <summary>A value and the affordance that acts on it — <c>Space.Xs</c>.</summary>
    private const double Attached = 4;

    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenseSpacingTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── The rows the user pointed at, each with its measured distance ────────────────────────────────

    [Fact]
    public Task TheCustomerNameAndItsGeneratedIdentifierDoNotTouch() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager, out _);

            AssertGaps(RowOf(window, "Identifier"), Independent);
        }, default);

    [Fact]
    public Task TheGeneratedIdentifierAndItsCopyActionAreOneObjectRatherThanTwo() =>
        _session.Dispatch(() =>
        {
            // ⭐ 4, not 8, and the difference carries meaning: the Copy button acts on the value beside
            //   it, so the pair has to read as one thing. At the row spacing it would read as two.
            using var manager = new ManagerFixture();
            var window = Show(manager, out _);

            var value = window.GetVisualDescendants().OfType<SelectableTextBlock>()
                .First(t => t.Classes.Contains("value"));

            AssertGaps((Grid)value.GetVisualParent()!, Attached);
        }, default);

    [Fact]
    public Task TheContactRowDoesNotRunFirstNameIntoLastName() =>
        _session.Dispatch(() =>
        {
            // ⚠ The measured before-state: First│Last = 0 and Last│E-mail = 8. One row, two different
            //   distances, because the 8 belonged to the wrong element.
            using var manager = new ManagerFixture();
            var window = Show(manager, out _);

            AssertGaps(RowOf(window, "First name"), Independent);
        }, default);

    [Fact]
    public Task SeatsDoesNotRunIntoTheFirstDateField() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager, out _);

            AssertGaps(RowOf(window, "Seats"), Independent);
        }, default);

    [Fact]
    public Task TheSeverityGlyphIsSeparatedFromTheStripeAndFromItsMessage() =>
        _session.Dispatch(() =>
        {
            // ⚠⚠ The measured before-state, and the reason a "just add a margin" fix would have been
            //   wrong: stripe→glyph was 0 while glyph→text was SIXTEEN — the glyph's own right margin
            //   plus the text's own left margin, two owners paying into one gap and neither paying into
            //   the one that was missing.
            using var manager = new ManagerFixture();
            var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now)
            {
                Message = StatusMessage.Warning("careful"),
            };
            var window = Show(shell);

            var strip = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("message"));

            var glyph = strip.GetVisualDescendants().OfType<Viewbox>().First();
            var content = (Grid)glyph.GetVisualParent()!;
            var stripe = strip.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("severity-stripe"));

            Assert.Equal(Independent, content.Bounds.X - stripe.Bounds.Right, precision: 3);
            AssertGaps(content, Compound);
        }, default);

    [Fact]
    public Task TheFilterRowSpacesEveryPairTheSameWay() =>
        _session.Dispatch(() =>
        {
            // ⚠ The before-state was 0 / 8 / 0 / 8 across four filters and a button: alternating, because
            //   the token was hung on every SECOND column.
            using var manager = new ManagerFixture();
            var window = Show(manager, out var shell);
            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            AssertGaps(RowOf(window, "Search"), Independent);
        }, default);

    // ── The rule, not the reports ────────────────────────────────────────────────────────────────────

    [Fact]
    public Task NoTwoNeighboursInAnyRowOfThisWindowTouch() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ Bounded by the DOMAIN rather than by the six rows the user happened to see. A seventh
            //   row added later with the same mistake fails here without anyone remembering to add a
            //   case — which is the difference between a rule and an exception list.
            //
            // ⚠ `TemplatedParent is null` is what keeps this OURS: a control template's internal grid
            //   (a ScrollViewer's, a TextBox's) legitimately packs its parts flush, and is not this
            //   application's to space.
            using var manager = new ManagerFixture();
            var window = Show(manager, out var shell);

            var offences = new List<string>();
            Sweep(window, offences);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();
            Sweep(window, offences);

            Assert.True(offences.Count == 0,
                "Sąsiedzi stykają się bez odstępu:" + Environment.NewLine +
                string.Join(Environment.NewLine, offences));
        }, default);

    private static void Sweep(Window window, List<string> offences)
    {
        foreach (var grid in window.GetVisualDescendants().OfType<Grid>())
        {
            if (grid.TemplatedParent is not null || grid.ColumnDefinitions.Count < 2)
            {
                continue;
            }

            var cells = Cells(grid);
            for (var i = 0; i + 1 < cells.Count; i++)
            {
                var gap = cells[i + 1].Bounds.X - cells[i].Bounds.Right;
                if (gap < Attached)
                {
                    offences.Add(string.Create(CultureInfo.InvariantCulture,
                        $"  {Describe(cells[i])} → {Describe(cells[i + 1])} = {gap:0.##} px"));
                }
            }
        }
    }

    /// <summary>
    /// The laid-out CONTENT children of <paramref name="grid"/>, one per column, left to right.
    ///
    /// <para>⚠ Zero-width and collapsed children are dropped: a control that renders nothing cannot be
    /// too close to anything, and keeping it would make the neighbour of an empty cell look adjacent to
    /// something two columns away.</para>
    ///
    /// <para>⭐⭐ A <see cref="GridSplitter"/> is dropped too, and that is the rule rather than an
    /// exception to it: the rhythm is a distance between two pieces of CONTENT, and a splitter is not
    /// content — it is the boundary. A boundary that does not touch what it bounds is a line floating in
    /// a gutter. ⚠ Found by the sweep rather than anticipated: it reported the customer rail and the
    /// detail pane each sitting 0 px from the handle, which is exactly what a handle is for. Dropping it
    /// leaves the two PANES measured against each other, across the handle — which is the distance that
    /// actually means something.</para>
    /// </summary>
    private static List<Control> Cells(Grid grid) =>
        grid.Children.OfType<Control>()
            .Where(c => c is not GridSplitter)
            .Where(c => c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            .GroupBy(Grid.GetColumn)
            .Select(g => g.OrderByDescending(c => c.Bounds.Width).First())
            .OrderBy(c => c.Bounds.X)
            .ToList();

    private static void AssertGaps(Grid grid, double expected)
    {
        var cells = Cells(grid);
        Assert.True(cells.Count >= 2, "Wiersz ma mniej niż dwie widoczne kolumny — nie ma czego mierzyć.");

        for (var i = 0; i + 1 < cells.Count; i++)
        {
            Assert.Equal(expected, cells[i + 1].Bounds.X - cells[i].Bounds.Right, precision: 3);
        }
    }

    /// <summary>The grid holding the field whose caption is <paramref name="label"/>.</summary>
    private static Grid RowOf(Window window, string label)
    {
        var caption = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text?.StartsWith(label, StringComparison.Ordinal) == true &&
                        t.Classes.Contains("field-label"));

        return caption.GetVisualAncestors().OfType<Grid>()
            .First(g => g.TemplatedParent is null && g.ColumnDefinitions.Count >= 2);
    }

    private static string Describe(Control control) =>
        control.Name ?? (control as TextBlock)?.Text ?? control.GetType().Name;

    private static MainWindow Show(ManagerFixture manager, out ShellViewModel shell)
    {
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);

        shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
        var window = Show(shell);

        shell.SelectedCustomer = shell.Customers.First();
        shell.SelectedLicense = shell.Licenses.First();
        window.UpdateLayout();
        return window;
    }

    private static MainWindow Show(ShellViewModel shell)
    {
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
