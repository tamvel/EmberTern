using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// P1-b — one row, one height, asserted as REALISED height.
///
/// <para>⭐⭐ Every number here is <see cref="Visual.Bounds"/> after layout, compared against the
/// NEIGHBOURS in the same row — never against the control's own <c>DesiredSize</c>, which is the
/// comparison that always agrees with itself. The reported defect was a 32 px date picker standing beside
/// a 24 px <c>Seats</c>; a test that read either control on its own would have found both of them
/// perfectly self-consistent.</para>
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenseControlSizeTests
{
    /// <summary>The field ladder — <c>Size.Control</c>.</summary>
    private const double Field = 24;

    /// <summary>The action ladder — <c>Size.ControlProminent</c>.</summary>
    private const double Action = 28;

    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenseControlSizeTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public Task SeatsAndBothDateFieldsStandAtTheSameHeight() =>
        _session.Dispatch(() =>
        {
            // ⚠ MEASURED BEFORE THE FIX: Seats 24, both pickers 32 — Fluent's own CalendarDatePicker
            //   setter, on the action ladder rather than the field one.
            var window = Show(out _);

            var seats = Seats(window);
            var pickers = window.GetVisualDescendants().OfType<CalendarDatePicker>().ToList();

            Assert.Equal(2, pickers.Count);
            foreach (var picker in pickers)
            {
                Assert.Equal(seats.Bounds.Height, picker.Bounds.Height, precision: 3);
            }
        }, default);

    [Fact]
    public Task TheDateFieldSitsOnTheFieldLadderAndNotOnTheActionLadder() =>
        _session.Dispatch(() =>
        {
            // ⭐ Not merely "equal to its neighbour" — equal to the RIGHT number. Two controls that agree
            //   at 32 would satisfy the test above and would still be a form field wearing a button's
            //   height. Tokens.axaml keeps the two ladders apart on purpose.
            var window = Show(out _);

            Assert.Equal(Field, Seats(window).Bounds.Height, precision: 3);

            foreach (var picker in window.GetVisualDescendants().OfType<CalendarDatePicker>())
            {
                Assert.Equal(Field, picker.Bounds.Height, precision: 3);
            }
        }, default);

    [Fact]
    public Task TheBoxInsideTheDatePickerGivesUpItsOwnFloorSoTheFieldCanOwnItsHeight() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE MEASURED REASON THE OUTER SETTER IS NOT ENOUGH. With PART_TextBox left on the base
            //   TextBox style's MinHeight of 24, the template's own 1 px inset makes the picker 26 — so
            //   the field asks for 24, is told 24 is allowed, and still renders 26.
            //   ⚠ This asserts the mechanism, because the mechanism is what a later edit would undo:
            //   somebody adding `MinHeight` back to the inner box would leave every height assertion
            //   above failing with no clue why.
            var window = Show(out _);

            var inner = window.GetVisualDescendants().OfType<CalendarDatePicker>()
                .Select(p => p.GetVisualDescendants().OfType<TextBox>().First())
                .ToList();

            Assert.NotEmpty(inner);
            foreach (var box in inner)
            {
                Assert.Equal(0, box.MinHeight);
            }
        }, default);

    [Fact]
    public Task EveryActionInTheLicenceFooterIsTheSameHeight() =>
        _session.Dispatch(() =>
        {
            // ⭐ WIDTH IS DELIBERATELY NOT ASSERTED. `Size.ActionMinWidth` is a FLOOR, and above it the
            //   label decides — so these four measure 146 / 194 / 194 / 206 and that is the design, not
            //   drift. Height is the property that has to agree, and it is the one asserted.
            var window = Show(out _);

            var footer = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content is string text &&
                            text is "Inspect latest" or "Export latest…" or "Save terms" or "Issue and save…")
                .ToList();

            Assert.Equal(4, footer.Count);
            foreach (var button in footer)
            {
                Assert.Equal(Action, button.Bounds.Height, precision: 3);
            }
        }, default);

    [Fact]
    public Task ADropdownLabelTooLongForItsBoxLosesItsTailRatherThanGainingALine() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE DEFECT THE SWEEP BELOW FOUND, pinned on its own so it cannot be lost to a shorter
            //   label. Measured: the "Issuing" filter rendered "Issued or not" on TWO lines and stood at
            //   34 px beside two identical dropdowns at 24 — the base TextBlock style wraps, which is
            //   right for a caption and wrong for a value.
            //
            // ⚠ Squeezed to the window's own MinWidth first, so the assertion does not depend on a
            //   particular label happening to be long. At 880 px every filter column is narrow.
            var window = Show(out var shell);
            shell.ShowLicensesCommand.Execute(null);
            window.Width = window.MinWidth;
            window.UpdateLayout();

            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(t => t.PlaceholderText?.StartsWith("Customer,", StringComparison.Ordinal) == true);

            foreach (var box in window.GetVisualDescendants().OfType<ComboBox>())
            {
                Assert.Equal(search.Bounds.Height, box.Bounds.Height, precision: 3);
            }
        }, default);

    [Fact]
    public Task NoNeighbourInAFormRowIsTallerThanAnyOtherByMoreThanTheLadderGap() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ Bounded by the domain: any input standing beside another input, in any row of either
            //   view, on either ladder — not the three controls the user happened to photograph.
            //   ⚠ Captions and multi-line boxes are excluded BY WHAT THEY ARE, not by name: a label is
            //   not an input, and a note box owns its own height by definition.
            var window = Show(out var shell);
            AssertRowsAgree(window);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();
            AssertRowsAgree(window);
        }, default);

    private static void AssertRowsAgree(Window window)
    {
        foreach (var grid in window.GetVisualDescendants().OfType<Grid>())
        {
            if (grid.TemplatedParent is not null || grid.ColumnDefinitions.Count < 2)
            {
                continue;
            }

            var inputs = grid.GetVisualDescendants().OfType<Control>()
                .Where(c => c is TextBox or ComboBox or CalendarDatePicker)
                .Where(c => c.TemplatedParent is null)
                .Where(c => c is not TextBox box || !box.AcceptsReturn)
                .Where(c => c.IsVisible && c.Bounds.Height > 0)
                .ToList();

            if (inputs.Count < 2)
            {
                continue;
            }

            var tallest = inputs.MaxBy(c => c.Bounds.Height)!;
            var shortest = inputs.MinBy(c => c.Bounds.Height)!;

            Assert.True(
                Math.Abs(tallest.Bounds.Height - shortest.Bounds.Height) < 0.001,
                string.Create(CultureInfo.InvariantCulture,
                    $"Kontrolki w jednym wierszu mają różne wysokości: {tallest.GetType().Name} = " +
                    $"{tallest.Bounds.Height:0.##}, {shortest.GetType().Name} = {shortest.Bounds.Height:0.##}."));
        }
    }

    private static TextBox Seats(Window window)
    {
        var caption = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == "Seats");

        return caption.GetVisualAncestors().OfType<Panel>().First()
            .GetVisualDescendants().OfType<TextBox>().First();
    }

    private static MainWindow Show(out ShellViewModel shell)
    {
        var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);

        shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.SelectedCustomer = shell.Customers.First();
        shell.SelectedLicense = shell.Licenses.First();
        window.UpdateLayout();
        return window;
    }
}
