using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The licences view, realised.
///
/// <para>⚠⚠ <b>EVERY TEST HERE RETURNS ITS <c>Task</c>, AND THAT IS LOAD-BEARING.</b>
/// <c>HeadlessUnitTestSession.Dispatch</c> returns a <c>Task</c>; written as <c>public void X() =&gt;
/// _session.Dispatch(…)</c> it compiles, the <c>Task</c> is DISCARDED, xUnit never awaits it, and no
/// assertion inside the lambda can fail the test. L3 shipped five tests in exactly that shape and all
/// five were green while proving nothing (gotcha #374). ⛔ Never write one of these as <c>void</c>.</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture:
/// one headless session per PROCESS (#94 / #226 / #286), and an <c>IClassFixture</c> would quietly make
/// a second one.</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicensesViewTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicensesViewTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheLicencesViewBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 3);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(shell.IsLicensesView);
            Assert.Equal(3, shell.Browser.Results.Count);
            Assert.Equal("3 licences.", shell.Browser.ResultSummary);
        }, default);

    [Fact]
    public Task ExactlyOneViewIsVisibleAtATime() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 1);
            var window = Show(shell);

            // ⚠ Asserted on the REALISED tree, not on the flags: the two views share one Grid cell, and
            //    "both visible" would be an overlap the flags cannot see.
            Assert.True(shell.IsCustomersView);
            Assert.True(CustomerRail(window).IsEffectivelyVisible);
            Assert.False(FilterCard(window).IsEffectivelyVisible);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            Assert.False(CustomerRail(window).IsEffectivelyVisible);
            Assert.True(FilterCard(window).IsEffectivelyVisible);

            shell.ShowCustomersCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(CustomerRail(window).IsEffectivelyVisible);
            Assert.False(FilterCard(window).IsEffectivelyVisible);
        }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheCurrentTabIsPaintedAsRaisedAndTheOtherIsNot(string theme) =>
        _session.Dispatch(() =>
        {
            // ⭐ The `.active` class is only worth having if it reaches the pixel. A class that is set and
            //   painted by nobody looks exactly like a class that is working, in every test that checks
            //   the class instead of the brush.
            HeadlessTheme.UseTheme(theme);

            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 1);
            var window = Show(shell);

            var raised = HeadlessTheme.Brush("SurfaceRaisedBrush")!.Color;

            Assert.Contains("active", Tab(window, "CustomersTab").Classes);
            Assert.DoesNotContain("active", Tab(window, "LicensesTab").Classes);
            Assert.Equal(raised, TabFill(window, "CustomersTab"));
            Assert.NotEqual(raised, TabFill(window, "LicensesTab"));

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            Assert.Contains("active", Tab(window, "LicensesTab").Classes);
            Assert.DoesNotContain("active", Tab(window, "CustomersTab").Classes);
            Assert.Equal(raised, TabFill(window, "LicensesTab"));
            Assert.NotEqual(raised, TabFill(window, "CustomersTab"));
        }, default);

    [Fact]
    public Task AFilterDropdownShowsItsLabelAndNotTheShapeOfItsRecord() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ Gotcha #370: `x:DataType` on a DataTemplate is also its MATCHING type, so a template
            //     that stops matching produces NO binding error — the host silently renders the item's
            //     ToString(), i.e. "ExpiryFilter { Label = …, WithinDays = … }" where a label belonged.
            //     So this asserts the text the tree actually renders, never what the XAML spells.
            HeadlessTheme.UseTheme("Dark");

            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 1);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            // ⚠ The FIRST TextBlock in a Fluent ComboBox is its placeholder, and it is empty here — so
            //   the assertion targets the first one that actually says something. The fallback this
            //   guards against is not silence, it is the record's ToString(), which is very much
            //   non-empty.
            var rendered = Dropdowns(window)
                .Select(c => c.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text)
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? string.Empty)
                .ToArray();

            Assert.Equal(
                [
                    shell.Browser.StatusFilters[0].Label,
                    shell.Browser.ExpiryFilters[0].Label,
                    shell.Browser.IssuingFilters[0].Label,
                ],
                rendered);
        }, default);

    [Fact]
    public Task SwitchingToLicencesReReadsTheRegister() =>
        _session.Dispatch(() =>
        {
            // ⚠ The browser is filled when the view is opened, not when the shell is built. A view that
            //   shows what the register held at start-up is a view that lies after the first save.
            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 1);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();
            Assert.Single(shell.Browser.Results);

            var customer = manager.Register.GetCustomers()[0];
            manager.SaveLicense(customer);

            shell.ShowCustomersCommand.Execute(null);
            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();

            Assert.Equal(2, shell.Browser.Results.Count);
        }, default);

    [Fact]
    public Task OpeningASelectedLicenceLandsOnItsCustomerWithThatLicencePicked() =>
        _session.Dispatch(() =>
        {
            // ⭐ Without this the search is a dead end — the operator finds what lapses on Friday and has
            //   nowhere to go with it.
            using var manager = new ManagerFixture();

            var acme = manager.SaveCustomer("ACME Sp. z o.o.");
            var beta = manager.SaveCustomer("Beta");
            manager.SaveLicense(acme);
            var target = manager.SaveLicense(beta);

            var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            shell.Browser.SearchText = "Beta";
            window.UpdateLayout();

            shell.Browser.SelectedLicense = Assert.Single(shell.Browser.Results);
            shell.OpenSelectedLicenseCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(shell.IsCustomersView);
            Assert.Equal(beta.CustomerId, shell.SelectedCustomer!.CustomerId);
            Assert.Equal(target.LicenseId, shell.SelectedLicense!.LicenseId);
            Assert.True(CustomerRail(window).IsEffectivelyVisible);
        }, default);

    [Fact]
    public Task OpeningWithNothingSelectedSaysSoInsteadOfSwitchingViews() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 1);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            shell.Browser.SelectedLicense = null;
            shell.OpenSelectedLicenseCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(shell.IsLicensesView);
            Assert.True(shell.IsWarning);
        }, default);

    [Fact]
    public Task TheDetailStripAppearsOnlyWhenSomethingIsSelected() =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme("Light");

            using var manager = new ManagerFixture();
            var shell = Shell(manager, licences: 2);
            var window = Show(shell);

            shell.ShowLicensesCommand.Execute(null);
            window.UpdateLayout();
            Assert.False(shell.Browser.HasSelection);

            shell.Browser.SelectedLicense = shell.Browser.Results[0];
            window.UpdateLayout();

            Assert.True(shell.Browser.HasSelection);
            Assert.NotEqual(string.Empty, shell.Browser.SelectedLicenseId);
        }, default);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ShellViewModel Shell(ManagerFixture manager, int licences)
    {
        var customer = manager.SaveCustomer();
        for (var i = 0; i < licences; i++)
        {
            manager.SaveLicense(customer);
        }

        return new ShellViewModel(manager.Register, manager.Session, () => manager.Now);
    }

    private static MainWindow Show(ShellViewModel shell)
    {
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Button Tab(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);

    private static Color TabFill(Window window, string name) =>
        Tab(window, name).GetVisualDescendants().OfType<ContentPresenter>()
            .Select(p => p.Background)
            .OfType<ISolidColorBrush>()
            .First()
            .Color;

    private static Border CustomerRail(Window window) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("rail"));

    // The filter card is the first card inside the licences view; the customers view has cards too, so
    // it is identified by what only it contains — the three filter dropdowns.
    private static Control FilterCard(Window window) =>
        Dropdowns(window).First().GetVisualAncestors().OfType<Border>()
            .First(b => b.Classes.Contains("card"));

    private static ComboBox[] Dropdowns(Window window) =>
        [.. window.GetVisualDescendants().OfType<ComboBox>()];
}
