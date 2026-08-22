using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The selected customer's two pages — <b>Customer</b> and <b>Licences</b>.
///
/// <para>⭐⭐ <b>What this class exists to hold.</b> The detail pane used to be one scrolling column in
/// which contact details ran into licence terms and then into the issuing history, with no boundary the
/// eye could use. The split gives each domain its own surface; these guards pin that the two never mix,
/// that the switch is a real gesture, and that the page an operator is on survives the things that used
/// to reset it.</para>
///
/// <para>⚠⚠ Every assertion about placement reads <c>IsEffectivelyVisible</c>, never <c>IsVisible</c>
/// (gotcha #387): a control inside a collapsed page still reports its own <c>IsVisible</c> as true, so
/// the declared value would say both pages are on screen at once.</para>
///
/// <para>⚠ Every test returns its <c>Task</c> (gotcha #374), and every control is located by NAME or by
/// the page that owns it (gotcha #379).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class CustomerPagesTests
{
    private readonly HeadlessUnitTestSession _session;

    public CustomerPagesTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    /// <summary>⭐ The pane opens on the customer's own details — who they are before what they bought.</summary>
    [Fact]
    public Task TheDetailPaneOpensOnTheCustomerPage() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager, out var shell);

            Assert.True(shell.IsCustomerTab);
            Assert.False(shell.IsLicencesTab);
            Assert.True(Page(window, "CustomerPage").IsEffectivelyVisible);
            Assert.False(Page(window, "LicencesPage").IsEffectivelyVisible);
        }, default);

    /// <summary>
    /// ⭐⭐ The switch is driven by a REAL CLICK on the real button, not by setting the view model. A guard
    /// that only set the property would stay green over a tab that had stopped being wired to anything.
    /// </summary>
    [Fact]
    public Task ClickingTheTabsSwitchesPagesBothWays() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager, out var shell);

            Click(window, ViewProbe.Named<Button>(window, "LicencesTab"));
            window.UpdateLayout();

            Assert.True(shell.IsLicencesTab);
            Assert.True(Page(window, "LicencesPage").IsEffectivelyVisible);
            Assert.False(Page(window, "CustomerPage").IsEffectivelyVisible);

            Click(window, ViewProbe.Named<Button>(window, "CustomerTab"));
            window.UpdateLayout();

            Assert.True(shell.IsCustomerTab);
            Assert.True(Page(window, "CustomerPage").IsEffectivelyVisible);
            Assert.False(Page(window, "LicencesPage").IsEffectivelyVisible);
        }, default);

    /// <summary>⭐ The active tab is marked, so the operator can see which page they are on.</summary>
    [Fact]
    public Task TheActiveTabIsMarkedAndOnlyOneIs() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var window = Show(manager, out _);

            var customer = ViewProbe.Named<Button>(window, "CustomerTab");
            var licences = ViewProbe.Named<Button>(window, "LicencesTab");

            Assert.Contains("active", customer.Classes);
            Assert.DoesNotContain("active", licences.Classes);

            Click(window, licences);
            window.UpdateLayout();

            Assert.DoesNotContain("active", customer.Classes);
            Assert.Contains("active", licences.Classes);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The domains do not mix — the whole point of the split.</b> Each field is asserted on the
    /// page that owns it AND asserted absent from the other, because "it is on the right page" alone
    /// would still hold if it were on both.
    /// </summary>
    [Fact]
    public Task EachDomainLivesOnItsOwnPageAndNowhereElse() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customerRecord = manager.SaveCustomer();
            manager.SaveLicense(customerRecord);
            var window = Show(manager, out var shell);
            shell.SelectedLicense = shell.Licenses.First();
            window.UpdateLayout();

            // Customer's own details.
            ViewProbe.ShowCustomerPage(window, shell);
            AssertOnPage(window, "CustomerPage", "Name — required, and this is what gets signed");
            AssertOnPage(window, "CustomerPage", "Address");
            AssertOnPage(window, "CustomerPage", "E-mail");
            AssertActionOnPage(window, "CustomerPage", "Save customer");

            // The whole licence domain, issuing history included.
            ViewProbe.ShowLicencesPage(window, shell);
            AssertOnPage(window, "LicencesPage", "Seats");
            AssertOnPage(window, "LicencesPage", "Licence id");
            AssertActionOnPage(window, "LicencesPage", "Save terms");
            AssertActionOnPage(window, "LicencesPage", "Issue and save…");
            AssertActionOnPage(window, "LicencesPage", "Inspect latest");
            AssertActionOnPage(window, "LicencesPage", "Export latest…");
            AssertActionOnPage(window, "LicencesPage", "New licence");
        }, default);

    /// <summary>
    /// ⭐ The issuing history is part of Licences, ⛔ NOT a third page: it describes what was issued for
    /// the licence selected right there, and separating them would put a question and its own answer on
    /// two different screens.
    /// </summary>
    [Fact]
    public Task TheIssuingHistoryBelongsToTheLicencesPage() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            var licence = manager.SaveLicense(customer);
            manager.Workflow.Issue(manager.Session, licence, customer, "initial");

            var window = Show(manager, out var shell);
            shell.SelectedLicense = shell.Licenses.First();
            ViewProbe.ShowLicencesPage(window, shell);

            var history = ViewProbe.Named<ListBox>(window, "ArtifactHistory");
            Assert.True(history.IsEffectivelyVisible);
            Assert.Contains(Page(window, "LicencesPage"), history.GetVisualAncestors());

            // ⛔ And there is no third tab.
            var tabs = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("view-tab"))
                .Where(b => b.Name is "CustomerTab" or "LicencesTab")
                .ToList();
            Assert.Equal(2, tabs.Count);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>Selecting a different customer KEEPS the page</b> (user decision). An operator comparing
    /// licences across customers stays in licences; being thrown back to contact details on every
    /// selection would make the switch feel like it undoes itself.
    /// </summary>
    [Fact]
    public Task ChangingCustomerKeepsTheCurrentPage() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            manager.SaveCustomer("ACME");
            manager.SaveCustomer("Umbrella");

            var window = Show(manager, out var shell);
            ViewProbe.ShowLicencesPage(window, shell);
            Assert.True(shell.IsLicencesTab);

            shell.SelectedCustomer = shell.Customers.Last();
            window.UpdateLayout();

            Assert.True(shell.IsLicencesTab);
            Assert.True(Page(window, "LicencesPage").IsEffectivelyVisible);
        }, default);

    /// <summary>
    /// ⭐⭐ Opening a licence from the LICENCES VIEW lands on the customer's LICENCES page.
    ///
    /// <para>⚠ The one place where leaving the page as it was is the wrong answer: the operator asked to
    /// open a LICENCE, so arriving on the customer's contact details would answer a question they did not
    /// ask and hide the row they double-clicked.</para>
    /// </summary>
    [Fact]
    public Task OpeningALicenceFromTheBrowserLandsOnTheLicencesPage() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var window = Show(manager, out var shell);
            Assert.True(shell.IsCustomerTab, "The pane should start on Customer for this to prove anything.");

            shell.ShowLicensesCommand.Execute(null);
            shell.Browser.SelectedLicense = shell.Browser.Results.First();
            shell.OpenSelectedLicenseCommand.Execute(null);
            window.UpdateLayout();

            Assert.False(shell.IsLicensesView);
            Assert.True(shell.IsLicencesTab);
            Assert.True(Page(window, "LicencesPage").IsEffectivelyVisible);
        }, default);

    /// <summary>
    /// ⚠ Nothing is clipped on either page, measured on the realised layout (#386) — and measured on BOTH,
    /// because a page that is not showing is not laid out, so checking one proves nothing about the other.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NothingIsClippedOnEitherPage(bool customerPage) =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var window = Show(manager, out var shell);
            shell.SelectedLicense = shell.Licenses.First();

            if (customerPage)
            {
                ViewProbe.ShowCustomerPage(window, shell);
            }
            else
            {
                ViewProbe.ShowLicencesPage(window, shell);
            }

            foreach (var control in window.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button or TextBox or ComboBox)
                         .Where(c => c.IsEffectivelyVisible))
            {
                var name = control.Name ?? (control as ContentControl)?.Content as string
                    ?? $"(unnamed {control.GetType().Name})";

                Assert.True(control.Bounds.Width > 0, $"{name} was not laid out at all");

                var origin = control.TranslatePoint(new Point(0, 0), window);
                Assert.True(origin.HasValue, $"{name} is not connected to the window's visual tree");
                Assert.True(origin!.Value.X >= 0, $"{name} starts off the left edge");

                var right = origin.Value.X + control.Bounds.Width;
                Assert.True(
                    right <= window.Bounds.Width + 1,
                    $"{name} ends at x={right:0.#} in a window {window.Bounds.Width:0.#} wide — clipped");
            }
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static StackPanel Page(Window window, string name) =>
        window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == name);

    /// <summary>
    /// A REAL click: a mouse press and release over the button, through the headless input stack.
    ///
    /// <para>⚠⚠ <b>Measured, and the first attempt was wrong in a way worth recording.</b> Raising
    /// <c>Button.ClickEvent</c> with <c>RaiseEvent</c> looks like a click and is not one — it raises the
    /// routed event without going through the control's own click handling, so the bound
    /// <c>Command</c> never runs. These guards caught it immediately, which is exactly what a guard
    /// written against the GESTURE rather than the property is for.</para>
    /// </summary>
    private static void Click(Window window, Button button)
    {
        window.UpdateLayout();

        var origin = button.TranslatePoint(new Point(0, 0), window);
        Assert.True(origin.HasValue, $"{button.Name} is not connected to the window's visual tree.");
        Assert.True(button.Bounds.Width > 0, $"{button.Name} was not laid out, so it cannot be clicked.");

        var centre = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.UpdateLayout();
    }

    /// <summary>
    /// A caption is on the page that owns it — and ⛔ is NOT anywhere effectively visible outside it.
    /// </summary>
    private static void AssertOnPage(Window window, string page, string caption)
    {
        var owner = Page(window, page);

        var found = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == caption)
            .Where(t => t.IsEffectivelyVisible)
            .ToList();

        Assert.True(found.Count > 0, $"'{caption}' is not visible on {page}.");
        Assert.All(found, t => Assert.Contains(owner, t.GetVisualAncestors()));
    }

    /// <summary>The same, for an action.</summary>
    private static void AssertActionOnPage(Window window, string page, string content)
    {
        var owner = Page(window, page);

        var found = window.GetVisualDescendants().OfType<Button>()
            .Where(b => (b.Content as string) == content)
            .Where(b => b.IsEffectivelyVisible)
            .ToList();

        Assert.True(found.Count > 0, $"The '{content}' action is not visible on {page}.");
        Assert.All(found, b => Assert.Contains(owner, b.GetVisualAncestors()));
    }

    private static MainWindow Show(ManagerFixture manager, out ShellViewModel shell)
    {
        if (manager.Register.GetCustomers().Count == 0)
        {
            manager.SaveCustomer();
        }

        shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.SelectedCustomer = shell.Customers.First();
        window.UpdateLayout();
        return window;
    }
}
