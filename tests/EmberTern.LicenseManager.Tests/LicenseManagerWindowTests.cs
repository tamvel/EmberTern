using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// One headless Avalonia session for this whole test assembly.
///
/// <para>⚠ <b>Same one-session-per-process rule as EmberTern's suite (gotchas #94 / #226 / #286), and it
/// applies here for the same reason even though this is a different process:</b> a session owns a UI
/// thread, and a second session in one process makes every later test's controls belong to a thread the
/// first session's static state does not. ⛔ Any further headless test class in this assembly joins
/// <see cref="ManagerHeadlessCollection"/> — it never adds its own <c>IClassFixture</c>.</para>
///
/// <para>⛔ Do NOT add a constructor that "warms the session up". It was tried in EmberTern, measured, and
/// reverted: it does not touch the upstream race, it only moves it into fixture construction, where a
/// throw fails every test in the collection instead of one.</para>
/// </summary>
public sealed class ManagerHeadlessSessionFixture : IDisposable
{
    /// <summary>The shared session.</summary>
    public HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessAppEntry));

    /// <inheritdoc />
    public void Dispose() => Session.Dispose();

    private static class HeadlessAppEntry
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<global::EmberTern.LicenseManager.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

/// <summary>The one collection every headless test class in this assembly belongs to.</summary>
[CollectionDefinition(Name)]
public sealed class ManagerHeadlessCollection : ICollectionFixture<ManagerHeadlessSessionFixture>
{
    /// <summary>Its name.</summary>
    public const string Name = "headless-license-manager";
}

/// <summary>
/// ⭐ <b>The checklist items a static scan cannot reach: the windows actually build, they build in BOTH
/// themes, and the realised controls carry the metrics and brushes the styles intend.</b>
///
/// <para>⚠⚠ <b>EVERY TEST HERE RETURNS ITS <c>Task</c>, AND THAT IS LOAD-BEARING RATHER THAN STYLISTIC.</b>
/// <c>HeadlessUnitTestSession.Dispatch</c> returns a <c>Task</c> and runs the body on the session's own UI
/// thread. Written as <c>public void X() =&gt; _session.Dispatch(…)</c> — which compiles, because a method
/// call is a statement expression — the <c>Task</c> is DISCARDED, xUnit never awaits it, and no assertion
/// inside the lambda can ever fail the test. L3 shipped all five of these tests in that shape, so all five
/// were green and none of them was evidence. ⭐ It was caught by injecting <c>Assert.Fail</c> into a
/// headless body and watching the run report success. ⛔ Never write one of these as <c>void</c>.</para>
///
/// <para>A theme token that resolves in Dark and not in Light produces no exception and no warning — the
/// property silently keeps its default. So the assertion that matters is not "it did not throw", it is
/// <see cref="TheSameBrushResolvesDifferentlyInEachTheme"/>: proof that the palette is actually switching
/// rather than that both themes happen to fall back to the same default.</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenseManagerWindowTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenseManagerWindowTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheUnlockWindowBuildsInBothThemes(string theme) =>
_session.Dispatch(() =>
    {
        HeadlessTheme.UseTheme(theme);

        using var manager = new ManagerFixture();
        var window = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
        window.Show();

        Assert.NotNull(window.Content);
        Assert.Equal("EmberTern License Manager", window.Title);
    }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheMainWindowBuildsInBothThemes(string theme) =>
_session.Dispatch(() =>
    {
        HeadlessTheme.UseTheme(theme);

        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);

        var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        Assert.NotNull(window.Content);
        Assert.Single(shell.Customers);
        Assert.Equal("R1", shell.SigningKeyId);
    }, default);

    [Fact]
    public Task TheSameBrushResolvesDifferentlyInEachTheme() =>
