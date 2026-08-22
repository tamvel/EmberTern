using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The Send licence window as it is actually realised, and the button that opens it.
///
/// <para>⚠⚠ <b>Every test here returns its <c>Task</c></b> (gotchas #374 / #391): the expression-bodied
/// <c>void</c> form compiles, discards the <c>Task</c>, and every assertion inside becomes dead.</para>
///
/// <para>⚠ Controls are located by NAME (#379), and the licences page is ACTIVATED before anything on it
/// is measured or clicked (#390) — a page that is not showing is not laid out, and a click at its
/// coordinates lands on whatever is in the corner.</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture:
/// an <c>IClassFixture</c> would silently create a SECOND headless session in the process (#94/#226/#286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class SendLicenceWindowTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;
    private readonly ManagerFixture _manager = new();

    public SendLicenceWindowTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    private static SmtpSettings Settings => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        MessageLanguage = MessageLanguages.Polish,
    };

    private SendLicenceViewModel Model(SmtpSettings? settings = null)
    {
        var use = settings ?? Settings;
        var customer = _manager.SaveCustomer("Żółć Sp. z o.o.", "biuro@zolc.test");
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        return new SendLicenceViewModel(
            LicenseMessageComposer.Compose(
                _manager.Register.GetCurrentArtifact(licence.LicenseId)!, customer, use),
            use,
            new LicenceDelivery(_manager.Register));
    }

    private static SendLicenceWindow Show(SendLicenceViewModel model)
    {
        var window = new SendLicenceWindow { DataContext = model };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheWindowBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);
            var window = Show(Model());

            Assert.NotNull(window.Content);
            Assert.Equal(
                HeadlessTheme.Brush("BackgroundBrush")!.Color,
                ((ISolidColorBrush)window.Background!).Color);
            Assert.Equal(
                HeadlessTheme.Brush("ForegroundBrush")!.Color,
                ((ISolidColorBrush)window.Foreground!).Color);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The window shows the message that will be sent</b> — read off the REALISED controls, not off
    /// the view model. A binding path typo produces an empty field and a green view-model test.
    /// </summary>
    [Fact]
    public Task EveryPartOfTheMessageIsOnScreen() =>
        _session.Dispatch(() =>
        {
            var model = Model();
            var window = Show(model);

            Assert.Contains(
                "biuro@zolc.test",
                ViewProbe.Named<SelectableTextBlock>(window, "RecipientText").Text!,
                StringComparison.Ordinal);
            Assert.Contains(
                "licencje@example.test",
                ViewProbe.Named<SelectableTextBlock>(window, "SenderText").Text!,
                StringComparison.Ordinal);
            Assert.Equal(
                model.Composed.Subject,
                ViewProbe.Named<SelectableTextBlock>(window, "SubjectText").Text);
            Assert.Contains(
                "EmberTern.etlic",
                ViewProbe.Named<SelectableTextBlock>(window, "AttachmentText").Text!,
                StringComparison.Ordinal);
            Assert.Equal(
                model.Composed.TextBody,
                ViewProbe.Named<TextBox>(window, "BodyPreview").Text);
        }, default);

    /// <summary>⛔ The preview cannot be edited into something else before it is sent.</summary>
    [Fact]
    public Task ThePreviewIsReadOnly() =>
        _session.Dispatch(() =>
        {
            var window = Show(Model());
            Assert.True(ViewProbe.Named<TextBox>(window, "BodyPreview").IsReadOnly);
        }, default);

    /// <summary>
    /// ⚠ With no server the Send button is disabled and the file route is not — the state an operator
    /// reaches by configuring a sender address and nothing else.
    /// </summary>
    [Fact]
    public Task WithNoServerSendIsDisabledAndSavingIsNot() =>
        _session.Dispatch(() =>
        {
            var window = Show(Model(Settings with { Host = string.Empty }));

            Assert.False(ViewProbe.Named<Button>(window, "SendLicence").IsEnabled);
            Assert.True(ViewProbe.Named<Button>(window, "SaveEml").IsEnabled);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The button on the main window is exercised as a real GESTURE</b> (#389): a routed
    /// <c>Click</c> event would not run the handler chain the way a click does, and this handler is the
    /// only path to the send window.
    ///
    /// <para>⚠ The assertion is what happens with e-mail UNCONFIGURED — the state of a fresh manager — so
    /// no window opens and the shell explains why on its own message strip. That is the refusal
    /// <see cref="ShellViewModel.PrepareSendLicence"/> exists to make, proved through the button.</para>
    /// </summary>
    [Fact]
    public Task TheMainWindowButtonAsksTheShellAndReportsARefusal() =>
        _session.Dispatch(() =>
        {
            var customer = _manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");
            var licence = _manager.SaveLicense(customer);
            _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

            var shell = new ShellViewModel(
                _manager.Register, _manager.Session, _manager.Paths, () => _manager.Now);

            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.SelectedCustomer = shell.Customers.First(c => c.CustomerId == customer.CustomerId);
            ViewProbe.ShowLicencesPage(window, shell);
            shell.SelectedLicense = shell.Licenses.First();
            window.UpdateLayout();

            var button = ViewProbe.Named<Button>(window, "SendLicenceButton");
            Assert.True(button.Bounds.Width > 0, "The Send licence button was never laid out.");

            var origin = button.TranslatePoint(new Point(0, 0), window);
            Assert.True(origin.HasValue);
            var centre = origin!.Value + new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);

            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            window.UpdateLayout();

            // ⚠ This manager has no smtp.dat, so the shell refuses and says so rather than opening a
            //   window that could only apologise.
            Assert.True(shell.IsWarning, shell.MessageText);
            Assert.Contains("E-mail is not configured", shell.MessageText, StringComparison.Ordinal);
        }, default);

    public void Dispose() => _manager.Dispose();
}
