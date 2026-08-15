using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
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
/// ⭐ <b>The two checklist items a static scan cannot reach: the windows actually build, and they build
/// in BOTH themes.</b>
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
    public void TheUnlockWindowBuildsInBothThemes(string theme) => _session.Dispatch(() =>
    {
        UseTheme(theme);

        using var manager = new ManagerFixture();
        var window = new UnlockWindow { DataContext = new UnlockViewModel(manager.Paths) };
        window.Show();

        Assert.NotNull(window.Content);
        Assert.Equal("EmberTern License Manager", window.Title);
    }, default);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void TheMainWindowBuildsInBothThemes(string theme) => _session.Dispatch(() =>
    {
        UseTheme(theme);

        using var manager = new ManagerFixture();
        var customer = manager.SaveCustomer();
        manager.SaveLicense(customer);

        var shell = new ShellViewModel(manager.Register, manager.Session);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        Assert.NotNull(window.Content);
        Assert.Single(shell.Customers);
        Assert.Equal("R1", shell.SigningKeyId);
    }, default);

    [Fact]
    public void TheSameBrushResolvesDifferentlyInEachTheme() => _session.Dispatch(() =>
    {
        // ⭐⭐ THE test that makes "renders correctly in both themes" mean something. If the linked
        //     Colors.axaml were not being found, both lookups would return the same fallback and every
        //     other test in this file would still pass.
        UseTheme("Dark");
        var dark = Brush("BackgroundBrush");

        UseTheme("Light");
        var light = Brush("BackgroundBrush");

        Assert.NotNull(dark);
        Assert.NotNull(light);
        Assert.NotEqual(dark!.Color, light!.Color);
    }, default);

    [Fact]
    public void TheWindowChromeIsPaintedFromTokensRatherThanDefaults() => _session.Dispatch(() =>
    {
        UseTheme("Dark");

        using var manager = new ManagerFixture();
        var window = new MainWindow
        {
            DataContext = new ShellViewModel(manager.Register, manager.Session),
        };
        window.Show();

        Assert.Equal(Brush("BackgroundBrush")!.Color, ((ISolidColorBrush)window.Background!).Color);
        Assert.Equal(Brush("ForegroundBrush")!.Color, ((ISolidColorBrush)window.Foreground!).Color);
    }, default);

    [Fact]
    public void IssuingThroughTheViewModelProducesAVerifiableArtifact() => _session.Dispatch(() =>
    {
        // The whole L3 loop driven the way a person drives it: pick a customer, save terms, issue.
        using var manager = new ManagerFixture();
        var shell = new ShellViewModel(manager.Register, manager.Session, () => manager.Now);

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

    private static void UseTheme(string theme) =>
        Application.Current!.RequestedThemeVariant =
            theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static ISolidColorBrush? Brush(string key)
    {
        var application = Application.Current!;
        return application.TryFindResource(key, application.ActualThemeVariant, out var value) &&
               value is ISolidColorBrush brush
            ? brush
            : null;
    }
}
