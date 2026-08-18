using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;
using static EmberTern.LicenseManager.Tests.ViewProbe;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// L5.3 on screen: the reason picker, the D‑6 steer away from a needless re-issue, and the Seats field
/// after its literal width was removed (D‑8).
///
/// <para>⚠⚠ Everything here is asserted on the REALISED tree — rendered text and <see cref="Visual.Bounds"/>
/// after layout — never on what the XAML spells or on a view-model flag. Gotcha #370: a
/// <c>DataTemplate</c> whose <c>x:DataType</c> stops matching produces no binding error at all, it
/// silently renders the item's <c>ToString()</c>; and gotcha #377: a virtualised list can look right by
/// its counts while no container was ever realised.</para>
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class IssueReasonUiTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public IssueReasonUiTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task ThePickerShowsTheReasonsInWordsAndNotTheShapeOfTheirRecord(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (window, shell) = ShowIssued(manager);

            var picker = Picker(window);

            // ⚠ Read off the SELECTION BOX, one choice at a time, rather than out of an opened dropdown:
            //   the popup lives in its own visual root, so the items are not descendants of this window and
            //   sweeping for them silently yields an empty list — a test that would pass on any label at
            //   all. The selection box renders through the same `ItemTemplate`, so a template that stopped
            //   matching still surfaces here as the record's ToString().
            var rendered = shell.IssueReasonChoices.Select(choice =>
            {
                shell.SelectedIssueReason = choice;
                window.UpdateLayout();

                return picker.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text)
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? string.Empty;
            }).ToArray();

            Assert.Equal(shell.IssueReasonChoices.Select(c => c.Label).ToArray(), rendered);

            // …and the words are words, not the persisted vocabulary leaking onto the screen.
            Assert.DoesNotContain("terms-change", rendered);
            Assert.DoesNotContain("reissue-lost", rendered);
        }, default);

    [Fact]
    public Task BeforeTheFirstIssueThePickerStatesTheReasonInsteadOfAskingForIt() =>
        _session.Dispatch(() =>
        {
            // ⭐ D‑2 on screen. A disabled control carrying the single truthful value says "this is what
            //   will be recorded"; an enabled list of four would invite an untruth about an artifact that
            //   does not exist yet.
            using var manager = new ManagerFixture();
            var (window, _) = Show(manager);

            var picker = Picker(window);

            Assert.False(picker.IsEffectivelyEnabled);
            Assert.Single(picker.Items);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task ChoosingReissueLostSteersTheOperatorToTheExportInstead(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (window, shell) = ShowIssued(manager);

            var advice = Advice(window);
            Assert.False(advice.IsEffectivelyVisible);

            Choose(shell, IssueReasons.ReissueLost);
            window.UpdateLayout();

            Assert.True(advice.IsEffectivelyVisible);

            // ⭐ D‑6. The advice has to NAME the cheaper action, or it is only a scolding. "Export this
            //   issue…" is the button that re-sends the delivered file without signing a new one.
            var words = advice.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .Aggregate(string.Empty, (all, next) => all + next);

            Assert.Contains("Export this issue", words, StringComparison.Ordinal);
        }, default);

    [Fact]
    public Task TheSteerIsAdviceAndNeverBlocksTheOperatorWhoMeantIt() =>
        _session.Dispatch(() =>
        {
            // ⛔ D‑6 recommends; it does not overrule. An operator who has read the advice and still needs
            //   a freshly signed replacement is making a decision, and nothing here may take it from them.
            using var manager = new ManagerFixture();
            var (window, shell) = ShowIssued(manager);

            Choose(shell, IssueReasons.ReissueLost);
            window.UpdateLayout();

            Assert.True(IssueButton(window).IsEffectivelyEnabled);

            manager.Now = manager.Now.AddMinutes(1);
            shell.IssueAndSaveCommand.Execute(null);

            var artifacts = manager.Register.GetArtifacts(shell.LicenseId);
            Assert.Equal(2, artifacts.Count);
            Assert.Equal(IssueReasons.ReissueLost, artifacts[0].Reason);
        }, default);

    [Fact]
    public Task TheReasonAppearsInWordsOnTheHistoryRow() =>
        _session.Dispatch(() =>
        {
            // ⚠ Asserted on the realised ROW, not on the view model's string. The list is virtualised, and
            //   #377 records that counts and extents can look right while no container was ever built.
            using var manager = new ManagerFixture();
            var (window, _) = ShowIssued(manager);

            var history = window.GetVisualDescendants().OfType<ListBox>()
                .First(l => l.Name == "ArtifactHistory");

            var texts = history.GetVisualDescendants().OfType<ListBoxItem>()
                .SelectMany(i => i.GetVisualDescendants().OfType<TextBlock>())
                .Select(t => t.Text ?? string.Empty)
                .ToArray();

            Assert.NotEmpty(texts);
            Assert.Contains("Initial issue", texts);
            Assert.DoesNotContain("initial", texts);
        }, default);

    [Fact]
    public Task SeatsTakesItsWidthFromItsColumnAndCarriesNoNumberOfItsOwn() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ D‑8. The literal Width="80" is gone, and it was NOT replaced with another number:
            //     Tokens.axaml has no width role for a small numeric input, and one was not invented for a
            //     single field. A control's size comes from its CONTEXT (CLAUDE.md UI rule 10), so the
            //     three form fields now share equal columns.
            using var manager = new ManagerFixture();
            var (window, _) = Show(manager);

            var seats = Seats(window);
            var pickers = FormPickers(window);

            Assert.True(double.IsNaN(seats.Width), "Seats must carry no explicit Width.");
            Assert.Equal(2, pickers.Count);

            // ⚠ Within a pixel, not to three decimals: three star columns split the row's remaining width
            //   and the leftover lands on one of them (measured: 246 against 247). The claim being made is
            //   "they share the row equally", and a rounding remainder is not a counter-example — while a
            //   control that kept its own 80 would miss by 160.
            foreach (var picker in pickers)
            {
                Assert.True(
                    Math.Abs(seats.Bounds.Width - picker.Bounds.Width) <= 1.5,
                    $"Seats {seats.Bounds.Width} against picker {picker.Bounds.Width}.");
            }

            // ⚠ And it is a real width, not zero — an Auto column with a stretching child would satisfy
            //   "equal to its neighbours" by collapsing all three.
            Assert.True(seats.Bounds.Width > 40, $"Seats collapsed to {seats.Bounds.Width}.");
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ComboBox Picker(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "IssueReason");

    private static Border Advice(Window window) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ReissueLostAdvice");

    private static Button IssueButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "Issue and save…");

    private static TextBox Seats(Window window)
    {
        var caption = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Seats");

        return caption.GetVisualAncestors().OfType<Panel>().First()
            .GetVisualDescendants().OfType<TextBox>().First();
    }

    private static void Choose(ShellViewModel shell, string reason) =>
        shell.SelectedIssueReason = shell.IssueReasonChoices.First(c => c.Value == reason);

    private static (MainWindow Window, ShellViewModel Shell) Show(ManagerFixture manager)
    {
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);

        var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.SelectedCustomer = shell.Customers.First();
        shell.SelectedLicense = shell.Licenses.First();
        window.UpdateLayout();
        return (window, shell);
    }

    private static (MainWindow Window, ShellViewModel Shell) ShowIssued(ManagerFixture manager)
    {
        var (window, shell) = Show(manager);
        shell.IssueAndSaveCommand.Execute(null);
        window.UpdateLayout();
        return (window, shell);
    }
}