_session.Dispatch(() =>
    {
        // ⭐⭐ THE test that makes "renders correctly in both themes" mean something. If the linked
        //     Colors.axaml were not being found, both lookups would return the same fallback and every
        //     other test in this file would still pass.
        HeadlessTheme.UseTheme("Dark");
        var dark = HeadlessTheme.Brush("BackgroundBrush");

        HeadlessTheme.UseTheme("Light");
        var light = HeadlessTheme.Brush("BackgroundBrush");

        Assert.NotNull(dark);
        Assert.NotNull(light);
        Assert.NotEqual(dark!.Color, light!.Color);
    }, default);

    [Fact]
    public Task TheWindowChromeIsPaintedFromTokensRatherThanDefaults() =>
_session.Dispatch(() =>
    {
        HeadlessTheme.UseTheme("Dark");

        using var manager = new ManagerFixture();
        var window = new MainWindow
        {
            DataContext = new ShellViewModel(manager.Register, manager.Session, manager.Paths),
        };
        window.Show();

        Assert.Equal(HeadlessTheme.Brush("BackgroundBrush")!.Color, ((ISolidColorBrush)window.Background!).Color);
        Assert.Equal(HeadlessTheme.Brush("ForegroundBrush")!.Color, ((ISolidColorBrush)window.Foreground!).Color);
    }, default);

    [Fact]
    public Task IssuingThroughTheViewModelProducesAVerifiableArtifact() =>
_session.Dispatch(() =>
    {
        // The whole L3 loop driven the way a person drives it: pick a customer, save terms, issue.
        using var manager = new ManagerFixture();
        var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);

        shell.NewCustomerCommand.Execute(null);
        shell.CustomerName = "ACME Sp. z o.o.";
        shell.SaveCustomerCommand.Execute(null);

        shell.NewLicenseCommand.Execute(null);
        shell.LicenseSeats = 3;
        shell.SaveLicenseCommand.Execute(null);

        shell.IssueAndSaveCommand.Execute(null);

        var licence = Assert.Single(manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));
        var artifact = Assert.Single(manager.Register.GetArtifacts(licence.LicenseId));

        var verdict = manager.Workflow.Inspect(manager.Session, artifact);
        Assert.Equal(EmberTern.Licensing.LicenseStatus.Valid, verdict.Status);
        Assert.Equal("ACME Sp. z o.o.", verdict.Payload!.Licensee);
        Assert.Equal(3, verdict.Payload.Seats);
        Assert.True(shell.IsSuccess, shell.MessageText);
    }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task ThePrimaryActionLabelReadsOnTheAccentFillInBothThemes(string theme) =>
