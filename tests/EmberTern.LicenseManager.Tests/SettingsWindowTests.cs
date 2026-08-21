using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The Settings window as it is actually realised.
///
/// <para>⚠⚠ <b>Every test here returns its <c>Task</c></b> (gotcha #374): the expression-bodied
/// <c>void</c> form compiles, discards the <c>Task</c>, and every assertion inside becomes dead.</para>
///
/// <para>⚠ <b>Every control is located by NAME</b> (gotcha #379) — never "the first <c>TextBox</c> in the
/// window", which is a guard on the window's inventory rather than on its subject.</para>
///
/// <para>⛔ This class joins <see cref="ManagerHeadlessCollection"/> rather than taking its own fixture:
/// an <c>IClassFixture</c> would silently create a SECOND headless session in the process, which is
/// gotchas #94 / #226 / #286.</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class SettingsWindowTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session;
    private readonly string _folder;

    public SettingsWindowTests(ManagerHeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
        _folder = Path.Combine(Path.GetTempPath(), "etlm-smtp-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheWindowBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);
            var window = Show();

            Assert.NotNull(window.Content);
            Assert.Equal(
                HeadlessTheme.Brush("BackgroundBrush")!.Color,
                ((ISolidColorBrush)window.Background!).Color);
            Assert.Equal(
                HeadlessTheme.Brush("ForegroundBrush")!.Color,
                ((ISolidColorBrush)window.Foreground!).Color);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>Gotcha #386, asserted geometrically rather than trusted.</b> A fixed-width window does not
    /// report an overflow: the control is present, named, bound and working, and simply laid out past
    /// the edge where the window clips it — which a user review reported as "the button is missing".
    ///
    /// <para>⛔ The repair for a failure here is never to shrink the controls or truncate the labels.</para>
    /// </summary>
    [Fact]
    public Task EveryVisibleControlIsLaidOutInsideTheWindow() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            window.UpdateLayout();

            foreach (var control in window.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button or TextBox or ComboBox)
                         .Where(c => c.IsEffectivelyVisible))
            {
                var name = control.Name ?? $"(unnamed {control.GetType().Name})";
                Assert.True(control.Bounds.Width > 0, $"{name} was not laid out at all");

                var origin = control.TranslatePoint(new Point(0, 0), window);
                Assert.True(origin.HasValue, $"{name} is not connected to the window's visual tree");
                Assert.True(
                    origin!.Value.X >= 0,
                    $"{name} starts at x={origin.Value.X:0.#}, off the left edge");

                var right = origin.Value.X + control.Bounds.Width;
                Assert.True(
                    right <= window.Bounds.Width,
                    $"{name} ends at x={right:0.#} in a window {window.Bounds.Width:0.#} wide — clipped");
            }
        }, default);

    /// <summary>
    /// ⭐⭐ <b>Gotcha #385: a type selector does not reach a subclass.</b> <c>SelectableTextBlock</c>
    /// derives from <c>TextBlock</c>, so <c>Selector="TextBlock.mono"</c> misses it in complete silence —
    /// no binding error, no warning, just the inherited font. The style file uses
    /// <c>:is(TextBlock).mono</c>; this reads the font back off the REALISED control to prove it.
    /// </summary>
    [Fact]
    public Task TheStoredPathActuallyRendersInTheCodeFont() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            window.UpdateLayout();

            var path = ViewProbe.Named<SelectableTextBlock>(window, "SettingsPathText");
            var expected = (FontFamily)Application.Current!.FindResource("Font.Code")!;

            Assert.Equal(expected.Name, path.FontFamily.Name);
        }, default);

    /// <summary>
    /// ⭐ The window must say where the file really is, read off the real paths — a hard-coded sentence
    /// would keep reading correctly after the location changed underneath it.
    /// </summary>
    [Fact]
    public Task TheWindowNamesTheRealSettingsFile() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;

            Assert.Equal(
                Path.Combine(_folder, "smtp.dat"),
                ViewProbe.Named<SelectableTextBlock>(window, "SettingsPathText").Text);
            Assert.Equal(model.SettingsPath, Path.Combine(_folder, "smtp.dat"));
        }, default);

    /// <summary>
    /// ⭐ The password field must hide what is typed. ⚠ Asserted on the realised control rather than on
    /// the markup: <c>PasswordChar</c> is the property that actually masks, and a field that lost it
    /// would look identical in every other respect.
    /// </summary>
    [Fact]
    public Task ThePasswordFieldIsMasked() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();

            Assert.NotEqual('\0', ViewProbe.Named<TextBox>(window, "PasswordBox").PasswordChar);

            // ⭐ Positive control: an ordinary field is NOT masked, so the assertion above is about this
            //    field rather than about every TextBox in the application.
            Assert.Equal('\0', ViewProbe.Named<TextBox>(window, "HostBox").PasswordChar);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>The summary follows the FIELDS, not the saved file.</b> An operator who has typed an
    /// address but no server must be told that file delivery is available — that is the sentence that
    /// tells them they are done.
    /// </summary>
    [Fact]
    public Task TheDeliverySummaryFollowsWhatIsTyped() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;
            var summary = ViewProbe.Named<TextBlock>(window, "DeliverySummaryText");

            Assert.Contains("Not configured", summary.Text, StringComparison.OrdinalIgnoreCase);

            model.FromAddress = "licencje@example.com";
            window.UpdateLayout();
            Assert.Contains("File delivery only", summary.Text, StringComparison.OrdinalIgnoreCase);

            model.Host = "smtp.example.com";
            window.UpdateLayout();
            Assert.Contains("Direct sending", summary.Text, StringComparison.OrdinalIgnoreCase);
        }, default);

    /// <summary>
    /// ⭐ Saving reports through the ONE message surface, and a refusal names every problem at once —
    /// an operator fixing four fields one message at a time is four round trips.
    /// </summary>
    [Fact]
    public Task SavingAnIncompleteConfigurationRefusesThroughTheMessageStrip() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;

            model.Username = "ktos";
            model.Password = "sekret";
            model.SaveCommand.Execute(null);
            window.UpdateLayout();

            Assert.True(model.IsWarning);
            Assert.True(
                ViewProbe.Named<TextBlock>(window, "EmailSettingsMessageText").IsEffectivelyVisible);
            Assert.False(File.Exists(model.SettingsPath));
        }, default);

    /// <summary>⭐ The round trip an operator actually performs: type, save, reopen, see it again.</summary>
    [Fact]
    public Task SavedSettingsComeBackWhenTheWindowIsOpenedAgain() =>
        _session.Dispatch(() =>
        {
            var first = Show();
            var model = (SettingsViewModel)first.DataContext!;

            model.FromAddress = "licencje@example.com";
            model.FromName = "EmberTern";
            model.Host = "smtp.example.com";
            model.Username = "licencje@example.com";
            model.Password = "app-password";
            model.SaveCommand.Execute(null);

            Assert.True(model.IsSuccess);

            var reopened = Show();
            var reloaded = (SettingsViewModel)reopened.DataContext!;

            Assert.Equal("licencje@example.com", reloaded.FromAddress);
            Assert.Equal("smtp.example.com", reloaded.Host);
            Assert.Equal("app-password", reloaded.Password);
            Assert.Null(reloaded.Message);
        }, default);

    /// <summary>
    /// ⭐⭐ <b>"There are no settings yet" must not look like a fault.</b> A first run opens quiet — no
    /// warning, no error — which is the distinction the store exists to make.
    /// </summary>
    [Fact]
    public Task AFirstRunOpensQuiet() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            Assert.Null(model.Message);
            Assert.False(model.HasMessage);
        }, default);

    /// <summary>
    /// ⛔ A file that could not be understood must NOT fill the form with recovered fragments: showing
    /// them beside an error invites a save that overwrites whatever is really there.
    /// </summary>
    [Fact]
    public Task AnUnreadableFileShowsAnErrorAndLeavesTheFormEmpty() =>
        _session.Dispatch(() =>
        {
            File.WriteAllText(Path.Combine(_folder, "smtp.dat"), "not a settings file at all");

            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            Assert.True(model.IsError);
            Assert.Empty(model.Host);
            Assert.Empty(model.FromAddress);
            Assert.Empty(model.Password);
        }, default);

    // -- L6.1a QA: Forget settings is CONFIRMED -------------------------------------------------------

    /// <summary>
    /// Confirming forgets the configuration - the form and the file together, so the two cannot disagree.
    /// </summary>
    [Fact]
    public Task ForgettingAfterConfirmationClearsBothTheFormAndTheFile() =>
        _session.Dispatch<bool>(async () =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;
            model.Confirm = _ => Task.FromResult(true);

            model.FromAddress = "licencje@example.com";
            model.SaveCommand.Execute(null);
            Assert.True(File.Exists(model.SettingsPath));

            await model.ForgetCommand.ExecuteAsync(null);

            Assert.False(File.Exists(model.SettingsPath));
            Assert.Empty(model.FromAddress);
            Assert.Equal(SmtpSettings.DefaultPort, model.Port);
            return true;
        }, default);

    /// <summary>
    /// <b>Cancel changes NOTHING.</b> The file, the form and the message strip are all exactly as they
    /// were - asserted on all three, because "the file survived" alone would still allow a form that had
    /// been blanked underneath the operator.
    /// </summary>
    [Fact]
    public Task CancellingTheConfirmationLeavesTheConfigurationIntact() =>
        _session.Dispatch<bool>(async () =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;
            model.Confirm = _ => Task.FromResult(false);

            model.FromAddress = "licencje@example.com";
            model.Host = "smtp.example.com";
            model.Username = "licencje@example.com";
            model.Password = "app-password";
            model.SaveCommand.Execute(null);

            var before = File.ReadAllBytes(model.SettingsPath);
            var messageBefore = model.Message;

            await model.ForgetCommand.ExecuteAsync(null);

            Assert.True(File.Exists(model.SettingsPath));
            Assert.Equal(before, File.ReadAllBytes(model.SettingsPath));
            Assert.Equal("licencje@example.com", model.FromAddress);
            Assert.Equal("smtp.example.com", model.Host);
            Assert.Equal("app-password", model.Password);
            Assert.Same(messageBefore, model.Message);

            // The settings are still READABLE, password included - cancel did not touch the DPAPI blob.
            var reopened = (SettingsViewModel)Show().DataContext!;
            Assert.Equal("app-password", reopened.Password);
            return true;
        }, default);

    /// <summary>
    /// The operator is actually ASKED, and asked the right question. A confirmation whose text does not
    /// name the consequence is a dialog people learn to click through.
    /// </summary>
    [Fact]
    public Task ForgettingAsksBeforeItActs() =>
        _session.Dispatch<bool>(async () =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            ConfirmRequest? asked = null;
            model.Confirm = request =>
            {
                asked = request;
                return Task.FromResult(false);
            };

            model.FromAddress = "licencje@example.com";
            model.SaveCommand.Execute(null);

            await model.ForgetCommand.ExecuteAsync(null);

            Assert.NotNull(asked);

            // ⭐ Asserted through the view model that actually renders the request, not on the request's
            //   keys: since L8.2 a ConfirmRequest carries KEYS, so reading the words back is the only way
            //   to prove the dialog still says what it said — including that the arguments arrive.
            var words = new ConfirmViewModel(asked!);

            Assert.Contains("Forget", words.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("permanently", words.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("password", words.Message, StringComparison.OrdinalIgnoreCase);

            // The action's button NAMES the action rather than saying "OK" or "Yes".
            Assert.Contains("Forget", words.ConfirmLabel, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Cancel", words.CancelLabel);
            return true;
        }, default);

    /// <summary>
    /// <b>With no confirmer wired the action REFUSES rather than proceeding.</b> That is the half worth
    /// pinning: proceeding would mean a destructive action silently losing its guard the moment a view
    /// forgot to attach one - and every other test here would still be green.
    /// </summary>
    [Fact]
    public Task ForgettingRefusesWhenNoConfirmationCanBeShown() =>
        _session.Dispatch<bool>(async () =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            model.FromAddress = "licencje@example.com";
            model.SaveCommand.Execute(null);
            Assert.True(File.Exists(model.SettingsPath));

            model.Confirm = null;
            await model.ForgetCommand.ExecuteAsync(null);

            Assert.True(File.Exists(model.SettingsPath));
            Assert.Equal("licencje@example.com", model.FromAddress);
            Assert.True(model.IsWarning);
            return true;
        }, default);

    /// <summary>
    /// The window WIRES the confirmer. Without this the four guards above would all be about a delegate
    /// the real application never assigns - and the running app would refuse to forget anything.
    /// </summary>
    [Fact]
    public Task TheWindowSuppliesTheConfirmer() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            Assert.NotNull(model.Confirm);
        }, default);

    /// <summary>The confirmation dialog builds and reads back what it was asked to say.</summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task TheConfirmationDialogBuildsInBothThemes(string theme) =>
        _session.Dispatch(() =>
        {
            HeadlessTheme.UseTheme(theme);

            var request = new ConfirmRequest(
                ConfirmCatalog.ForgetSmtpTitle,
                ConfirmCatalog.ForgetSmtpMessage,
                ConfirmCatalog.ForgetSmtpAction);
            var model = new ConfirmViewModel(request);
            var dialog = new ConfirmDialog { DataContext = model };
            dialog.Show();
            dialog.UpdateLayout();

            // ⚠ Compared against the RESOLVED words, because the request now carries keys — and the four
            //   controls must show what the view model resolves, which is the whole binding claim.
            Assert.Equal(model.Title, ViewProbe.Named<TextBlock>(dialog, "ConfirmTitle").Text);
            Assert.Equal(model.Message, ViewProbe.Named<TextBlock>(dialog, "ConfirmMessage").Text);
            Assert.Equal(model.ConfirmLabel, ViewProbe.Named<Button>(dialog, "ConfirmAccept").Content);
            Assert.Equal(model.CancelLabel, ViewProbe.Named<Button>(dialog, "ConfirmCancel").Content);

            Assert.Equal(
                HeadlessTheme.Brush("BackgroundBrush")!.Color,
                ((ISolidColorBrush)dialog.Background!).Color);

            dialog.Close();
        }, default);

    /// <summary>
    /// Escape leaves without acting, and the way out is the CANCEL button rather than the action - the
    /// gesture a hesitating operator reaches for first must be the safe one.
    /// </summary>
    [Fact]
    public Task TheDialogsDefaultAndCancelAreTheRightWayRound() =>
        _session.Dispatch(() =>
        {
            var dialog = new ConfirmDialog
            {
                DataContext = new ConfirmViewModel(
                    new ConfirmRequest(
                        ConfirmCatalog.ForgetSmtpTitle,
                        ConfirmCatalog.ForgetSmtpMessage,
                        ConfirmCatalog.ForgetSmtpAction)),
            };
            dialog.Show();

            Assert.True(ViewProbe.Named<Button>(dialog, "ConfirmCancel").IsCancel);
            Assert.False(ViewProbe.Named<Button>(dialog, "ConfirmAccept").IsCancel);
            Assert.True(ViewProbe.Named<Button>(dialog, "ConfirmAccept").IsDefault);

            dialog.Close();
        }, default);

    /// <summary>
    /// The view model's answer defaults to NO: anything that is not an explicit confirmation - including
    /// closing the dialog by its title bar - must mean "do not do it".
    /// </summary>
    [Fact]
    public void AnUnansweredConfirmationMeansNo()
    {
        var model = new ConfirmViewModel(new ConfirmRequest(
            ConfirmCatalog.ForgetSmtpTitle, ConfirmCatalog.ForgetSmtpMessage, ConfirmCatalog.ForgetSmtpAction));

        Assert.False(model.Result);

        model.CancelCommand.Execute(null);
        Assert.False(model.Result);

        model.ConfirmCommand.Execute(null);
        Assert.True(model.Result);
    }

    /// <summary>
    /// Opens the window on the E-mail page — where every setting L6.1 shipped now lives.
    ///
    /// <para>⚠ The page is selected through the view model rather than by clicking the navigation, and
    /// that is deliberate for the tests that are ABOUT the e-mail form: the click itself is exercised by
    /// <see cref="ClickingTheNavigationSwitchesPages"/>, so it is proved once rather than assumed
    /// everywhere.</para>
    /// </summary>

    // -- L6.1a: the Settings shape --------------------------------------------------------------------

    /// <summary>
    /// The window opens on General, and exactly one page is on screen.
    ///
    /// <para>WARNING <c>IsEffectivelyVisible</c>, never <c>IsVisible</c> (gotcha #387): the latter is a
    /// control's OWN declared value, so every child of a collapsed panel still reports true - and written
    /// the wrong way round the assertion passes vacuously forever.</para>
    /// </summary>
    [Fact]
    public Task TheWindowOpensOnGeneralAndShowsExactlyOnePage() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            Assert.Equal("general", model.SelectedPage.Id);
            Assert.True(ViewProbe.Named<StackPanel>(window, "GeneralPage").IsEffectivelyVisible);
            Assert.False(ViewProbe.Named<StackPanel>(window, "EmailPage").IsEffectivelyVisible);
        }, default);

    /// <summary>
    /// The navigation is driven by a real selection on the realised list, not only by the view model - a
    /// guard that set the property alone would stay green over a list that no longer selects anything.
    /// </summary>
    [Fact]
    public Task SelectingInTheNavigationSwitchesPages() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var model = (SettingsViewModel)window.DataContext!;

            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = 1;
            window.UpdateLayout();

            Assert.Equal("email", model.SelectedPage.Id);
            Assert.True(ViewProbe.Named<StackPanel>(window, "EmailPage").IsEffectivelyVisible);
            Assert.False(ViewProbe.Named<StackPanel>(window, "GeneralPage").IsEffectivelyVisible);
        }, default);

    /// <summary>The heading follows the page, so the two can never name different things.</summary>
    [Fact]
    public Task TheHeadingNamesThePageThatIsShowing() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var heading = ViewProbe.Named<TextBlock>(window, "PageHeading");

            Assert.Equal("General", heading.Text);

            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = 1;
            window.UpdateLayout();

            Assert.Equal("E-mail", heading.Text);
        }, default);

    /// <summary>
    /// <b>Decision D-8, asserted on the realised control.</b> The interface-language row is SHOWN so the
    /// structure is real and a later localization stage has a place to land, and it is DISABLED because
    /// nothing is behind it - a preference the operator can set that then changes nothing is the defect
    /// that removed <c>ClientLibraryPath</c> from EmberTern's connection dialog.
    /// </summary>
    [Fact]
    public Task TheApplicationLanguagePickerIsVisibleButDisabled() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            var picker = ViewProbe.Named<ComboBox>(window, "ApplicationLanguagePicker");

            Assert.True(picker.IsEffectivelyVisible);
            Assert.False(picker.IsEnabled);
            Assert.Equal(2, picker.ItemCount);

            // The reason is on screen, not only in a tooltip - a disabled control that does not say why
            // reads as broken.
            var note = ViewProbe.Named<TextBlock>(window, "ApplicationLanguageNote");
            Assert.True(note.IsEffectivelyVisible);
            Assert.Contains("later stage", note.Text, StringComparison.OrdinalIgnoreCase);
        }, default);

    /// <summary>
    /// Nothing the interface-language row shows may reach the settings file. The view model exposes no
    /// setter at all, so this asserts the ABSENCE of a path rather than the behaviour of one.
    /// </summary>
    [Fact]
    public Task TheApplicationLanguageIsNotWrittenAnywhere() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;

            model.FromAddress = "licencje@example.com";
            model.SaveCommand.Execute(null);

            var stored = File.ReadAllText(model.SettingsPath);
            Assert.DoesNotContain("applicationLanguage", stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("uiLanguage", stored, StringComparison.OrdinalIgnoreCase);

            Assert.Null(typeof(SettingsViewModel)
                .GetProperty(nameof(SettingsViewModel.ApplicationLanguage))!.SetMethod);
        }, default);

    /// <summary>D-9: a first run offers Polish, and the picker offers both languages.</summary>
    [Fact]
    public Task TheMessageLanguageDefaultsToPolish() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;
            var picker = ViewProbe.Named<ComboBox>(window, "MessageLanguagePicker");

            Assert.Equal("pl", model.MessageLanguage.Code);
            Assert.Equal("Polski", model.MessageLanguage.Label);
            Assert.True(picker.IsEffectivelyVisible);
            Assert.True(picker.IsEnabled);
            Assert.Equal(2, picker.ItemCount);
        }, default);

    /// <summary>The chosen message language survives a save and a reopen.</summary>
    [Fact]
    public Task TheMessageLanguageIsStoredAndComesBack() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var model = (SettingsViewModel)window.DataContext!;

            model.FromAddress = "licencje@example.com";
            model.MessageLanguage = model.MessageLanguageOptions.Single(o => o.Code == "en");
            model.SaveCommand.Execute(null);

            Assert.True(model.IsSuccess);

            var reopened = (SettingsViewModel)ShowEmailPage().DataContext!;
            Assert.Equal("en", reopened.MessageLanguage.Code);
            Assert.Equal("English", reopened.MessageLanguage.Label);
        }, default);

    /// <summary>
    /// The footer belongs to the E-mail page - the rule StorageWindow established: an action is never on
    /// screen beside a page it cannot act on.
    /// </summary>
    [Fact]
    public Task TheSaveActionIsOnlyOnScreenForThePageItActsOn() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            window.UpdateLayout();

            Assert.False(ViewProbe.Named<Button>(window, "SaveSettings").IsEffectivelyVisible);

            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = 1;
            window.UpdateLayout();

            Assert.True(ViewProbe.Named<Button>(window, "SaveSettings").IsEffectivelyVisible);
        }, default);

    /// <summary>
    /// The geometric guard (#386) run on the General page too - a page is only laid out while it is the
    /// one showing, so checking one page proves nothing about the other.
    /// </summary>
    [Fact]
    public Task EveryVisibleControlOnGeneralIsLaidOutInsideTheWindow() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            window.UpdateLayout();

            foreach (var control in window.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button or TextBox or ComboBox or ListBox)
                         .Where(c => c.IsEffectivelyVisible))
            {
                var name = control.Name ?? $"(unnamed {control.GetType().Name})";
                Assert.True(control.Bounds.Width > 0, $"{name} was not laid out at all");

                var origin = control.TranslatePoint(new Point(0, 0), window);
                Assert.True(origin.HasValue, $"{name} is not connected to the window's visual tree");
                Assert.True(origin!.Value.X >= 0, $"{name} starts off the left edge");

                var right = origin.Value.X + control.Bounds.Width;
                Assert.True(
                    right <= window.Bounds.Width,
                    $"{name} ends at x={right:0.#} in a window {window.Bounds.Width:0.#} wide - clipped");
            }
        }, default);


    // -- L6.1a QA: ONE STABLE SIZE ---------------------------------------------------------------------

    /// <summary>
    /// <b>The window is the same size on both pages, and switching does not resize it.</b>
    ///
    /// <para>The reported defect: the window declared <c>SizeToContent="Height"</c>, so it measured
    /// whichever page was showing - General opened small, E-mail opened much taller than the screen, and
    /// navigating resized the window under the operator. This asserts the repair on the REALISED bounds,
    /// not on the markup.</para>
    /// </summary>
    [Fact]
    public Task SwitchingPagesDoesNotChangeTheWindowSize() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            window.UpdateLayout();
            var onGeneral = window.Bounds.Size;

            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = 1;
            window.UpdateLayout();
            var onEmail = window.Bounds.Size;

            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = 0;
            window.UpdateLayout();
            var backOnGeneral = window.Bounds.Size;

            Assert.Equal(onGeneral, onEmail);
            Assert.Equal(onGeneral, backOnGeneral);

            // Positive control: the window really was laid out, so the equalities above are not three
            // readings of an unmeasured zero (#378).
            Assert.True(onGeneral.Width > 0 && onGeneral.Height > 0);
        }, default);

    /// <summary>
    /// The size is DECLARED, never derived. <c>SizeToContent</c> is what made the height a function of the
    /// page, so its absence is the property worth pinning - a future edit that reintroduces it would pass
    /// every other test here.
    /// </summary>
    [Fact]
    public Task TheWindowSizeIsDeclaredRatherThanMeasuredFromContent() =>
        _session.Dispatch(() =>
        {
            var window = Show();

            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.True(window.Width > 0, "The window declares no width.");
            Assert.True(window.Height > 0, "The window declares no height.");
            Assert.True(window.MinHeight > 0, "The window declares no minimum height.");
        }, default);

    /// <summary>
    /// <b>Only the content scrolls.</b> The navigation, the heading and the footer must never be inside
    /// the scrolled surface - otherwise a long page carries the whole window away with it.
    /// </summary>
    [Fact]
    public Task OnlyTheContentPaneScrolls() =>
        _session.Dispatch(() =>
        {
            var window = Show();
            window.UpdateLayout();

            var scroll = ViewProbe.Named<ScrollViewer>(window, "PageScroll");

            // Each of these must be OUTSIDE the scrolled surface.
            foreach (var name in new[] { "PageList", "PageHeading", "SaveSettings" })
            {
                var control = window.GetVisualDescendants().OfType<Control>()
                    .Single(c => c.Name == name);

                Assert.False(
                    control.GetVisualAncestors().Contains(scroll),
                    $"{name} sits inside PageScroll - it would scroll with the page.");
            }

            // Positive control: something IS inside it, so the assertions above are not vacuous.
            var page = ViewProbe.Named<StackPanel>(window, "GeneralPage");
            Assert.Contains(scroll, page.GetVisualAncestors());
        }, default);

    /// <summary>
    /// The E-mail page is taller than the space it is given, and the viewer can actually scroll it - the
    /// realised extent, not the declaration.
    /// </summary>
    [Fact]
    public Task TheEmailPageContentCanBeScrolled() =>
        _session.Dispatch(() =>
        {
            var window = ShowEmailPage();
            var scroll = ViewProbe.Named<ScrollViewer>(window, "PageScroll");

            // WARNING Without this the guard could pass on an UNMEASURED viewport: Extent > 0 > Viewport is
            // trivially true, and the assertion below would then be reporting nothing at all (#378).
            Assert.True(
                scroll.Viewport.Height > 0,
                "The scroll viewer was never laid out, so nothing below this measures anything.");

            Assert.True(
                scroll.Extent.Height > scroll.Viewport.Height,
                $"The E-mail page fits ({scroll.Extent.Height:0.#} <= {scroll.Viewport.Height:0.#}), so "
                + "this guard no longer measures anything - re-check it rather than deleting it.");

            scroll.ScrollToEnd();
            window.UpdateLayout();
            Assert.True(scroll.Offset.Y > 0, "The content did not move when scrolled to the end.");
        }, default);

    /// <summary>
    /// Nothing is clipped HORIZONTALLY on either page, measured on the realised layout (#386). Vertical
    /// overflow is expected and is what the scroll viewer is for.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public Task NothingIsClippedHorizontallyOnEitherPage(int page) =>
        _session.Dispatch(() =>
        {
            var window = Show();
            ViewProbe.Named<ListBox>(window, "PageList").SelectedIndex = page;
            window.UpdateLayout();

            foreach (var control in window.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button or TextBox or ComboBox or ListBox)
                         .Where(c => c.IsEffectivelyVisible))
            {
                var name = control.Name ?? $"(unnamed {control.GetType().Name})";
                var origin = control.TranslatePoint(new Point(0, 0), window);
                Assert.True(origin.HasValue, $"{name} is not connected to the window's visual tree");
                Assert.True(origin!.Value.X >= 0, $"{name} starts off the left edge");

                var right = origin.Value.X + control.Bounds.Width;
                Assert.True(
                    right <= window.Bounds.Width + 1,
                    $"{name} ends at x={right:0.#} in a window {window.Bounds.Width:0.#} wide - clipped");
            }
        }, default);

    /// <summary>
    /// The declared height stays within a modest working area, so the window fits a small laptop screen
    /// before the runtime clamp has to do anything. The clamp only ever SHRINKS, and there is no screen to
    /// query in a headless session - so this measures the declaration, which is the thing under control.
    /// </summary>
    [Fact]
    public Task TheDeclaredSizeFitsASmallScreen() =>
        _session.Dispatch(() =>
        {
            var window = Show();

            // 1366x768 minus a taskbar - the smallest display this application is expected to meet.
            Assert.True(window.Height <= 700, $"The window declares {window.Height:0} points of height.");
            Assert.True(window.Width <= 1300, $"The window declares {window.Width:0} points of width.");
        }, default);

    private SettingsWindow ShowEmailPage()
    {
        var window = Show();
        var model = (SettingsViewModel)window.DataContext!;
        model.SelectedPage = model.Pages.Single(p => p.Id == "email");
        window.UpdateLayout();
        return window;
    }

    private SettingsWindow Show()
    {
        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel(
                new SmtpSettingsStore(Path.Combine(_folder, "smtp.dat"))),
        };
        window.Show();
        return window;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
