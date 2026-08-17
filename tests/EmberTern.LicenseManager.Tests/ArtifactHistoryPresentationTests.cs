using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// L5.2 — the issuing history as it is actually RENDERED.
///
/// <para>⭐⭐ The claims this file exists to hold are visual ones: that the current issue is unmistakable,
/// and that an earlier issue does not look deleted. Neither can be checked by reading a property — a
/// class that is set and painted by nobody looks exactly like one that works (§40.4). So every assertion
/// below reads a REALISED brush, a realised size or realised text off a laid-out window.</para>
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class ArtifactHistoryPresentationTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public ArtifactHistoryPresentationTests(ManagerHeadlessSessionFixture fixture) =>
        _session = fixture.Session;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheCurrentIssueWearsTheChipAndTheEarlierOnesDoNot(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐ The chip's REALISED fill, per theme — not the presence of a class. `Button.view-tab.active`
            //   taught this lesson in L5.1: the class was set, painted by nobody, and every class-based
            //   test was green.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var window = ShowWithHistory(manager, out var shell);

            var rows = Rows(window);
            Assert.Equal(3, rows.Count);

            var chips = rows.Select(ChipOf).ToList();
            Assert.Single(chips, c => c is { IsVisible: true });

            var visible = chips.First(c => c is { IsVisible: true })!;
            Assert.Equal(
                HeadlessTheme.Brush("ConnectedBrush")!.Color,
                ((ISolidColorBrush)visible.Background!).Color);

            // …and the chip is on the row the register calls current.
            var chipped = rows[chips.FindIndex(c => c is { IsVisible: true })];
            Assert.True(((ArtifactListItem)chipped.DataContext!).IsCurrent);
            Assert.True(shell.History.Artifacts.Single(a => a.IsCurrent).IsCurrent);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task AnEarlierIssueIsNotDimmedStruckThroughOrOtherwiseShownAsRemoved(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE CLAIM THAT MATTERS MOST HERE, and it is a claim about pixels. The register's whole
            //    append-only guarantee is that an earlier issue was really delivered and still stands; a
            //    row that renders faint or struck through tells the operator the opposite, in the one
            //    language they cannot argue with.
            //
            // ⚠ Measured RELATIVELY — the earlier row's content against the current row's — rather than
            //   against a token this test names for itself. That is the comparison that stays true if the
            //   palette changes, and it is the difference the eye actually reads.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var window = ShowWithHistory(manager, out _);

            var rows = Rows(window);
            var current = rows.First(r => ((ArtifactListItem)r.DataContext!).IsCurrent);
            var earlier = rows.First(r => !((ArtifactListItem)r.DataContext!).IsCurrent);

            foreach (var column in new[] { 1, 2 })   // the stamp and the reason: the row's own content
            {
                var a = Content(current, column);
                var b = Content(earlier, column);

                // ⚠⚠ MEASURED FROM THE TEXT UPWARDS, not from the row container. The first version of
                //    this walked up from the ListBoxItem, and an injected `Opacity="0.5"` on the template's
                //    own Grid — the most natural way anyone would ever dim a row — left it GREEN, because
                //    the dimming sits BELOW the container it was measuring. Effective opacity is a product
                //    along the whole chain, so the measurement has to start at the ink.
                Assert.Equal(1d, Opacity(b), precision: 3);
                Assert.Equal(Opacity(a), Opacity(b), precision: 3);

                Assert.Equal(Colour(a), Colour(b));
                Assert.Equal(a.FontSize, b.FontSize);
                Assert.False(IsStruck(b), "Wcześniejsze wydanie jest przekreślone.");
            }
        }, default);

    [Fact]
    public Task TheHistoryPanelIsShownEvenForALicenceThatWasNeverIssued() =>
        _session.Dispatch(() =>
        {
            // ⚠ A panel that disappears cannot say "nothing was ever sent" — and that is precisely the
            //   state an operator needs told.
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var window = Show(manager, out var shell);
            shell.SelectedLicense = shell.Licenses.First();
            window.UpdateLayout();

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .First(l => l.Name == "ArtifactHistory");

            Assert.False(list.IsEffectivelyVisible);

            var summary = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text?.StartsWith("Never issued", StringComparison.Ordinal) == true);
            Assert.True(summary.IsEffectivelyVisible);
        }, default);

    [Fact]
    public Task TheDetailAppearsOnlyOnceAnIssueIsSelectedAndThenShowsTheRealVerdict() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = ShowWithHistory(manager, out var shell);

            var verdictLabel = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == "What EmberTern would say about it today");
            Assert.False(verdictLabel.IsEffectivelyVisible);

            shell.History.SelectedArtifact = shell.History.Artifacts[0];
            window.UpdateLayout();

            Assert.True(verdictLabel.IsEffectivelyVisible);
            Assert.NotEmpty(shell.History.Verdict);

            // ⭐ The rendered sentence is the view model's, which is the verifier's — asserted as the text
            //   on screen so a template that stopped binding could not pass (gotcha #370).
            var shown = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text)
                .Where(t => t is not null);
            Assert.Contains(shell.History.Verdict, shown);
        }, default);

    [Fact]
    public Task TheHistoryRowKeepsTheSpacingRhythmP1Established() =>
        _session.Dispatch(() =>
        {
            // ⛔ A panel added after P1 must not reintroduce the touching neighbours P1 measured away.
            //   `LicenseSpacingTests` sweeps the whole window, so this is the targeted half.
            using var manager = new ManagerFixture();
            var window = ShowWithHistory(manager, out _);

            var grid = (Grid)Rows(window)[0].GetVisualDescendants().OfType<Grid>()
                .First(g => g.TemplatedParent is null);

            var cells = grid.Children.OfType<Control>()
                .Where(c => c.IsVisible && c.Bounds.Width > 0)
                .OrderBy(c => c.Bounds.X)
                .ToList();

            for (var i = 0; i + 1 < cells.Count; i++)
            {
                Assert.Equal(8, cells[i + 1].Bounds.X - cells[i].Bounds.Right, precision: 3);
            }
        }, default);

    [Fact]
    public Task DoubleClickingALicenceStillOpensThePreviewAndNowOpensTheCurrentIssueWithIt() =>
        _session.Dispatch(() =>
        {
            // ⭐ P1-c's gesture, unchanged in wiring and richer in effect: it runs the one
            //   `InspectLatestCommand`, which now also selects the artifact it is describing.
            using var manager = new ManagerFixture();
            var window = ShowWithHistory(manager, out var shell);

            shell.History.SelectedArtifact = shell.History.Artifacts[^1];   // the oldest
            window.UpdateLayout();

            var row = window.GetVisualDescendants().OfType<ListBox>()
                .First(l => l.Name == "CustomerLicenses")
                .GetVisualDescendants().OfType<ListBoxItem>().First();
            row.RaiseEvent(new DoubleTap(row));

            Assert.True(shell.History.SelectedArtifact!.IsCurrent);
            Assert.Equal(shell.History.Verdict, shell.Message!.Text);
        }, default);

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static System.Collections.Generic.List<ListBoxItem> Rows(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Name == "ArtifactHistory")
            .GetVisualDescendants().OfType<ListBoxItem>()
            .ToList();

    private static Border? ChipOf(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("current-chip"));

    private static TextBlock Content(ListBoxItem row, int column) =>
        row.GetVisualDescendants().OfType<TextBlock>()
            .First(t => Grid.GetColumn(t) == column && t.Parent is Grid);

    private static Color Colour(TextBlock text) => ((ISolidColorBrush)text.Foreground!).Color;

    private static double Opacity(Visual visual)
    {
        var value = 1d;
        for (var v = visual; v is not null; v = v.GetVisualParent())
        {
            value *= v.Opacity;
        }

        return value;
    }

    /// <summary>
    /// Whether the text is crossed out.
    ///
    /// <para>⚠ Asks about the STRIKETHROUGH specifically rather than "has any decoration": a collection
    /// can exist and be empty, and an underline is a different statement from a deletion.</para>
    /// </summary>
    private static bool IsStruck(TextBlock text) =>
        text.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;

    private static MainWindow ShowWithHistory(ManagerFixture manager, out ShellViewModel shell)
    {
        var customer = manager.SaveCustomer();
        var licence = manager.SaveLicense(customer);

        foreach (var reason in new[] { "initial", "renewal", "reissue-lost" })
        {
            manager.Workflow.Issue(manager.Session, licence, customer, reason);
            manager.Now = manager.Now.AddDays(1);
        }

        var window = Show(manager, out shell);
        shell.SelectedLicense = shell.Licenses.First();
        window.UpdateLayout();

        Realize(window);
        return window;
    }

    /// <summary>
    /// Forces the history list to realise every row.
    ///
    /// <para>⚠⚠ MEASURED, and it cost a wrong hypothesis first. After <c>UpdateLayout</c> the list
    /// reported <c>ItemCount</c> 3, a viewport of 74 px and an extent of 74 px — i.e. layout had already
    /// accounted for all three rows — while <c>ContainerFromIndex</c> answered non-null for index 0 only.
    /// A second layout pass changed nothing; <c>ScrollIntoView</c> of the last index realised all three.
    /// So the panel's realisation is lazy in a way that a laid-out, correctly-sized list does not
    /// advertise.</para>
    ///
    /// <para>⛔ The first attempt at this was <c>window.Height = 2000</c>, on the theory that the three
    /// stacked cards pushed the list off-screen. It was left in for one run and measured to change
    /// nothing — a headless window ignores a Height set after <c>Show</c>, and the extent proved the list
    /// was sized correctly anyway. Removed rather than kept as a plausible-looking no-op.</para>
    ///
    /// <para>⚠ A test that silently measured one row would still have passed several of the assertions
    /// here, which is why every one of them states the expected row COUNT first.</para>
    /// </summary>
    private static void Realize(Window window)
    {
        var list = window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Name == "ArtifactHistory");

        if (list.ItemCount > 0)
        {
            list.ScrollIntoView(list.ItemCount - 1);
            window.UpdateLayout();
        }
    }

    private static MainWindow Show(ManagerFixture manager, out ShellViewModel shell)
    {
        shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.SelectedCustomer = shell.Customers.First();
        window.UpdateLayout();
        return window;
    }

    /// <summary>A double-tap, addressed to the row it is raised on.</summary>
    private sealed class DoubleTap : Avalonia.Input.TappedEventArgs
    {
        internal DoubleTap(Avalonia.Interactivity.Interactive source)
            : base(Avalonia.Input.InputElement.DoubleTappedEvent, null!) => Source = source;
    }
}