_session.Dispatch(() =>
        {
            // The property that matters, asserted on the REALISED brush of the window as it ships. ⚠ It
            // does NOT distinguish which mechanism delivers it — see the test below for that.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var window = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
            window.Show();

            Assert.Equal(
                HeadlessTheme.Brush("OnAccentBrush")!.Color,
                ((ISolidColorBrush)PrimaryActionLabel(window).Foreground!).Color);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task AnExplicitLabelInsideAPrimaryActionAlsoReadsOnTheAccentFill(string theme) =>
_session.Dispatch(() =>
        {
            // ⭐⭐ THE CASE THE `Button.primary TextBlock` STYLE ACTUALLY PROTECTS, written after the
            //    obvious version of this test was verified and found to prove NOTHING: removing that
            //    style left the window's own buttons correct, because a string `Content` gets its
            //    Foreground as a LOCAL VALUE from the ContentPresenter, and a local value outranks every
            //    style setter.
            //
            // ⭐ An EXPLICIT `<TextBlock>` child has no such local value, so the implicit TextBlock style
            //    — which paints ForegroundBrush — wins and puts a dark label on the accent fill. That is
            //    the shape every primary button in EmberTern takes (icon + label + shortcut chip), so it
            //    is the shape the first icon-bearing button here will take too.
            //
            // ⚠ Verified RED with the style removed, in both themes, before being accepted green.
            HeadlessTheme.UseTheme(theme);

            var button = new Button { Classes = { "primary" }, Content = new TextBlock { Text = "Issue" } };
            var window = new Window { Content = button };
            window.Show();

            var label = button.GetVisualDescendants().OfType<TextBlock>().First();

            Assert.Equal(
                HeadlessTheme.Brush("OnAccentBrush")!.Color,
                ((ISolidColorBrush)label.Foreground!).Color);
        }, default);

    [Fact]
    public Task TheDialogActionStandsOnTheActionHeightNotTheFieldHeight() =>
_session.Dispatch(() =>
    {
        // ⭐ Tokens.axaml keeps fields and actions on two independent ladders on purpose: a field stands
        //    in a SERIES (alignment decides), an action stands ALONE and is aimed at. The realised
        //    heights must therefore DIFFER — and the action must be the taller of the two.
        HeadlessTheme.UseTheme("Dark");

        using var manager = new ManagerFixture();
        var window = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
        window.Show();

        var action = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
        var field = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);

        Assert.True(action.MinHeight > field.MinHeight,
            $"The dialog action is {action.MinHeight} px against a {field.MinHeight} px field. An action "
            + "on the field height reads as one more row of the form rather than as its conclusion.");
    }, default);

    [Fact]
    public Task ACaptionSitsCloserToItsOwnFieldThanTwoFieldsSitToEachOther() =>
_session.Dispatch(() =>
    {
        // ⭐⭐ THE PROXIMITY RULE, AS A MEASUREMENT. Tokens.axaml states it where `Margin.LabelGap` is
        //    defined: the caption→field gap must be SMALLER than the field→field gap, because if they are
        //    equal or inverted the eye attaches the caption to the field ABOVE it. L3 put a uniform
        //    StackPanel `Spacing` on the form, which cannot express the inequality at all — it makes both
        //    gaps the same by construction.
        //
        // ⚠ Measured on realised controls, so a `Spacing` reintroduced on any ancestor breaks it: spacing
        //    is added BETWEEN children, on top of these margins, which is exactly how the inequality was
        //    lost the first time.
        HeadlessTheme.UseTheme("Dark");

        using var manager = new ManagerFixture();
        var window = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
        window.Show();

        var caption = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("field-label"));
        var field = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);

        Assert.True(caption.Margin.Bottom < field.Margin.Bottom,
            $"A caption is {caption.Margin.Bottom} px from its field while two fields are "
            + $"{field.Margin.Bottom} px apart. Equal or inverted, the caption reads as belonging to the "
            + "field above it.");

        foreach (var stack in window.GetVisualDescendants().OfType<StackPanel>()
                     .Where(s => s.Orientation == Orientation.Vertical))
        {
            Assert.Equal(0d, stack.Spacing);
        }
    }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task EveryWindowCarriesTheEmberTernIcon(string theme) =>
