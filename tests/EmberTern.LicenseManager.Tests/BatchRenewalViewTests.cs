using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;
using static EmberTern.LicenseManager.Tests.ViewProbe;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The batch renewal card and the multiple selection it reads, realised.
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
public sealed class BatchRenewalViewTests
{
    /// <summary>The action ladder — <c>Size.ControlProminent</c>.</summary>
    private const double Action = 28;

    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public BatchRenewalViewTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheBatchCardBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 2);

            var card = Named<Border>(window, "BatchRenewalCard");
            Assert.True(card.IsEffectivelyVisible);
            Assert.True(card.Bounds.Height > 0);

            // ⚠ The preview and the blocker box start hidden — nothing is ticked and no date is chosen,
            //   so a card that showed either would be describing an operation nobody asked for.
            Assert.False(Named<ListBox>(window, "BatchPreview").IsEffectivelyVisible);
            Assert.False(Named<Border>(window, "BatchBlockerBox").IsEffectivelyVisible);
            Assert.False(Named<Button>(window, "ExtendSelected").IsEffectivelyEnabled);
        }, default);

    [Fact]
    public Task TheREALCheckboxesTickRowsAndTheViewModelSeesEveryOne() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE BINDING ITSELF IS THE CLAIM. Everything else in this stage is tested at the view
            //    model, which would pass just as happily if the checkbox column were not bound at all and
            //    the operator could never tick anything. This drives the realised controls.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 3);

            var ticks = AllNamed<CheckBox>(window, "RowTick");
            Assert.Equal(3, ticks.Count);
            Assert.All(ticks, tick => Assert.False(tick.IsChecked));

            foreach (var tick in ticks)
            {
                tick.IsChecked = true;
            }

            window.UpdateLayout();

            Assert.Equal(3, shell.Browser.CheckedCount);
            Assert.Equal(3, shell.Browser.CheckedIds.Count);

            // ⭐ And back the other way: the commands drive the same boxes, so "Select all shown" and a
            //   hand-ticked row cannot disagree about what is ticked.
            shell.Browser.ClearChecksCommand.Execute(null);
            window.UpdateLayout();
            Assert.All(AllNamed<CheckBox>(window, "RowTick"), tick => Assert.False(tick.IsChecked));
        }, default);

    [Fact]
    public Task AnOrdinaryROWSELECTIONLeavesEveryTickWhereItWas() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE REASON THE COLUMN EXISTS. Row selection changes on every click; a batch tick must
            //    survive one. Were the two bound to the same thing — a list in `SelectionMode="Multiple"`
            //    writing into the tick set — a plain click would mean "select ONLY this one" and nineteen
            //    decisions would vanish with no warning and no undo.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 3);

            shell.Browser.CheckAllShownCommand.Execute(null);
            window.UpdateLayout();
            Assert.Equal(3, shell.Browser.CheckedCount);

            // Exactly what a click on row two does to the list.
            // ⚠ A `DataGrid` since 2026-08-18 — the licences list is now EmberTern's grid. The claim is
            //   unchanged and so is the mechanism: `SelectionMode="Single"`, and the tick lives on the row.
            var list = Named<DataGrid>(window, "LicenceResults");
            list.SelectedItem = shell.Browser.Results[1];
            window.UpdateLayout();

            Assert.Equal(3, shell.Browser.CheckedCount);
            Assert.All(AllNamed<CheckBox>(window, "RowTick"), tick => Assert.True(tick.IsChecked));

            // ⚠ And the selection still does its own job — the detail strip follows the click.
            Assert.Equal(shell.Browser.Results[1].Summary.License.LicenseId, shell.Browser.SelectedLicenseId);
        }, default);

    [Fact]
    public Task EveryCONTROLInTheExtendRowStandsAtOneHeight() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ Measured before the repair: picker 24, note 24, three buttons 28 — one row wearing two
            //    ladders, which is what the QA report showed. `Tokens.axaml` separates them by CONTEXT:
            //    `Size.Control` (24) is a control standing in a SERIES, `Size.ControlProminent` (28) one
            //    standing ALONE as a deliberate target — and its own comment records a button and a TEXT
            //    FIELD as the two consumers known when it was added. This row is the second kind.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 2);

            var row = Named<Border>(window, "BatchRenewalCard").GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c is CalendarDatePicker or Button or TextBox && c.TemplatedParent is null)
                .ToList();

            Assert.Equal(5, row.Count);
            foreach (var control in row)
            {
                Assert.Equal(Action, control.Bounds.Height, precision: 3);
            }

            // ⛔ That the licence FORM's two pickers are NOT dragged along is proved where they are
            //    actually showing — `LicenseControlSizeTests` measures them at 24 in the customers view,
            //    and a style that stopped being scoped to this row turns that test red. ⚠ It cannot be
            //    asserted HERE: this window is on the licences view, so those two are unarranged and
            //    measure zero, which would pass or fail for reasons that have nothing to do with the rule.
        }, default);

    [Fact]
    public Task ThePreviewNamesEveryTickedLicenceAndTheActionBecomesAvailable() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 2);

            shell.Browser.CheckAllShownCommand.Execute(null);
            shell.BatchRenewal.TargetDate = new DateTime(2029, 6, 30);
            window.UpdateLayout();

            var preview = Named<ListBox>(window, "BatchPreview");
            Assert.True(preview.IsEffectivelyVisible);

            // ⚠ Read off the REALISED rows, not off the source collection — a template that stopped
            //   matching would leave the source intact and render type names (gotcha #370). Two rows, so
            //   virtualisation realises both (gotcha #377).
            var rendered = VisibleText(preview);

            Assert.Equal(2, preview.GetRealizedContainers().Count());
            Assert.All(
                shell.BatchRenewal.Rows,
                row => Assert.Contains(row.Change, rendered));
            Assert.Contains("Initial issue", rendered);

            Assert.True(Named<Button>(window, "ExtendSelected").IsEffectivelyEnabled);
            Assert.Contains("2 licences would be extended to 2029-06-30", Text(window, "BatchPreviewSummary"));
        }, default);

    [Fact]
    public Task OneBlockedLicenceDisablesTheACTIONAndNamesTheReasonOnTheRow() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ D‑3 on the realised control. The button being disabled is the whole guarantee that no
            //    partial batch can be started, and the row's sentence is the only thing telling the
            //    operator which of twenty licences to deal with.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);
            manager.Register.SaveLicense(
                manager.SaveLicense(customer) with { ExpiresAt = LicenseDay.EndOf(new DateTime(2035, 1, 1)) });

            var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
            var window = Open(shell);

            shell.Browser.CheckAllShownCommand.Execute(null);
            shell.BatchRenewal.TargetDate = new DateTime(2029, 6, 30);
            window.UpdateLayout();

            Assert.False(Named<Button>(window, "ExtendSelected").IsEffectivelyEnabled);

            var blockerBox = Named<Border>(window, "BatchBlockerBox");
            Assert.True(blockerBox.IsEffectivelyVisible);
            Assert.Contains("whole operation is held", Text(window, "BatchBlockerSummary"));

            var rendered = VisibleText(Named<ListBox>(window, "BatchPreview"));

            Assert.Contains(rendered, text => text.Contains("would not extend it", StringComparison.Ordinal));
        }, default);

    [Fact]
    public Task TheRESULTStaysOnScreenAfterTheBatchHasRun() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var (shell, window) = Show(manager, licences: 2);

            shell.Browser.CheckAllShownCommand.Execute(null);
            shell.BatchRenewal.TargetDate = new DateTime(2029, 6, 30);
            shell.BatchRenewal.ExtendCommand.Execute(null);
            window.UpdateLayout();

            var result = Named<TextBlock>(window, "BatchLastResult");
            Assert.True(result.IsEffectivelyVisible);
            Assert.Contains("2 licences extended to 2029-06-30", result.Text);

            // ⭐ And the operation is finished rather than merely reported: the ticks are gone and the
            //   action is unavailable again, so a second press cannot repeat a batch by accident.
            Assert.False(Named<Button>(window, "ExtendSelected").IsEffectivelyEnabled);
            Assert.False(Named<ListBox>(window, "BatchPreview").IsEffectivelyVisible);
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static (ShellViewModel Shell, MainWindow Window) Show(ManagerFixture manager, int licences)
    {
        var customer = manager.SaveCustomer();
        for (var i = 0; i < licences; i++)
        {
            manager.SaveLicense(customer);
        }

        var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
        return (shell, Open(shell));
    }

    private static MainWindow Open(ShellViewModel shell)
    {
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.ShowLicensesCommand.Execute(null);
        window.UpdateLayout();
        return window;
    }

    private static string Text(Window window, string name) =>
        Named<TextBlock>(window, name).Text ?? string.Empty;

    /// <summary>
    /// The text a list actually SHOWS — realised containers, and only the parts of them the operator
    /// can see.
    ///
    /// <para>⚠⚠ <b>The visibility filter is the whole point, and it was added because the guard was
    /// PROVED VACUOUS without it.</b> Hiding the blocker line with <c>IsVisible="False"</c> was injected
    /// as a defect and the test stayed GREEN: an invisible <c>TextBlock</c> is still in the visual tree
    /// with its <c>Text</c> bound, so a sweep of realised descendants reads a sentence nobody on the
    /// other side of the screen can read (#378). ⛔ Never assert "the text is present" over a tree
    /// without asking whether it is showing.</para>
    /// </summary>
    private static List<string> VisibleText(ListBox list) =>
        list.GetRealizedContainers()
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Height > 0)
            .Select(t => t.Text ?? string.Empty)
            .ToList();
}
