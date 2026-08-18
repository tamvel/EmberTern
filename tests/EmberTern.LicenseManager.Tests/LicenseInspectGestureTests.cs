using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// P1-c — double-clicking a licence in the customer view previews its newest artifact.
///
/// <para>⭐⭐ The gesture runs <c>InspectLatestCommand</c> and nothing else. These tests assert that by
/// comparing what the DOUBLE-CLICK produced with what the COMMAND produces from the same state — a
/// second implementation that happened to word its message identically would still be caught by the
/// never-issued case, which is the one a copy always gets wrong.</para>
///
/// <para>⚠⚠ Every test returns its <c>Task</c> (gotcha #374). ⛔ Joins
/// <see cref="ManagerHeadlessCollection"/>, never its own fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LicenseInspectGestureTests
{
    private readonly HeadlessUnitTestSession _session;

    /// <summary>Takes the shared session.</summary>
    public LicenseInspectGestureTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public Task DoubleClickingAnIssuedLicenceSaysWhatEmberTernWouldMakeOfIt() =>
        _session.Dispatch(() =>
        {
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            var licence = manager.SaveLicense(customer);
            manager.Workflow.Issue(manager.Session, licence, customer, "P1-c test");

            var (window, shell) = Show(manager);

            // What the button does from this exact state, recorded first…
            shell.InspectLatestCommand.Execute(null);
            var fromTheButton = shell.Message;

            shell.Message = null;
            DoubleClickTheFirstLicence(window);

            // …and the gesture has to reach the same place.
            Assert.NotNull(shell.Message);
            Assert.Equal(fromTheButton!.Severity, shell.Message!.Severity);
            Assert.Equal(fromTheButton.Text, shell.Message.Text);
            Assert.Contains("EmberTern would", shell.Message.Text, StringComparison.Ordinal);
        }, default);

    [Fact]
    public Task DoubleClickingALicenceThatWasNeverIssuedExplainsWhyThereIsNothingToShow() =>
        _session.Dispatch(() =>
        {
            // ⭐⭐ THE CASE THAT PROVES THERE IS ONE IMPLEMENTATION. A gesture wired to "open the preview"
            //   rather than to the command would have to answer this itself, and the honest failure mode
            //   is silence — a double-click that does nothing and says nothing.
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var (window, shell) = Show(manager);
            DoubleClickTheFirstLicence(window);

            Assert.NotNull(shell.Message);
            Assert.Equal(MessageSeverity.Warning, shell.Message!.Severity);
            Assert.Equal("This licence has never been issued.", shell.Message.Text);
        }, default);

    [Fact]
    public Task DoubleClickingTheEmptySpaceBelowTheRowsPreviewsNothing() =>
        _session.Dispatch(() =>
        {
            // ⚠ `DoubleTapped` bubbles from the list's own background too. Re-opening the previously
            //   selected licence because the operator double-clicked past the last row is a preview
            //   nobody asked for.
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var (window, shell) = Show(manager);
            var list = List(window);

            shell.Message = null;
            list.RaiseEvent(new RoutedEventArgsProbe(list));

            Assert.Null(shell.Message);
        }, default);

    [Fact]
    public Task TheRowGestureIsWiredToTheListThatShowsACustomersLicences() =>
        _session.Dispatch(() =>
        {
            // ⭐ Names the surface, so moving the gesture to a different list is a decision somebody has
            //   to make rather than something that quietly stops working.
            using var manager = new ManagerFixture();
            var customer = manager.SaveCustomer();
            manager.SaveLicense(customer);

            var (window, _) = Show(manager);

            var list = List(window);
            Assert.Equal("CustomerLicenses", list.Name);
            Assert.All(list.Items, item => Assert.IsType<LicenseRecord>(item));
        }, default);

    /// <summary>
    /// Raises <see cref="InputElement.DoubleTappedEvent"/> from the first licence ROW.
    ///
    /// <para>⚠ Raised on the row rather than driven through synthesised mouse input on purpose: the
    /// double-click INTERVAL is a platform gesture-recogniser concern, and a test that depends on two
    /// synthetic clicks landing inside it is a test that fails for reasons that have nothing to do with
    /// this window. What is ours is the routing and the handler, and this exercises both — including the
    /// ancestor guard, since the event travels up from the row exactly as the real one does.</para>
    /// </summary>
    private static void DoubleClickTheFirstLicence(Window window)
    {
        var row = List(window).GetVisualDescendants().OfType<ListBoxItem>().First();
        row.RaiseEvent(new RoutedEventArgsProbe(row));
    }

    private static ListBox List(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "CustomerLicenses");

    private static (MainWindow Window, ShellViewModel Shell) Show(ManagerFixture manager)
    {
        var shell = new ShellViewModel(manager.Register, manager.Session, manager.Paths, () => manager.Now);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        shell.SelectedCustomer = shell.Customers.First();
        shell.SelectedLicense = shell.Licenses.First();
        window.UpdateLayout();
        return (window, shell);
    }

    /// <summary>A double-tap, addressed to the element it is raised on.</summary>
    private sealed class RoutedEventArgsProbe : TappedEventArgs
    {
        internal RoutedEventArgsProbe(Interactive source)
            : base(InputElement.DoubleTappedEvent, null!) => Source = source;
    }
}