_session.Dispatch(() =>
    {
        // ⭐ From ONE style setter, so no window can be the one that forgot. ⚠ An .ico carries its own
        //    artwork and is not repainted by the palette — asserting it in both themes records that as a
        //    fact rather than leaving the next reader to wonder whether it needs a per-theme variant.
        HeadlessTheme.UseTheme(theme);

        using var manager = new ManagerFixture();

        var unlock = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
        unlock.Show();

        var shell = new MainWindow
        {
            DataContext = new ShellViewModel(manager.Register, manager.Session, manager.Paths),
        };
        shell.Show();

        Assert.NotNull(unlock.Icon);
        Assert.NotNull(shell.Icon);
    }, default);

    [Fact]
    public Task EveryFieldCentresItsContentVertically() => _session.Dispatch(() =>
    {
        // ⭐⭐ THE DEFECT THE USER SAW IN THE RUNNING APPLICATION AND NO TEST HAD ASKED ABOUT: text and
        //    password dots pinned to the TOP of every field. `Pad.Control` is `8,0` on purpose — the
        //    height belongs to `Size.Control`, and one thing must own a size — but zero vertical padding
        //    only CENTRES if `VerticalContentAlignment` says so, and the framework default is `Stretch`.
        //
        // ⚠ Asserted as a MEASUREMENT of the realised presenter, not as "the setter is present". The
        //    setter being written down is what was true of `Pad.Control` too, and the field still looked
        //    wrong; the property that matters is where the glyphs actually land.
        HeadlessTheme.UseTheme("Dark");

        // ⚠ BOTH branches, and the counts are how the test proves it looked at the right one. A
        //   ManagerFixture performs a REAL ceremony in its constructor, so its paths always HAVE a
        //   keystore and its window is the one-field UNLOCK screen. First run — the screen the defect
        //   was reported on — needs paths with no keystore behind them.
        using var manager = new ManagerFixture();
        var firstRun = new ManagerPaths(
            Path.Combine(Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N")));

        var fields = new List<TextBox>();

        foreach (var (paths, expected) in new[] { (firstRun, 3), (manager.Paths, 1) })
        {
            var window = new UnlockWindow { DataContext = new UnlockViewModel(paths) };
            window.Show();
            window.UpdateLayout();

            var visible = window.GetVisualDescendants().OfType<TextBox>()
                .Where(t => t.IsEffectivelyVisible).ToList();

            Assert.Equal(expected, visible.Count);
            fields.AddRange(visible);
        }

        foreach (var field in fields)
        {
            var presenter = field.GetVisualDescendants().OfType<TextPresenter>().First();
            var origin = ((Visual)presenter).TranslatePoint(default, field);

            Assert.NotNull(origin);

            var above = origin!.Value.Y;
            var below = field.Bounds.Height - (origin.Value.Y + presenter.Bounds.Height);
            var slack = presenter.Bounds.Height - (presenter.TextLayout?.Height ?? 0);

            // ⚠⚠ THE FIRST VERSION OF THIS TEST MEASURED THE WRONG BOX AND PASSED WITH THE DEFECT IN
            //    PLACE. Under `Stretch` the presenter FILLS the field — measured 22 px inside a 24 px
            //    field, origin at 1 — so "is the presenter centred?" answered yes while the glyphs sat
            //    against its top edge. The presenter has to be sized to its TEXT before its position
            //    means anything: measured 15 px against a 14.17 px layout once the setter is present,
            //    versus 22 px against the same 14.17 px without it.
            Assert.True(slack <= 1.5,
                $"A field's text presenter is {presenter.Bounds.Height:0.##} px tall around "
                + $"{presenter.TextLayout?.Height:0.##} px of text — it is being STRETCHED to the field "
                + "instead of sized to its content, which puts the glyphs on the top edge. The base "
                + "TextBox style needs VerticalContentAlignment, because `Pad.Control` carries no "
                + "vertical padding by design.");

            // Tolerance of 1.5, not 0: 14.17 px of text inside 22 px of inner height cannot split
            // evenly, and the arrange rounds. Measured 5 above / 4 below.
            Assert.True(Math.Abs(above - below) <= 1.5,
                $"A field's content sits {above:0.##} px from the top and {below:0.##} px from the "
                + $"bottom of a {field.Bounds.Height:0.##} px field.");
        }
    }, default);

    [Fact]
    public Task APasswordFieldIsAlignedLikeAnyOtherFieldAndAMultilineOneStartsAtTheTop() =>
        _session.Dispatch(() =>
        {
            // ⭐ Same rule for a password as for plain text — the user asked for exactly that, and it is
            //   what falls out of putting the decision on the BASE style rather than on the fields.
            // ⭐ …and the one deliberate exception: a box of fifteen lines starts at the top, because
            //   centring hangs a short note in the middle of the frame. EmberTern added that setter after
            //   a user reported it at the M2c acceptance; inheriting the centring without it would have
            //   reintroduced the same defect in the main window's three multi-line fields.
            HeadlessTheme.UseTheme("Dark");

            var plain = new TextBox();
            var password = new TextBox { PasswordChar = '•' };
            var multiline = new TextBox { Classes = { "multiline" } };
            var window = new Window
            {
                Content = new StackPanel { Children = { plain, password, multiline } },
            };
            window.Show();
            window.UpdateLayout();

            Assert.Equal(VerticalAlignment.Center, plain.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, password.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Top, multiline.VerticalContentAlignment);
        }, default);

    private static TextBlock PrimaryActionLabel(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("primary"))
            .SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>())
            .First();

}
