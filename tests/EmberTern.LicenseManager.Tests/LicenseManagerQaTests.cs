using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The L5.1 QA pass, realised — the six points the user raised after looking at the running application.
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenseManagerQaTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenseManagerQaTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    // ── QA-1 · the top bar belongs to EmberTern ─────────────────────────────────────────────────────

    [Fact]
    public Task TheThemeToggleIsAnIconAndItShowsTheActionRatherThanTheState() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ The glyphs are EmberTern's OWN, reached through the linked IconGeometries.axaml — the
            //     whole point of splitting that dictionary. Asserted as the SAME INSTANCE as the resource,
            //     so a copied-in path could not pass.
            using var manager = new ManagerFixture();
            var window = Show(manager);

            var glyph = Glyph(window, "ThemeToggle");

            HeadlessTheme.UseTheme("Dark");
            window.UpdateLayout();
            Assert.Same(Geometry("Icon.Sun"), glyph.Data);

            HeadlessTheme.UseTheme("Light");
            window.UpdateLayout();
            Assert.Same(Geometry("Icon.Moon"), glyph.Data);
        }, default);

    [Fact]
    public Task TheThemeToggleCarriesNoTextLabelAnyMore() =>
        _session.Dispatch(() =>
        {
            // ⚠ The literal complaint: a text "Light / Dark" button where EmberTern has an icon.
            using var manager = new ManagerFixture();
            var window = Show(manager);

            var toggle = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ThemeToggle");

            Assert.Contains("icon", toggle.Classes);
            Assert.DoesNotContain("flat", toggle.Classes);
            Assert.False(toggle.Content is string);
        }, default);

    [Fact]
    public Task TheSigningKeyReadsAsProvenanceRatherThanAsAControl() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager);

            var key = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text?.StartsWith("Signing key:", StringComparison.Ordinal) == true);

            Assert.Contains("hint", key.Classes);
            Assert.Equal(
                HeadlessTheme.Brush("SubtleForegroundBrush")!.Color,
                ((ISolidColorBrush)key.Foreground!).Color);
        }, default);

    // ── QA follow-up 1 · the application draws its own window ───────────────────────────────────────

    [Fact]
    public Task TheWindowDrawsItsOwnChromeInsteadOfLettingWindowsAddASecondTitleBar() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ The reported defect: Windows drew a title bar saying "EmberTern License Manager" and the
            //     application drew a second bar saying the same thing directly beneath it — two bars that
            //     read as two different programs. EmberTern has extended its client area since M3.1; this
            //     is the same three properties.
            using var manager = new ManagerFixture();
            var window = Show(manager);

            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(-1, window.ExtendClientAreaTitleBarHeightHint);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheThreeCaptionButtonsExistAndWearEmberTernsOwnGlyphs(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var window = Show(manager);

            foreach (var (name, key) in new[]
                     {
                         ("MinimizeButton", "Icon.WindowMinimize"),
                         ("MaxRestoreButton", "Icon.WindowMaximize"),
                         ("CloseButton", "Icon.WindowClose"),
                     })
            {
                var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);
                Assert.Contains("caption", button.Classes);
                Assert.Same(Geometry(key), Glyph(window, name).Data);
            }

            // ⚠ Only the close button carries the Windows convention colour, and it must be the ONLY one.
            var close = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CloseButton");
            Assert.Contains("close", close.Classes);
            Assert.DoesNotContain(
                "close",
                window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "MinimizeButton").Classes);
        }, default);

    [Fact]
    public Task TheMaximiseGlyphShowsWhatTheClickWillDo() =>
        _session.Dispatch(() =>
        {
            // ⭐ Same rule as the theme toggle: the icon shows the ACTION, not the state. Driven by the
            //   window's own WindowState, so it is right however the state changed — including a Windows
            //   snap gesture the application never saw as a click.
            using var manager = new ManagerFixture();
            var window = Show(manager);

            Assert.Same(Geometry("Icon.WindowMaximize"), Glyph(window, "MaxRestoreButton").Data);

            window.WindowState = WindowState.Maximized;
            window.UpdateLayout();
            Assert.Same(Geometry("Icon.WindowRestore"), Glyph(window, "MaxRestoreButton").Data);

            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
            Assert.Same(Geometry("Icon.WindowMaximize"), Glyph(window, "MaxRestoreButton").Data);
        }, default);

    // ── QA-2 · a generated value does not look like a field ─────────────────────────────────────────

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task GeneratedIdentifiersAreValuesWithACopyActionNotDisabledFields(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐ The customer identifier and the licence id are both generated. A read-only TextBox states
            //   that by refusing input, which is the one way of saying it that still invites the input.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer("ACME");
            manager.SaveLicense(customer);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);
            shell.SelectedCustomer = shell.Customers[0];
            shell.SelectedLicense = shell.Licenses[0];
            window.UpdateLayout();

            var values = window.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Where(v => v.Classes.Contains("value"))
                .Select(v => v.Text)
                .ToArray();

            Assert.Contains(customer.CustomerId, values);
            Assert.Contains(shell.LicenseId, values);

            // ⛔ And no read-only TextBox is left carrying either of them.
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsReadOnly),
                t => t.Text == customer.CustomerId || t.Text == shell.LicenseId);

            // Each value has a copy action beside it, wearing EmberTern's own copy glyph.
            var copies = window.GetVisualDescendants().OfType<Path>()
                .Count(p => ReferenceEquals(p.Data, Geometry("Icon.Copy")));
            Assert.Equal(2, copies);
        }, default);

    // ── QA-3 · the rail can be resized ──────────────────────────────────────────────────────────────

    [Fact]
    public Task TheCustomerRailIsResizableWithinBoundsThatKeepBothPanesUsable() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager);

            var splitter = Assert.Single(window.GetVisualDescendants().OfType<GridSplitter>());
            var grid = splitter.GetVisualAncestors().OfType<Grid>()
                .First(g => g.ColumnDefinitions.Count == 3);

            var rail = grid.ColumnDefinitions[0];

            Assert.Equal(200, rail.MinWidth);
            Assert.Equal(480, rail.MaxWidth);
            Assert.True(rail.Width.Value >= rail.MinWidth && rail.Width.Value <= rail.MaxWidth,
                "Domyślna szerokość railu leży poza własnymi granicami.");
        }, default);

    // ── QA-4 · the message strip ────────────────────────────────────────────────────────────────────

    [Fact]
    public Task TheMessageStripLeavesNoEmptyBandBeneathIt() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE measured cause of the reported empty band: `Margin.SectionGap` is `0,0,0,16` — a
            //     BOTTOM margin — on a strip docked to the bottom edge. Sixteen pixels of window
            //     background under the message, meaning nothing.
            using var manager = new ManagerFixture();
            var window = Show(manager);

            var strip = MessageStrip(window);

            Assert.Equal(new Thickness(0), strip.Margin);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task EachSeverityPaintsItsOwnStripeInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ The Light-theme complaint, pinned. Severity used to be carried by the BORDER colour
            //     alone, and in Light `PanelBrush` sits so close to `BackgroundBrush` that a warning was a
            //     hairline. Now it is a stripe — and this asserts the stripe's REALISED fill, per severity,
            //     in both themes.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);

            foreach (var (message, key) in new (StatusMessage Message, string Key)[]
                     {
                         (StatusMessage.Error("boom"), "ErrorBrush"),
                         (StatusMessage.Warning("careful"), "WarningBrush"),
                         (StatusMessage.Success("done"), "ConnectedBrush"),
                         (StatusMessage.Info("note"), "AccentMutedBrush"),
                     })
            {
                shell.Message = message;
                window.UpdateLayout();

                var stripe = MessageStrip(window).GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("severity-stripe"));

                Assert.Equal(
                    HeadlessTheme.Brush(key)!.Color,
                    ((ISolidColorBrush)stripe.Background!).Color);
            }
        }, default);

    [Fact]
    public Task TheMessageWearsTheSameGlyphEmberTernUsesForThatSeverity() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);

            foreach (var (message, key) in new (StatusMessage Message, string Key)[]
                     {
                         (StatusMessage.Error("boom"), "Icon.BreakException"),
                         (StatusMessage.Warning("careful"), "Icon.AlertTriangle"),
                         (StatusMessage.Success("done"), "Icon.Check"),
                         (StatusMessage.Info("note"), "Icon.Comment"),
                     })
            {
                shell.Message = message;
                window.UpdateLayout();

                var glyph = MessageStrip(window).GetVisualDescendants().OfType<Path>().First();
                Assert.Same(Geometry(key), glyph.Data);
            }
        }, default);

    // ── QA-5 · the licences list says more ──────────────────────────────────────────────────────────

    [Fact]
    public Task TheListShowsTheContactPersonAndTheRegistersOwnStatus() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();

            var customer = manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = "c-0001", Name = "ACME", FirstName = "Jan", LastName = "Kowalski",
            });
            manager.SaveLicense(customer);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);
            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            var row = Assert.Single(shell.Browser.Results);

            Assert.Equal("Jan Kowalski", row.Contact);
            // ⛔ The REGISTER's value, capitalised — not a verdict this view computed. `LicenseVerifier`
            //    stays on the selection, where Inspect latest runs it for real.
            Assert.Equal("Active", row.Status);
        }, default);

    [Fact]
    public Task ACustomerWithNoContactPersonShowsADashRatherThanABlank() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer("ACME");
            manager.SaveLicense(customer);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);
            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            Assert.Equal("—", Assert.Single(shell.Browser.Results).Contact);
        }, default);

    [Fact]
    public Task TheContactPersonIsSearchable() =>
        _session.Dispatch(() =>
        {
            // ⭐ The point of showing the person: "the licence Kowalski called about" has to be findable
            //   by that name, not only by the company's.
            using var manager = new ManagerFixture();

            var acme = manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = "c-0001", Name = "ACME", LastName = "Kowalski",
            });
            var beta = manager.Register.SaveCustomer(new CustomerRecord
            {
                CustomerId = "c-0002", Name = "Beta", LastName = "Nowak",
            });
            manager.SaveLicense(acme);
            manager.SaveLicense(beta);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);
            shell.ShowLicensesCommand.Execute(null);
            shell.Browser.SearchText = "kowalski";
            window.UpdateLayout();

            Assert.Equal("ACME", Assert.Single(shell.Browser.Results).CustomerName);
        }, default);

    // ── QA-6 · the date pickers are wired ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheTermsAreEnteredThroughDatePickersInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer("ACME");
            var licence = manager.SaveLicense(customer);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);
            shell.SelectedCustomer = shell.Customers[0];
            shell.SelectedLicense = shell.Licenses[0];
            window.UpdateLayout();

            var pickers = window.GetVisualDescendants().OfType<CalendarDatePicker>().ToArray();
            Assert.Equal(2, pickers.Length);

            // ⭐ Bound both ways, and displaying the day the register holds.
            Assert.Equal(licence.NotBefore.UtcDateTime.Date, pickers[0].SelectedDate);
            Assert.Equal(licence.ExpiresAt.UtcDateTime.Date, pickers[1].SelectedDate);

            // ⚠ The CAPTION no longer has to teach a format — that was half of what it used to say.
            //   Scoped to `field-label`, deliberately: the picker's own placeholder still shows the shape
            //   of a date, and that is help offered inside the control rather than a caption spending its
            //   words on syntax. Asserting over every TextBlock in the window conflated the two.
            var captions = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("field-label"))
                .Select(t => t.Text ?? string.Empty)
                .ToArray();

            Assert.NotEmpty(captions);
            Assert.DoesNotContain(captions, c => c.Contains("yyyy-mm-dd", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(captions, c => c == "Valid from");
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static MainWindow Show(ManagerFixture manager)
    {
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);
        return Show(new ShellViewModel(manager.Register, manager.Session, () => manager.Now));
    }

    private static MainWindow Show(ShellViewModel shell)
    {
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Border MessageStrip(Window window) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("message"));

    private static Path Glyph(Window window, string buttonName) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == buttonName)
            .GetVisualDescendants().OfType<Path>()
            .First();

    private static Geometry Geometry(string key)
    {
        var application = Application.Current!;
        Assert.True(
            application.Resources.TryGetResource(key, null, out var value) && value is Geometry,
            $"Geometria {key} nie rozwiązuje się — IconGeometries.axaml nie jest zlinkowany do "
            + "License Managera albo klucz zniknął.");

        return (Geometry)value!;
    }
}
